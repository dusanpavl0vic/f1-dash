using System.Text.Json.Nodes;

namespace F1Dash.Server.Catalog;

public sealed record DriverRound(
    int Round,
    string RaceName,
    string Circuit,
    string Date,
    int? Grid,
    int? Finish,
    string Status,
    /// <summary>Race points only. The round's total is this plus SprintPoints.</summary>
    double Points,
    int? SprintPosition,
    double SprintPoints,
    /// <summary>Running championship total after this round.</summary>
    double CumulativePoints,
    int? QualifyingPosition,
    string? QualifyingTime);

public sealed record DriverSeason(
    int Season,
    string DriverId,
    string Code,
    string GivenName,
    string FamilyName,
    string Nationality,
    string Constructor,
    double Points,
    int Wins,
    int Podiums,
    int? BestFinish,
    int Retirements,
    IReadOnlyList<DriverRound> Rounds);

/// <summary>
/// One driver's season: every round, points progression, and qualifying against
/// race result.
///
/// Two requests rather than one per round. Jolpica addresses a driver's whole
/// season directly, so twenty-four round lookups collapse into two — which
/// matters because this is a page anyone can open for any driver in any year.
/// </summary>
public sealed class DriverSeasonService(HttpClient http, ILogger<DriverSeasonService> logger)
{
    public const string DefaultBaseUrl = StandingsService.DefaultBaseUrl;

    private readonly Dictionary<(int, string), DriverSeason> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public async Task<DriverSeason?> GetAsync(int year, string driverId, CancellationToken ct)
    {
        var key = (year, driverId);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A past season never changes. The current one does, so it is not
            // cached here — the HTTP layer already gives it a short max-age.
            if (year < DateTime.UtcNow.Year && _cache.TryGetValue(key, out var cached)) return cached;

            var races = await RacesAsync($"{BaseUrl}/{year}/drivers/{driverId}/results.json?limit=100", ct)
                .ConfigureAwait(false);

            if (races.Count == 0) return null;

            var qualifying = await RacesAsync(
                $"{BaseUrl}/{year}/drivers/{driverId}/qualifying.json?limit=100", ct).ConfigureAwait(false);

            // Sprints are a SEPARATE resource. /results.json returns Sunday only,
            // so a season summed from it alone understates the championship by
            // exactly the sprint points — 241 against the official 267 for the
            // 2026 leader, which is how this was caught.
            var sprints = await RacesAsync(
                $"{BaseUrl}/{year}/drivers/{driverId}/sprint.json?limit=100", ct).ConfigureAwait(false);

            var sprintByRound = new Dictionary<int, JsonObject>();
            foreach (var race in sprints)
            {
                if (Int(race["round"]) is { } round && race["SprintResults"]?[0] is JsonObject sprint)
                {
                    sprintByRound[round] = sprint;
                }
            }

            // Keyed by round, NOT zipped by index. A driver can have thirteen
            // race results and twelve qualifying entries in the same season —
            // a sprint-only weekend, a session they sat out, a result expunged.
            // Pairing them positionally silently attributes every later
            // qualifying result to the wrong race.
            var qualiByRound = new Dictionary<int, JsonObject>();
            foreach (var race in qualifying)
            {
                if (Int(race["round"]) is { } round && race["QualifyingResults"]?[0] is JsonObject q)
                {
                    qualiByRound[round] = q;
                }
            }

            var rounds = new List<DriverRound>();
            double running = 0;

            foreach (var race in races.OrderBy(r => Int(r["round"]) ?? 0))
            {
                if (race["Results"]?[0] is not JsonObject result) continue;

                var round = Int(race["round"]) ?? 0;
                var points = Double(result["points"]) ?? 0;

                sprintByRound.TryGetValue(round, out var sprint);
                var sprintPoints = sprint is null ? 0 : Double(sprint["points"]) ?? 0;

                running += points + sprintPoints;

                qualiByRound.TryGetValue(round, out var quali);

                rounds.Add(new DriverRound(
                    Round: round,
                    RaceName: (string?)race["raceName"] ?? "",
                    Circuit: (string?)race["Circuit"]?["circuitName"] ?? "",
                    Date: (string?)race["date"] ?? "",
                    Grid: Int(result["grid"]),
                    Finish: Int(result["position"]),
                    Status: (string?)result["status"] ?? "",
                    Points: points,
                    SprintPosition: sprint is null ? null : Int(sprint["position"]),
                    SprintPoints: sprintPoints,
                    CumulativePoints: running,
                    QualifyingPosition: quali is null ? null : Int(quali["position"]),
                    QualifyingTime: quali is null ? null : BestQualiTime(quali)));
            }

            var driver = races[0]["Results"]?[0]?["Driver"];
            var constructor = races[^1]["Results"]?[0]?["Constructor"];

            var season = new DriverSeason(
                Season: year,
                DriverId: driverId,
                Code: (string?)driver?["code"] ?? driverId.ToUpperInvariant()[..Math.Min(3, driverId.Length)],
                GivenName: (string?)driver?["givenName"] ?? "",
                FamilyName: (string?)driver?["familyName"] ?? "",
                Nationality: (string?)driver?["nationality"] ?? "",
                // The LAST constructor of the season, not the first: a driver
                // who changed teams mid-year is described by where they ended.
                Constructor: (string?)constructor?["name"] ?? "",
                Points: running,
                // Sprint wins are not Grand Prix wins and are not counted here.
                Wins: rounds.Count(r => r.Finish == 1),
                Podiums: rounds.Count(r => r.Finish is >= 1 and <= 3),
                BestFinish: rounds.Where(r => r.Finish is > 0).Select(r => r.Finish!.Value).DefaultIfEmpty().Min() is var best && best > 0 ? best : null,
                // "Finished" and "+1 Lap" are finishes; everything else is not.
                Retirements: rounds.Count(r => !r.Status.StartsWith("Finished", StringComparison.Ordinal)
                                            && !r.Status.Contains("Lap", StringComparison.Ordinal)),
                Rounds: rounds);

            if (year < DateTime.UtcNow.Year) _cache[key] = season;
            return season;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "Could not fetch {Year} season for {Driver}", year, driverId);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Q3 if they reached it, else Q2, else Q1 — their best of the day.</summary>
    private static string? BestQualiTime(JsonObject quali)
    {
        foreach (var key in (string[])["Q3", "Q2", "Q1"])
        {
            var value = (string?)quali[key];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private async Task<List<JsonObject>> RacesAsync(string url, CancellationToken ct)
    {
        var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var races = JsonNode.Parse(text)?["MRData"]?["RaceTable"]?["Races"]?.AsArray();

        return races is null ? [] : [.. races.OfType<JsonObject>()];
    }

    private static int? Int(JsonNode? node) => int.TryParse((string?)node, out var v) ? v : null;

    private static double? Double(JsonNode? node) =>
        double.TryParse((string?)node, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
}
