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
    bool loop = false) : ISessionSource
{
    public string Description =>
        $"replay {Path.GetFileName(Path.GetDirectoryName(streamPath))} at {speed}x" + (loop ? " (looping)" : "");

    public async IAsyncEnumerable<TopicUpdate> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        do
        {
            var wallStart = DateTime.UtcNow;
            var emitted = 0;

            foreach (var entry in SessionDownloader.ReadJsonl(streamPath))
            {
                ct.ThrowIfCancellationRequested();

                if (entry.OffsetMs < startOffsetMs) continue;

                // Pace against the wall clock rather than sleeping per entry:
                // entries cluster tightly and a per-entry sleep would drift
                // badly across two hours.
                var sessionElapsed = TimeSpan.FromMilliseconds((entry.OffsetMs - startOffsetMs) / speed);
                var behind = sessionElapsed - (DateTime.UtcNow - wallStart);
                if (behind > TimeSpan.FromMilliseconds(5))
                {
                    await Task.Delay(behind, ct).ConfigureAwait(false);
                }

                // The first entry seeds state the way the live feed's "R"
                // snapshot does, so append-only topics index from it correctly.
                yield return new TopicUpdate(entry.Topic, entry.Payload, null, IsSnapshot: emitted == 0);
                emitted++;
            }
        }
        while (loop && !ct.IsCancellationRequested);
    }
}
