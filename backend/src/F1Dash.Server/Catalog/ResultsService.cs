using System.Text.Json.Nodes;

namespace F1Dash.Server.Catalog;

public sealed record ResultRow(
    int Position,
    string Number,
    string Code,
    string Driver,
    string Constructor,
    string Nationality,
    /// <summary>Race time, qualifying best, or the reason they are not classified.</summary>
    string Time,
    string Gap,
    double Points,
    int Laps,
    string Status,
    /// <summary>Grid position, where the session has one.</summary>
    int? Grid);

public sealed record SessionResults(string Session, IReadOnlyList<ResultRow> Rows);

public sealed record RaceHighlight(string Code, string Driver, string Constructor, string Value, string? Detail);

public sealed record RaceResults(
    int Season,
    int Round,
    string RaceName,
    string Circuit,
    string Country,
    string Locality,
    string? Date,
    RaceHighlight? Winner,
    RaceHighlight? Pole,
    RaceHighlight? FastestLap,
    IReadOnlyList<SessionResults> Sessions);

/// <summary>
/// Race, qualifying and sprint results for one round.
///
/// Practice results are deliberately absent: Ergast never carried them, and the
/// F1 archive gives us practice timing far more completely through the session
/// analysis. Inventing a thin practice table from a source that does not have
/// one would be worse than pointing at the analysis page.
/// </summary>
public sealed class ResultsService(HttpClient http, ILogger<ResultsService> logger)
{
    public const string DefaultBaseUrl = "https://api.jolpi.ca/ergast/f1";

    private readonly Dictionary<(int, int), RaceResults> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public async Task<RaceResults?> GetAsync(int year, int round, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A completed round's result never changes.
            if (_cache.TryGetValue((year, round), out var cached)) return cached;

            var race = await FetchRaceAsync($"{BaseUrl}/{year}/{round}/results.json?limit=40", ct)
                .ConfigureAwait(false);

            if (race is null) return null;

            var sessions = new List<SessionResults>();

            var raceRows = ReadRows(race["Results"] as JsonArray, "race");
            if (raceRows.Count > 0) sessions.Add(new SessionResults("Race", raceRows));

            // The grid is the race result read by starting position — a separate
            // view of data we already have, not another request.
            var grid = raceRows
                .Where(r => r.Grid is > 0)
                .OrderBy(r => r.Grid)
                .Select(r => r with { Position = r.Grid!.Value, Time = "", Gap = "", Points = 0 })
                .ToList();
            if (grid.Count > 0) sessions.Add(new SessionResults("Starting grid", grid));

            var qualifying = await FetchRaceAsync($"{BaseUrl}/{year}/{round}/qualifying.json?limit=40", ct)
                .ConfigureAwait(false);
            var qualifyingRows = ReadQualifying(qualifying?["QualifyingResults"] as JsonArray);
            if (qualifyingRows.Count > 0) sessions.Add(new SessionResults("Qualifying", qualifyingRows));

            var sprint = await FetchRaceAsync($"{BaseUrl}/{year}/{round}/sprint.json?limit=40", ct)
                .ConfigureAwait(false);
            var sprintRows = ReadRows(sprint?["SprintResults"] as JsonArray, "sprint");
            if (sprintRows.Count > 0) sessions.Add(new SessionResults("Sprint", sprintRows));

            var circuit = race["Circuit"] as JsonObject;
            var location = circuit?["Location"] as JsonObject;

            var result = new RaceResults(
                Season: year,
                Round: round,
                RaceName: (string?)race["raceName"] ?? "",
                Circuit: (string?)circuit?["circuitName"] ?? "",
                Country: (string?)location?["country"] ?? "",
                Locality: (string?)location?["locality"] ?? "",
                Date: (string?)race["date"],
                Winner: Winner(raceRows),
                Pole: Pole(qualifyingRows),
                FastestLap: FastestLap(race["Results"] as JsonArray),
                Sessions: sessions);

            _cache[(year, round)] = result;
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "Could not fetch results for {Year} round {Round}", year, round);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<JsonObject?> FetchRaceAsync(string url, CancellationToken ct)
    {
        var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var races = JsonNode.Parse(text)?["MRData"]?["RaceTable"]?["Races"] as JsonArray;
        return races?.FirstOrDefault() as JsonObject;
    }

