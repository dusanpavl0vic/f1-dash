using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using F1Dash.Core.Signalr;
using F1Dash.Server.Ingest;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace F1Dash.Core.Tests;

public class FailoverSessionSourceTests
{
    private static TopicUpdate Update(string value) =>
        new("TimingData", JsonNode.Parse("{\"v\":\"" + value + "\"}")!.AsObject(), null, false);

    [Fact]
    public async Task A_source_that_connects_but_sends_nothing_is_abandoned()
    {
        // The exact observed failure: the handshake completes and then silence.
        // "Connected" is not the test — "produced an update" is.
        var failover = new FailoverSessionSource(
            [new SilentSource(), new WorkingSource(["a", "b"])],
            NullLogger.Instance,
            probation: TimeSpan.FromMilliseconds(150));

        var seen = new List<string>();
        await foreach (var u in failover.ReadAsync(CancellationToken.None))
        {
            seen.Add((string)u.Payload["v"]!);
        }

        Assert.Equal(["a", "b"], seen);
        Assert.Equal("working", failover.Active);
    }

    [Fact]
    public async Task A_healthy_source_is_not_cut_off_when_probation_expires()
    {
        // The probation timer must be disabled once the source proves itself,
        // not left armed to kill a working stream mid-session.
        var failover = new FailoverSessionSource(
            [new WorkingSource(["a", "b", "c"], gap: TimeSpan.FromMilliseconds(120))],
            NullLogger.Instance,
            probation: TimeSpan.FromMilliseconds(100));

        var seen = new List<string>();
        await foreach (var u in failover.ReadAsync(CancellationToken.None))
        {
            seen.Add((string)u.Payload["v"]!);
        }

        Assert.Equal(["a", "b", "c"], seen);
    }

    [Fact]
    public async Task A_source_that_throws_falls_through()
    {
        var failover = new FailoverSessionSource(
            [new ThrowingSource(), new WorkingSource(["x"])],
            NullLogger.Instance,
            probation: TimeSpan.FromMilliseconds(150));

        var seen = new List<string>();
        await foreach (var u in failover.ReadAsync(CancellationToken.None))
        {
            seen.Add((string)u.Payload["v"]!);
        }

        Assert.Equal(["x"], seen);
    }

    [Fact]
    public async Task A_source_that_finishes_after_delivering_ends_the_session()
    {
        // A completed session must not fall through to the next source and be
        // replayed from the beginning.
        var second = new WorkingSource(["should-not-appear"]);
        var failover = new FailoverSessionSource(
            [new WorkingSource(["a"]), second], NullLogger.Instance);

        var seen = new List<string>();
        await foreach (var u in failover.ReadAsync(CancellationToken.None))
        {
            seen.Add((string)u.Payload["v"]!);
        }

        Assert.Equal(["a"], seen);
        Assert.False(second.Started);
    }

    // ---------------------------------------------------------------- fakes

    private sealed class SilentSource : ISessionSource
    {
        public string Description => "silent";

        public async IAsyncEnumerable<TopicUpdate> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            yield break;
        }
    }

    private sealed class ThrowingSource : ISessionSource
    {
        public string Description => "throwing";

        public async IAsyncEnumerable<TopicUpdate> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            throw new HttpRequestException("refused");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class WorkingSource(string[] values, TimeSpan? gap = null) : ISessionSource
    {
        public bool Started { get; private set; }
        public string Description => "working";

        public async IAsyncEnumerable<TopicUpdate> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            Started = true;
            foreach (var value in values)
            {
                if (gap is { } delay) await Task.Delay(delay, ct);
                else await Task.Yield();

                yield return Update(value);
            }
        }
    }
}
