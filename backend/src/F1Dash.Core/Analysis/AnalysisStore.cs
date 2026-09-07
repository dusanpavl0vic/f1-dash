using System.Text.Json;
using System.Text.Json.Serialization;

namespace F1Dash.Core.Analysis;

/// <summary>
/// Reads and writes a session's analysis on disk, beside the stream it came
/// from.
///
///   analysis/meta.json
///   analysis/laps.json
///   analysis/stints.json
///   analysis/telemetry/{racingNumber}.jsonl
///
/// Telemetry is per driver in its own file and loaded on demand: one document
/// would be a 40 MB response for a chart that needs a single lap.
/// </summary>
public sealed class AnalysisStore(string sessionDirectory)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public string Directory => Path.Combine(sessionDirectory, "analysis");
    public string TelemetryDirectory => Path.Combine(Directory, "telemetry");

    private string MetaPath => Path.Combine(Directory, "meta.json");
    private string LapsPath => Path.Combine(Directory, "laps.json");
    private string StintsPath => Path.Combine(Directory, "stints.json");

    public bool Exists => File.Exists(LapsPath);

    public async Task SaveAsync(SessionAnalysis analysis, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);

        await WriteAsync(MetaPath, analysis.Meta, ct).ConfigureAwait(false);

        // Laps and stints are split because the strategy view needs stints
        // alone, and they are two orders of magnitude smaller.
        await WriteAsync(LapsPath, analysis.Drivers.Select(d => new
        {
            d.RacingNumber, d.Tla, d.TeamName, d.TeamColour, d.Laps,
        }), ct).ConfigureAwait(false);

        await WriteAsync(StintsPath, analysis.Drivers.Select(d => new
        {
            d.RacingNumber, d.Tla, d.TeamName, d.TeamColour, d.Stints,
        }), ct).ConfigureAwait(false);
    }

    public async Task<SessionAnalysis?> LoadAsync(CancellationToken ct = default)
    {
        if (!Exists) return null;

        var meta = await ReadAsync<AnalysisMeta>(MetaPath, ct).ConfigureAwait(false);
        var laps = await ReadAsync<List<DriverLapsDocument>>(LapsPath, ct).ConfigureAwait(false);
        var stints = await ReadAsync<List<DriverStintsDocument>>(StintsPath, ct).ConfigureAwait(false);

        if (meta is null || laps is null) return null;

        var stintsByNumber = (stints ?? []).ToDictionary(s => s.RacingNumber, s => s.Stints);

        var drivers = laps.Select(d => new DriverAnalysis(
            d.RacingNumber, d.Tla, d.TeamName, d.TeamColour, d.Laps,
            stintsByNumber.TryGetValue(d.RacingNumber, out var s) ? s : [])).ToList();

        return new SessionAnalysis(meta, drivers);
    }

    /// <summary>
    /// Per-sector comparison over each driver's best sector times.
    ///
    /// Best sector, not the sectors of the best lap: a driver's fastest S1 and
    /// fastest S3 often come from different laps, and the question being asked
    /// is where each is quicker, not what one lap looked like.
    /// </summary>
    public static SectorComparison Compare(DriverAnalysis a, DriverAnalysis b)
    {
        var sectors = new List<SectorDelta>();

        for (var index = 1; index <= 3; index++)
        {
            var bestA = BestSector(a, index);
            var bestB = BestSector(b, index);

            double? delta = bestA is { } x && bestB is { } y ? Math.Round(x - y, 3) : null;

            sectors.Add(new SectorDelta(
                index, bestA, bestB, delta,
                delta is null ? null : delta < 0 ? a.Tla : delta > 0 ? b.Tla : null));
        }

        return new SectorComparison(a.Tla, b.Tla, a.BestLapSeconds, b.BestLapSeconds, sectors);
    }

    private static double? BestSector(DriverAnalysis driver, int sector)
    {
        var values = driver.Laps
            .Where(l => !l.InPit && !l.PitOut)
            .Select(l => sector switch { 1 => l.Sector1, 2 => l.Sector2, _ => l.Sector3 })
            .Where(v => v is > 0)
            .ToList();

        return values.Count == 0 ? null : values.Min();
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        // Written to a temporary file and moved into place, so an interrupted
        // save never leaves a half-written document that later reads as valid.
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, value, Json, ct).ConfigureAwait(false);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed record DriverLapsDocument(
        string RacingNumber, string Tla, string TeamName, string TeamColour, List<LapRecord> Laps);

    private sealed record DriverStintsDocument(
        string RacingNumber, string Tla, string TeamName, string TeamColour, List<StintRecord> Stints);
}
