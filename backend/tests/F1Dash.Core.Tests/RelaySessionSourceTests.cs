using F1Dash.Server.Ingest;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace F1Dash.Core.Tests;

public class RelaySessionSourceTests
{
    private static string Frame(long seq, string topic, string payload) =>
        $"{{\"seq\":{seq},\"topic\":\"{topic}\",\"payload\":{payload}}}";

    [Fact]
    public void A_well_formed_frame_parses()
    {
        Assert.True(RelaySessionSource.TryParseFrame(
            Frame(7, "TimingData", "{\"Lines\":{\"1\":{\"Position\":\"1\"}}}"),
            out var seq, out var update));

        Assert.Equal(7, seq);
        Assert.Equal("TimingData", update.Topic);
        Assert.Equal("1", (string?)update.Payload["Lines"]!["1"]!["Position"]);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"seq\":1}")]                                   // no topic
    [InlineData("{\"seq\":1,\"topic\":\"X\"}")]                    // no payload
    [InlineData("{\"seq\":1,\"topic\":\"X\",\"payload\":\"str\"}")] // payload not an object
    public void Malformed_frames_are_rejected_rather_than_crashing(string text)
    {
        // Anything reaching this endpoint is remote input. A malformed frame
        // must be discarded, never allowed to take down the session.
        Assert.False(RelaySessionSource.TryParseFrame(text, out _, out _));
    }

    [Fact]
    public async Task Frames_already_seen_are_ignored()
    {
        // A reconnecting collector resends from the last acknowledged sequence,
        // and its boundary is inclusive — so duplicates are the normal case,
        // not a fault. Applying them twice would double-count append-only
        // topics like race control messages.
        var relay = new RelaySessionSource(NullLogger.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        foreach (var seq in (long[])[1, 2, 3])
        {
            RelaySessionSource.TryParseFrame(
                Frame(seq, "TimingData", $"{{\"n\":{seq}}}"), out var s, out var u);
            await relay.OfferAsync(s, u, cts.Token);
        }

        // Replayed.
        RelaySessionSource.TryParseFrame(Frame(2, "TimingData", "{\"n\":99}"), out var s2, out var u2);
        await relay.OfferAsync(s2, u2, cts.Token);

        Assert.Equal(3, relay.LastSequence);

        var seen = new List<int>();
        await foreach (var update in relay.ReadAsync(cts.Token))
        {
            seen.Add((int)update.Payload["n"]!);
            if (seen.Count == 3) break;
        }

        Assert.Equal([1, 2, 3], seen);
    }

    [Fact]
    public void A_disconnect_does_not_end_the_session()
    {
        // A collector on a home connection will drop. Ending the session over a
        // dropped link would mean a Wi-Fi blip ends the race.
        var relay = new RelaySessionSource(NullLogger.Instance);

        relay.CollectorConnected();
        Assert.True(relay.Connected);

        relay.CollectorDisconnected();
        Assert.False(relay.Connected);
        Assert.Contains("waiting", relay.Description, StringComparison.Ordinal);
    }
}