    private static List<ResultRow> ReadRows(JsonArray? results, string kind)
    {
        var rows = new List<ResultRow>();

        foreach (var entry in (results ?? []).OfType<JsonObject>())
        {
            var driver = entry["Driver"] as JsonObject;
            var time = entry["Time"] as JsonObject;
            var status = (string?)entry["status"] ?? "";

            // Ergast gives the leader an absolute time and everyone else a gap,
            // both in the same field. A car that did not finish has neither.
            var display = (string?)time?["time"] ?? "";
            var position = ReadInt(entry["position"]) ?? 0;

            rows.Add(new ResultRow(
                Position: position,
                Number: (string?)entry["number"] ?? "",
                Code: (string?)driver?["code"] ?? "",
                Driver: $"{(string?)driver?["givenName"]} {(string?)driver?["familyName"]}".Trim(),
                Constructor: (string?)entry["Constructor"]?["name"] ?? "",
                Nationality: (string?)driver?["nationality"] ?? "",
                Time: position == 1 ? display : "",
                Gap: position == 1 ? "" : display.Length > 0 ? display : status,
                Points: ReadDouble(entry["points"]) ?? 0,
                Laps: ReadInt(entry["laps"]) ?? 0,
                Status: status,
                Grid: kind == "race" ? ReadInt(entry["grid"]) : null));
        }

        return rows;
    }

    private static List<ResultRow> ReadQualifying(JsonArray? results)
    {
        var rows = new List<ResultRow>();

        foreach (var entry in (results ?? []).OfType<JsonObject>())
        {
            var driver = entry["Driver"] as JsonObject;

            // The best of Q1/Q2/Q3 the driver reached is the time that matters.
            var best = (string?)entry["Q3"] ?? (string?)entry["Q2"] ?? (string?)entry["Q1"] ?? "";

            rows.Add(new ResultRow(
                Position: ReadInt(entry["position"]) ?? 0,
                Number: (string?)entry["number"] ?? "",
                Code: (string?)driver?["code"] ?? "",
                Driver: $"{(string?)driver?["givenName"]} {(string?)driver?["familyName"]}".Trim(),
                Constructor: (string?)entry["Constructor"]?["name"] ?? "",
                Nationality: (string?)driver?["nationality"] ?? "",
                Time: best,
                Gap: "",
                Points: 0,
                Laps: 0,
                Status: "",
                Grid: null));
        }

        return rows;
    }

    private static RaceHighlight? Winner(List<ResultRow> rows)
    {
        var first = rows.FirstOrDefault(r => r.Position == 1);
        return first is null ? null
            : new RaceHighlight(first.Code, first.Driver, first.Constructor, first.Time, null);
    }

    private static RaceHighlight? Pole(List<ResultRow> rows)
    {
        var first = rows.FirstOrDefault(r => r.Position == 1);
        return first is null ? null
            : new RaceHighlight(first.Code, first.Driver, first.Constructor, first.Time, null);
    }

    private static RaceHighlight? FastestLap(JsonArray? results)
    {
        foreach (var entry in (results ?? []).OfType<JsonObject>())
        {
            if (entry["FastestLap"] is not JsonObject fastest) continue;
            if ((string?)fastest["rank"] != "1") continue;

            var driver = entry["Driver"] as JsonObject;

            return new RaceHighlight(
                (string?)driver?["code"] ?? "",
                $"{(string?)driver?["givenName"]} {(string?)driver?["familyName"]}".Trim(),
                (string?)entry["Constructor"]?["name"] ?? "",
                (string?)fastest["Time"]?["time"] ?? "",
                $"Lap {(string?)fastest["lap"]}");
        }

        return null;
    }

    private static int? ReadInt(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && int.TryParse(s, out var i) ? i : null;

    private static double? ReadDouble(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s)
        && double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}
