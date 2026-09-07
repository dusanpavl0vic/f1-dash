namespace F1Dash.Core.Analysis;

/// <summary>One completed lap by one driver.</summary>
/// <param name="Lap">Lap number, as the feed counts them.</param>
/// <param name="TimeSeconds">Lap time in seconds, or null when the feed never published one.</param>
/// <param name="Position">Running position at the moment the lap completed.</param>
/// <param name="InPit">The driver pitted on this lap — pace on it says nothing about the car.</param>
public sealed record LapRecord(
    int Lap,
    double? TimeSeconds,
    double? Sector1,
    double? Sector2,
    double? Sector3,
    int? Position,
    string? Compound,
    int? TyreAge,
    bool InPit,
    bool PitOut,
    /// <summary>Track status while the lap ran: "1" clear, "4" SC, "6" VSC, "5" red.</summary>
    string? TrackStatus);

/// <summary>A run on one set of tyres.</summary>
public sealed record StintRecord(
    int Index,
    string Compound,
    int StartLap,
    int EndLap,
    int Laps,
    /// <summary>The feed sends this as the string "true"/"false", not a boolean.</summary>
    bool NewTyres);

public sealed record DriverAnalysis(
    string RacingNumber,
    string Tla,
    string TeamName,
    string TeamColour,
    IReadOnlyList<LapRecord> Laps,
    IReadOnlyList<StintRecord> Stints)
{
    /// <summary>Fastest lap that was not a pit or out lap.</summary>
    public double? BestLapSeconds => Laps
        .Where(l => l is { TimeSeconds: > 0, InPit: false, PitOut: false })
        .Min(l => l.TimeSeconds);
}

public sealed record AnalysisMeta(
    int? Year,
    string Meeting,
    string SessionName,
    string SessionType,
    string Circuit,
    int? CircuitKey,
    string? StartDate,
    int TotalLaps,
    /// <summary>Whether per-lap telemetry was captured; see docs/20 Phase H.</summary>
    bool HasTelemetry,
    string RecordedAtUtc);

public sealed record SessionAnalysis(
    AnalysisMeta Meta,
    IReadOnlyList<DriverAnalysis> Drivers);

/// <summary>
/// One lap of telemetry, as parallel arrays.
///
/// Parallel arrays rather than an array of objects: 3–5× smaller over the wire
/// and directly consumable by a charting library without a transform step
/// (docs/09).
/// </summary>
/// <param name="OffsetMs">Milliseconds since the lap started, one per sample.</param>
/// <param name="X">Track X, native F1 units — same system as the circuit outline.</param>
/// <param name="Y">Track Y, native F1 units.</param>
public sealed record TelemetryLap(
    int Lap,
    IReadOnlyList<int> OffsetMs,
    IReadOnlyList<int> Speed,
    IReadOnlyList<int> Throttle,
    IReadOnlyList<int> Brake,
    IReadOnlyList<int> Gear,
    IReadOnlyList<int> Rpm,
    IReadOnlyList<int> X,
    IReadOnlyList<int> Y);

/// <summary>Per-sector comparison of two drivers over their best laps.</summary>
public sealed record SectorComparison(
    string DriverA,
    string DriverB,
    double? BestA,
    double? BestB,
    IReadOnlyList<SectorDelta> Sectors);

public sealed record SectorDelta(
    int Sector,
    double? BestA,
    double? BestB,
    /// <summary>Positive means A is slower. Null when either driver never set the sector.</summary>
    double? Delta,
    string? Faster);
