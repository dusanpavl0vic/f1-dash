using F1Dash.Core.Analysis;

namespace F1Dash.Server.Storage;

/// <summary>
/// A derived index over the archive.
///
/// Every implementation must be:
///
/// - <b>Optional.</b> Unconfigured, it reports itself unavailable and the
///   application falls back to the files. A self-hosted install on a small box
///   must not be forced to run three databases.
/// - <b>Idempotent.</b> Indexing the same session twice repairs rather than
///   duplicates, because the indexer is expected to be re-run over the whole
///   archive after any schema change.
/// - <b>Off the live path.</b> Nothing here may be called while merging deltas.
/// </summary>
public interface IStorageIndex
{
    string Name { get; }

    /// <summary>False when the store is not configured or not reachable.</summary>
    bool Available { get; }

    /// <summary>Creates schema, buckets or collections. Safe to call repeatedly.</summary>
    Task InitialiseAsync(CancellationToken ct);

    /// <summary>Writes one session's data. Must be an upsert on the natural key.</summary>
    Task IndexAsync(SessionKey key, SessionAnalysis analysis, CancellationToken ct);
}

/// <summary>An index that also stores per-lap telemetry samples.</summary>
public interface ITelemetryIndex : IStorageIndex
{
    Task IndexTelemetryAsync(
        SessionKey key, string tla, IReadOnlyList<TelemetryLap> laps, CancellationToken ct);
}
