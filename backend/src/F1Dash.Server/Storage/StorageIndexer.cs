using System.Text.Json;
using F1Dash.Core.Analysis;

namespace F1Dash.Server.Storage;

public sealed record IndexReport(
    int SessionsIndexed,
    int SessionsSkipped,
    int TelemetryDriversIndexed,
    IReadOnlyList<string> Stores,
    IReadOnlyList<string> Failures);

/// <summary>
/// Populates every configured store from the archive.
///
/// Runs when a session ends, and can be run over the whole archive at any time.
/// It is idempotent by construction — every write is keyed on the natural key
/// and replaces — because re-indexing is the intended answer to a schema change
/// or a bad parse, not an exceptional operation.
///
/// Nothing here touches the live path. It reads files that are already written.
/// </summary>
public sealed class StorageIndexer(
    PostgresStore postgres,
    InfluxStore influx,
    MongoStore mongo,
    string archiveRoot,
    ILogger<StorageIndexer> logger)
{
    public IReadOnlyList<IStorageIndex> Stores => [postgres, influx, mongo];

    public IReadOnlyList<string> AvailableStores =>
        [.. Stores.Where(s => s.Available).Select(s => s.Name)];

    public async Task InitialiseAsync(CancellationToken ct)
    {
        foreach (var store in Stores)
        {
            await store.InitialiseAsync(ct).ConfigureAwait(false);
        }

        var available = AvailableStores;
        logger.LogInformation(
            available.Count == 0
                ? "No storage indexes configured; the archive on disk is the only store."
                : $"Storage indexes active: {string.Join(", ", available)}");
    }

    /// <summary>Indexes one session, including its telemetry if present.</summary>
    public async Task<bool> IndexSessionAsync(
        SessionKey key, SessionAnalysis analysis, string sessionDirectory, CancellationToken ct)
    {
        if (AvailableStores.Count == 0) return false;

        await postgres.IndexAsync(key, analysis, ct).ConfigureAwait(false);
        await mongo.IndexAsync(key, analysis, ct).ConfigureAwait(false);

        if (influx.Available)
        {
            await IndexTelemetryAsync(key, sessionDirectory, analysis, ct).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<int> IndexTelemetryAsync(
        SessionKey key, string sessionDirectory, SessionAnalysis analysis, CancellationToken ct)
    {
        var directory = Path.Combine(sessionDirectory, "analysis", "telemetry");
        if (!Directory.Exists(directory)) return 0;

        var indexed = 0;

        // Telemetry files are named by racing number, but a racing number is
        // reassigned between seasons and reused. Tagging the series with it
        // would make a query for one driver's history return several people's.
        // The analysis carries the mapping, so it is resolved here.
        var tlaByNumber = analysis.Drivers
            .ToDictionary(d => d.RacingNumber, d => d.Tla, StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            var number = Path.GetFileNameWithoutExtension(file);

            // A car with telemetry but no entry in the analysis has nothing to
            // resolve to. Indexing it under its number would reintroduce the
            // very ambiguity this mapping exists to remove.
            if (!tlaByNumber.TryGetValue(number, out var tla))
            {
                logger.LogDebug("No driver entry for car {Number} in {Key}; telemetry skipped",
                    number, key);
                continue;
            }

            // TelemetryRecorder owns this format — the on-disk keys are short
            // ("t", not "offsetMs") to keep a ten-megabyte file from being
            // twenty. Deserialising it here with generic options produced a
            // TelemetryLap whose OffsetMs was null, and a NullReferenceException
            // on the first write.
            var laps = TelemetryRecorder.Read(directory, number).ToList();
            if (laps.Count == 0) continue;

            await influx.IndexTelemetryAsync(key, tla, laps, ct).ConfigureAwait(false);
            indexed++;
        }

        return indexed;
    }

    /// <summary>
    /// Walks the whole archive and indexes everything with a saved analysis.
    ///
    /// Sessions without a saved analysis are skipped rather than replayed:
    /// producing one is a separate job, and doing it here would turn a re-index
    /// into hours of work nobody asked for.
    /// </summary>
    public async Task<IndexReport> BackfillAsync(int? year, CancellationToken ct)
    {
        var indexed = 0;
        var skipped = 0;
        var telemetry = 0;
        var failures = new List<string>();

        if (AvailableStores.Count == 0)
        {
            return new IndexReport(0, 0, 0, [], ["No storage index is configured."]);
        }

        foreach (var seasonDirectory in Directory.EnumerateDirectories(archiveRoot))
        {
            var seasonName = Path.GetFileName(seasonDirectory);
            if (!int.TryParse(seasonName, out var seasonYear)) continue;
            if (year is { } wanted && seasonYear != wanted) continue;

            foreach (var meetingDirectory in Directory.EnumerateDirectories(seasonDirectory))
            {
                foreach (var sessionDirectory in Directory.EnumerateDirectories(meetingDirectory))
                {
                    ct.ThrowIfCancellationRequested();

                    // AnalysisStore owns the on-disk layout — three documents,
                    // not one. Reading them here by hand would be a second
                    // place that has to be updated when it changes.
                    var store = new AnalysisStore(sessionDirectory);
                    if (!store.Exists)
                    {
                        skipped++;
                        continue;
                    }

                    var key = new SessionKey(
                        seasonYear,
                        Path.GetFileName(meetingDirectory),
                        Path.GetFileName(sessionDirectory));

                    try
                    {
                        var analysis = await store.LoadAsync(ct).ConfigureAwait(false);

                        if (analysis is null)
                        {
                            failures.Add($"{key}: the analysis documents did not parse");
                            continue;
                        }

                        await postgres.IndexAsync(key, analysis, ct).ConfigureAwait(false);
                        await mongo.IndexAsync(key, analysis, ct).ConfigureAwait(false);

                        if (influx.Available)
                        {
                            telemetry += await IndexTelemetryAsync(
                                key, sessionDirectory, analysis, ct).ConfigureAwait(false);
                        }

                        indexed++;
                    }
                    catch (Exception e) when (e is IOException or JsonException)
                    {
                        failures.Add($"{key}: {e.Message}");
                    }
                }
            }
        }

        logger.LogInformation(
            "Backfill complete: {Indexed} sessions, {Telemetry} telemetry files, {Skipped} skipped",
            indexed, telemetry, skipped);

        return new IndexReport(indexed, skipped, telemetry, AvailableStores, failures);
    }

}
