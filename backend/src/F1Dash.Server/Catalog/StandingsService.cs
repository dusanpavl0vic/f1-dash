using System.Text.Json.Nodes;

namespace F1Dash.Server.Catalog;

public sealed record DriverStanding(
    int Position, string Code, string Driver, string Nationality,
    string Constructor, double Points, int Wins,
    /// <summary>Jolpica's own id ("max_verstappen"), the key for a driver's season.</summary>
    string DriverId);

public sealed record ConstructorStanding(
    int Position, string Constructor, string Nationality, double Points, int Wins);

public sealed record Standings(
    int Season,
    /// <summary>The round the table stands after.</summary>
    int Round,
    IReadOnlyList<DriverStanding> Drivers,
    IReadOnlyList<ConstructorStanding> Constructors);

/// <summary>
/// Championship tables as they stood after any round.
///
/// Ergast — and Jolpica after it — addresses standings by round, so the table
/// after round 5 is a URL rather than something to reconstruct by replaying
/// results. Cached per (season, round) because a completed round's table never
/// changes again.
/// </summary>
public sealed class StandingsService(HttpClient http, ILogger<StandingsService> logger)
{
    public const string DefaultBaseUrl = "https://api.jolpi.ca/ergast/f1";

    private readonly Dictionary<(int Season, int Round), Standings> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>Pass round 0 for the latest available table.</summary>
    public async Task<Standings?> GetAsync(int year, int round, CancellationToken ct)
    {
        var key = (year, round);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Round 0 means "latest", which keeps changing during a season, so
            // it is never cached.
            if (round > 0 && _cache.TryGetValue(key, out var cached)) return cached;

            var suffix = round > 0 ? $"/{round}" : "";

            var drivers = await FetchAsync(
                $"{BaseUrl}/{year}{suffix}/driverStandings.json?limit=40", "DriverStandings", ct)
                .ConfigureAwait(false);

            var constructors = await FetchAsync(
                $"{BaseUrl}/{year}{suffix}/constructorStandings.json?limit=40", "ConstructorStandings", ct)
                .ConfigureAwait(false);

            if (drivers is null && constructors is null) return null;

            var actualRound = ReadInt(drivers?["round"]) ?? ReadInt(constructors?["round"]) ?? round;

            var result = new Standings(
                year,
                actualRound,
                ReadDrivers(drivers),
                ReadConstructors(constructors));

            if (round > 0) _cache[key] = result;
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "Could not fetch {Year} standings for round {Round}", year, round);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<JsonObject?> FetchAsync(string url, string listName, CancellationToken ct)
    {
        var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var lists = JsonNode.Parse(text)?["MRData"]?["StandingsTable"]?["StandingsLists"] as JsonArray;

        // An empty list is the honest answer for a season that has not started.
        _ = listName;
        return lists?.FirstOrDefault() as JsonObject;
    }

    private static List<DriverStanding> ReadDrivers(JsonObject? list)
    {
        var result = new List<DriverStanding>();
        if (list?["DriverStandings"] is not JsonArray entries) return result;

        foreach (var entry in entries.OfType<JsonObject>())
        {
            var driver = entry["Driver"] as JsonObject;
            var constructors = entry["Constructors"] as JsonArray;

            result.Add(new DriverStanding(
                Position: ReadInt(entry["position"]) ?? 0,
                Code: (string?)driver?["code"] ?? "",
                Driver: $"{(string?)driver?["givenName"]} {(string?)driver?["familyName"]}".Trim(),
                Nationality: (string?)driver?["nationality"] ?? "",
                // A driver who changed teams mid-season lists several; the last
                // is the current one.
                Constructor: (string?)(constructors?.LastOrDefault() as JsonObject)?["name"] ?? "",
                Points: ReadDouble(entry["points"]) ?? 0,
                Wins: ReadInt(entry["wins"]) ?? 0,
                DriverId: (string?)driver?["driverId"] ?? ""));
        }

        return result;
    }

    private static List<ConstructorStanding> ReadConstructors(JsonObject? list)
    {
        var result = new List<ConstructorStanding>();
        if (list?["ConstructorStandings"] is not JsonArray entries) return result;

        foreach (var entry in entries.OfType<JsonObject>())
        {
            var constructor = entry["Constructor"] as JsonObject;

            result.Add(new ConstructorStanding(
                Position: ReadInt(entry["position"]) ?? 0,
                Constructor: (string?)constructor?["name"] ?? "",
                Nationality: (string?)constructor?["nationality"] ?? "",
                Points: ReadDouble(entry["points"]) ?? 0,
                Wins: ReadInt(entry["wins"]) ?? 0));
        }

        return result;
    }

    // Ergast sends every number as a string.
    private static int? ReadInt(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && int.TryParse(s, out var i) ? i
        : node is JsonValue n && n.TryGetValue<int>(out var direct) ? direct
        : null;

    private static double? ReadDouble(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s)
        && double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d
        : null;
}
