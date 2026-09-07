using System.Runtime.CompilerServices;
using F1Dash.Core.Archive;
using F1Dash.Core.Signalr;

namespace F1Dash.Server.Ingest;

/// <summary>
/// Replays a recorded session at its original pace, so the whole stack runs
/// out of season with no live feed and no network access to F1.
/// </summary>
public sealed class ReplaySessionSource(
    string streamPath,
    double speed = 1.0,
    long startOffsetMs = 0,
    bool loop = false,
    ReplayController? controller = null) : ISessionSource
{
    /// <summary>Transport state, when the caller supplied one.</summary>
    public ReplayController? Controller => controller;

    public string Description =>
        $"replay {Path.GetFileName(Path.GetDirectoryName(streamPath))} at {speed}x" + (loop ? " (looping)" : "");

    /// <summary>
    /// Blocks while paused, keeping the pacing clock still so resuming does not
    /// fast-forward through everything that was missed.
    /// </summary>
    private async Task HoldForPauseAsync(CancellationToken ct, Action<TimeSpan> compensate)
    {
        while (controller is { Playing: false })
        {
            var pausedAt = DateTime.UtcNow;
            await Task.Delay(100, ct).ConfigureAwait(false);
            compensate(DateTime.UtcNow - pausedAt);
        }
    }

    public async IAsyncEnumerable<TopicUpdate> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        do
        {
            var wallStart = DateTime.UtcNow;
            Task HoldWhilePausedAsync() => HoldForPauseAsync(ct, paused => wallStart += paused);
            var pacingStarted = false;
            var emitted = 0;

            foreach (var entry in SessionDownloader.ReadJsonl(streamPath))
            {
                ct.ThrowIfCancellationRequested();

                // Everything before the start offset is applied IMMEDIATELY
                // rather than skipped. State is delta-accumulated, so there is
                // no way to read the state at time t without replaying to it —
                // the same reason a backward seek is a full rebuild (docs/08).
                //
                // Skipping instead of rebuilding is why an earlier run showed
                // "No session" with nine unnamed drivers: SessionInfo,
                // DriverList and LapCount are all one-time topics emitted in
                // the opening seconds.
                if (entry.OffsetMs < startOffsetMs)
                {
                    yield return new TopicUpdate(entry.Topic, entry.Payload, null, IsSnapshot: emitted == 0);
                    emitted++;
                    continue;
                }

                if (controller is not null && entry.OffsetMs > controller.DurationMs)
                {
                    controller.DurationMs = entry.OffsetMs;
                }

                // The rebuild above runs at full speed, so the wall clock for
                // pacing starts at the first entry that is actually due.
                if (!pacingStarted)
                {
                    wallStart = DateTime.UtcNow;
                    pacingStarted = true;
                }

                await HoldWhilePausedAsync();

                var currentSpeed = controller?.Speed ?? speed;

                // Pace against the wall clock rather than sleeping per entry:
                // entries cluster tightly and a per-entry sleep would drift
                // badly across two hours.
                var sessionElapsed = TimeSpan.FromMilliseconds((entry.OffsetMs - startOffsetMs) / currentSpeed);
                var behind = sessionElapsed - (DateTime.UtcNow - wallStart);
                if (behind > TimeSpan.FromMilliseconds(5))
                {
                    await Task.Delay(behind, ct).ConfigureAwait(false);
                }

                // Checked AGAIN after the wait. A pause arriving while the delay
                // is in flight would otherwise let one more entry through, and
                // between sparse pre-race entries that single entry can carry
                // fourteen seconds of session time — which looks exactly like a
                // pause that does not work.
                await HoldWhilePausedAsync();

                controller?.SetPosition(entry.OffsetMs);

                // The first entry seeds state the way the live feed's "R"
                // snapshot does, so append-only topics index from it correctly.
                yield return new TopicUpdate(entry.Topic, entry.Payload, null, IsSnapshot: emitted == 0);
                emitted++;
            }
        }
        while (loop && !ct.IsCancellationRequested);
    }
}
