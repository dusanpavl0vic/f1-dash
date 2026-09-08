using System.Runtime.CompilerServices;
using F1Dash.Core.Signalr;

namespace F1Dash.Server.Ingest;

/// <summary>
/// Tries each live source in turn and stays on the first one that actually
/// delivers data.
///
/// "Delivers data" rather than "connects" is the whole point. The SignalR
/// endpoint will happily complete a handshake and then send nothing — that is
/// exactly the failure this project has hit — so a source that has connected
/// but produced no update inside the probation window is treated as failed.
///
/// Once a source has produced its first update it is trusted for the rest of
/// the session: falling back mid-race because of a quiet minute under a red
/// flag would be worse than the problem.
/// </summary>
public sealed class FailoverSessionSource(
    IReadOnlyList<ISessionSource> sources,
    ILogger logger,
    TimeSpan? probation = null) : ISessionSource
{
    private readonly TimeSpan _probation = probation ?? TimeSpan.FromSeconds(20);

    /// <summary>Which source is actually feeding the session, once one is.</summary>
    public string? Active { get; private set; }

    public string Description => Active ?? $"live (trying {sources.Count} sources)";

    public async IAsyncEnumerable<TopicUpdate> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var last = i == sources.Count - 1;

            logger.LogInformation("Trying live source: {Source}", source.Description);

            var produced = false;

            await foreach (var update in ProbeAsync(source, last, ct).ConfigureAwait(false))
            {
                if (!produced)
                {
                    produced = true;
                    Active = source.Description;
                    logger.LogInformation("Live source active: {Source}", source.Description);
                }

                yield return update;
            }

            // The source ended after delivering data: the session is over, not
            // broken. Falling through to a lesser source would replay it.
            if (produced) yield break;

            if (!last) logger.LogWarning("{Source} produced nothing; falling back.", source.Description);
        }

        logger.LogError("No live source produced any data.");
    }

    /// <summary>
    /// Yields from one source, abandoning it if the first update does not
    /// arrive within the probation window.
    /// </summary>
    private async IAsyncEnumerable<TopicUpdate> ProbeAsync(
        ISessionSource source, bool last, [EnumeratorCancellation] CancellationToken ct)
    {
        // The last source gets no deadline. There is nothing to fall back to,
        // so cutting it off would only turn a slow start into no data at all.
        using var deadline = last
            ? new CancellationTokenSource()
            : new CancellationTokenSource(_probation);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        var enumerator = source.ReadAsync(linked.Token).GetAsyncEnumerator(linked.Token);
        try
        {
            while (true)
            {
                TopicUpdate current;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) yield break;
                    current = enumerator.Current;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // The probation deadline, not a shutdown.
                    yield break;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "{Source} failed", source.Description);
                    yield break;
                }

                // The first update means the source works, so the probation
                // timer is DISABLED — not cancelled. Cancelling this token
                // would cancel the linked token and tear down the very stream
                // that just proved itself healthy.
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}
