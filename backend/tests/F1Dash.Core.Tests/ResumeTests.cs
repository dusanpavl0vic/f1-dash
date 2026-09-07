using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using F1Dash.Core.Signalr;
using F1Dash.Server.Realtime;
using Xunit;

namespace F1Dash.Core.Tests;

/// <summary>
/// A reconnecting client asks to resume from the last sequence it saw. Getting
/// this wrong is invisible: the dashboard keeps rendering, just from state that
/// silently diverged. So each branch is pinned.
/// </summary>
public class ResumeTests
{
    private static TopicUpdate Update(string value) =>
        new("TimingData",
            JsonNode.Parse("{\"Lines\":{\"1\":{\"Position\":\"" + value + "\"}}}")!.AsObject(),
            null, false);

    private static List<JsonObject> Sent(FakeSocket socket) =>
        [.. socket.Frames.Select(f => (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(f))!)];

    [Fact]
    public async Task Resume_sends_only_the_deltas_the_client_missed()
    {
        var state = new LiveSessionState();
        for (var i = 1; i <= 5; i++) state.Apply(Update(i.ToString()));

        var socket = new FakeSocket();
        var client = new ClientConnection(socket, 64);

        var resumed = state.AddAndCatchUp(client, since: 3);
        client.Complete();
        await client.WriteLoopAsync(CancellationToken.None);

        Assert.True(resumed);

        var frames = Sent(socket);
        Assert.All(frames, f => Assert.Equal("delta", (string?)f["type"]));
        Assert.Equal([4L, 5L], frames.Select(f => (long)f["seq"]!));
    }

    [Fact]
    public async Task A_client_further_behind_than_the_backlog_gets_a_snapshot()
    {
        var state = new LiveSessionState();
        // The backlog holds 600 frames; 700 puts sequence 1 out of reach.
        for (var i = 1; i <= 700; i++) state.Apply(Update(i.ToString()));

        var socket = new FakeSocket();
        var client = new ClientConnection(socket, 2048);

        var resumed = state.AddAndCatchUp(client, since: 1);
        client.Complete();
        await client.WriteLoopAsync(CancellationToken.None);

        Assert.False(resumed);
        Assert.Equal("snapshot", (string?)Sent(socket)[0]["type"]);
    }

    [Fact]
    public async Task A_session_change_forces_a_snapshot_even_for_a_recent_client()
    {
        var state = new LiveSessionState();
        for (var i = 1; i <= 5; i++) state.Apply(Update(i.ToString()));

        // The backlog describes the outgoing session. Replaying it into the new
        // one is exactly the cross-session merge that put two cars on P1.
        state.Reset();

        var socket = new FakeSocket();
        var client = new ClientConnection(socket, 64);

        var resumed = state.AddAndCatchUp(client, since: 5);
        client.Complete();
        await client.WriteLoopAsync(CancellationToken.None);

        Assert.False(resumed);
        Assert.Equal("snapshot", (string?)Sent(socket)[0]["type"]);
    }

    [Fact]
    public async Task A_first_connection_gets_a_snapshot()
    {
        var state = new LiveSessionState();
        state.Apply(Update("1"));

        var socket = new FakeSocket();
        var client = new ClientConnection(socket, 64);

        var resumed = state.AddAndCatchUp(client, since: 0);
        client.Complete();
        await client.WriteLoopAsync(CancellationToken.None);

        Assert.False(resumed);
        Assert.Equal("snapshot", (string?)Sent(socket)[0]["type"]);
    }
}


/// <summary>A socket that keeps what was written, so frames can be asserted on.</summary>
internal sealed class FakeSocket : WebSocket
{
    public List<byte[]> Frames { get; } = [];

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

    public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c)
    {
        Frames.Add([.. b]);
        return Task.CompletedTask;
    }
}
