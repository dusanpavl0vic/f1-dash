using F1Dash.Server.Realtime;

namespace F1Dash.Server.Ingest;

/// <summary>
/// The ingest loop. Reads from whichever source is configured and applies every
/// update to the shared state.
///
/// The source is an interface on purpose: the replay source and the live
/// SignalR source are interchangeable, so the path exercised every day in
/// development is the same path that runs on race day.
/// </summary>
public sealed class IngestService(
    ISessionSource source,
    LiveSessionState state,
    ILogger<IngestService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingest starting: {Source}", source.Description);

        var backoff = TimeSpan.FromSeconds(1);
        var maxBackoff = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = 0;
                await foreach (var update in source.ReadAsync(stoppingToken).ConfigureAwait(false))
                {
                    state.Apply(update);

                    // Reset the backoff once data is genuinely flowing, not
                    // merely on a successful connect.
                    if (++count == 50) backoff = TimeSpan.FromSeconds(1);
                }

                logger.LogInformation("Source completed after {Count} updates", count);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Never clear accumulated state on a reconnect: a fresh snapshot
                // overwrites it wholesale anyway, and keeping it means the
                // dashboard does not blank out during a brief blip.
                logger.LogWarning(e, "Ingest source failed; retrying in {Backoff}", backoff);

                await Task.Delay(Jitter(backoff), stoppingToken).ConfigureAwait(false);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
            }
        }
    }

    /// <summary>±20% jitter so reconnecting clients do not synchronise.</summary>
    private static TimeSpan Jitter(TimeSpan value) =>
        value * (0.8 + (Random.Shared.NextDouble() * 0.4));
}
