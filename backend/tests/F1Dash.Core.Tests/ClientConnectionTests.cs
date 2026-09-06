using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using F1Dash.Core.Signalr;
using F1Dash.Server.Realtime;
using Xunit;

namespace F1Dash.Core.Tests;

public class ClientConnectionTests
{
    private static ReadOnlyMemory<byte> Frame(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Offer_AcceptsUpToCapacity()
    {
        var client = new ClientConnection(new DummySocket(), capacity: 3);

        Assert.True(client.Offer(Frame("a")));
        Assert.True(client.Offer(Frame("b")));
        Assert.True(client.Offer(Frame("c")));
        Assert.False(client.Overflowed);
    }

    [Fact]
    public void Offer_RefusesBeyondCapacityAndMarksOverflow()
    {
        // A slow client is dropped, not accommodated. One stalled browser tab
        // must never back up the shared reader for everyone else.
        var client = new ClientConnection(new DummySocket(), capacity: 2);
        client.Offer(Frame("a"));
        client.Offer(Frame("b"));

        Assert.False(client.Offer(Frame("c")));
        Assert.True(client.Overflowed);
    }

    [Fact]
    public void Overflow_RefusesRatherThanEvictingTheOldest()
    {
        // Dropping the OLDEST delta would silently corrupt the client's state:
        // it would keep receiving patches for a state it never got. Refusing
        // lets us close the socket and force a clean resync instead.
        var client = new ClientConnection(new DummySocket(), capacity: 1);
        client.Offer(Frame("first"));
        client.Offer(Frame("second"));

        Assert.True(client.Overflowed);
    }
}

public class LiveSessionStateTests
{
    private static TopicUpdate Update(string topic, string json) =>
        new(topic, JsonNode.Parse(json)!.AsObject(), null, false);

    [Fact]
    public void SnapshotFrame_CarriesSequenceAndServerTime()
    {
        var state = new LiveSessionState();
        state.Apply(Update("TrackStatus", """{"Status":"1"}"""));

        var frame = JsonNode.Parse(Encoding.UTF8.GetString(state.SnapshotFrame()))!;

        Assert.Equal("snapshot", (string?)frame["type"]);
        Assert.Equal(1, (long?)frame["seq"]);
        // The delay buffer needs serverTime to correct for clock skew.
        Assert.NotNull((long?)frame["serverTime"]);
        Assert.Equal("1", (string?)frame["data"]!["TrackStatus"]!["Status"]);
    }

    [Fact]
    public void SnapshotFrame_IsCachedUntilTheNextDelta()
    {
        // Rebuilding and re-serialising a 1-2 MB state on every delta is the
        // single largest cost in the naive design.
        var state = new LiveSessionState();
        state.Apply(Update("TrackStatus", """{"Status":"1"}"""));

        var before = state.SnapshotFrame();
        Assert.Same(before, state.SnapshotFrame());

        // A new delta invalidates the cache; the next snapshot is rebuilt once
        // and then cached again.
        state.Apply(Update("TrackStatus", """{"Status":"4"}"""));
        var after = state.SnapshotFrame();
        Assert.NotSame(before, after);
        Assert.Same(after, state.SnapshotFrame());
    }

    [Fact]
    public void Sequence_IncrementsDenselySoClientsCanDetectGaps()
    {
        var state = new LiveSessionState();
        for (var i = 0; i < 5; i++) state.Apply(Update("TrackStatus", $$"""{"Status":"{{i}}"}"""));

        Assert.Equal(5, state.Sequence);
    }

    [Fact]
    public void SlowClient_IsDroppedFromTheBroadcastSet()
    {
        var state = new LiveSessionState();
        var client = new ClientConnection(new DummySocket(), capacity: 2);
        state.Add(client);

        for (var i = 0; i < 10; i++) state.Apply(Update("TrackStatus", $$"""{"Status":"{{i}}"}"""));

        Assert.True(client.Overflowed);
        Assert.Equal(0, state.ClientCount);
    }
}

/// <summary>A socket that is never actually written to; the write loop is not under test.</summary>
internal sealed class DummySocket : WebSocket
{
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override WebSocketState State => WebSocketState.Open;
    public override string? SubProtocol => null;
    public override void Abort() { }
    public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
    public override void Dispose() { }
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
        Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
    public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c) =>
        Task.CompletedTask;
}
