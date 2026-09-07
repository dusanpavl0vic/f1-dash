using System.Text.Json.Nodes;

namespace F1Dash.Server.Catalog;

public sealed record PodiumEntry(int Position, string Code, string Driver, string Constructor);

public sealed record ScheduledRound(
    int Round,
    string Name,
    string Circuit,
    string Country,
    string Locality,
    /// <summary>Race start, ISO 8601 UTC.</summary>
    string? StartUtc,
    string Status,
    /// <summary>Top three, for rounds that have run. Empty otherwise.</summary>
    IReadOnlyList<PodiumEntry> Podium);

public sealed record NextSession(ScheduledRound? Round, double? HoursUntil);

/// <summary>
/// The season calendar, from Jolpica (the Ergast successor).
///
/// The F1 archive index only lists sessions that have already run, so it cannot
/// answer "what is next". Jolpica publishes the full calendar including future
/// rounds — 23 for 2026 — with dates and start times.
///
/// Cached for a day: a calendar changes a handful of times a season, and
/// Jolpica rate-limits to roughly 4 requests a second and 500 an hour.
/// </summary>
public sealed class ScheduleService(HttpClient http, ILogger<ScheduleService> logger)
{
    public const string DefaultBaseUrl = "https://api.jolpi.ca/ergast/f1";

    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    private readonly Dictionary<int, (DateTime Fetched, IReadOnlyList<ScheduledRound> Rounds)> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public async Task<IReadOnlyList<ScheduledRound>> SeasonAsync(int year, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(year, out var cached) && DateTime.UtcNow - cached.Fetched < CacheFor)
            {
                return Restatus(cached.Rounds);
            }

            var url = $"{BaseUrl}/{year}.json?limit=100";
            var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);

            var races = JsonNode.Parse(text)?["MRData"]?["RaceTable"]?["Races"] as JsonArray;
            var rounds = new List<ScheduledRound>();

            foreach (var race in (races ?? []).OfType<JsonObject>())
            {
                var circuit = race["Circuit"] as JsonObject;
                var location = circuit?["Location"] as JsonObject;

                // Jolpica splits date and time; a round with no time yet is
                // still a real scheduled round and must not be dropped.
                var date = (string?)race["date"];
                var time = (string?)race["time"];
                var start = date is null ? null : $"{date}T{time ?? "00:00:00Z"}".Replace("ZZ", "Z");

                rounds.Add(new ScheduledRound(
                    Round: int.TryParse((string?)race["round"], out var r) ? r : 0,
                    Name: (string?)race["raceName"] ?? "",
                    Circuit: (string?)circuit?["circuitName"] ?? "",
                    Country: (string?)location?["country"] ?? "",
                    Locality: (string?)location?["locality"] ?? "",
                    StartUtc: start,
                    Status: "",
                    Podium: []));
            }

            var podiums = await PodiumsAsync(year, ct).ConfigureAwait(false);
            var withPodiums = rounds
                .Select(r => podiums.TryGetValue(r.Round, out var p) ? r with { Podium = p } : r)
                .ToList();

            _cache[year] = (DateTime.UtcNow, withPodiums);
            return Restatus(withPodiums);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "Could not fetch the {Year} calendar", year);
            return _cache.TryGetValue(year, out var stale) ? Restatus(stale.Rounds) : [];
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Podiums for every round that has run.
    ///
    /// Three requests for the whole season rather than one per round: Ergast
    /// exposes results filtered by finishing position, so /results/1, /2 and /3
    /// give every winner, runner-up and third place in three calls. Jolpica
    /// rate-limits to roughly 4 requests a second, so 23 calls for a calendar
    /// page would be rude and slow.
    /// </summary>
    private async Task<Dictionary<int, List<PodiumEntry>>> PodiumsAsync(int year, CancellationToken ct)
    {
        var byRound = new Dictionary<int, List<PodiumEntry>>();

        for (var position = 1; position <= 3; position++)
        {
            try
            {
                var text = await http
                    .GetStringAsync($"{BaseUrl}/{year}/results/{position}.json?limit=40", ct)
                    .ConfigureAwait(false);

                var races = JsonNode.Parse(text)?["MRData"]?["RaceTable"]?["Races"] as JsonArray;

                foreach (var race in (races ?? []).OfType<JsonObject>())
                {
                    if (!int.TryParse((string?)race["round"], out var round)) continue;
                    if (race["Results"] is not JsonArray results || results.FirstOrDefault() is not JsonObject result) continue;

                    var driver = result["Driver"] as JsonObject;

                    if (!byRound.TryGetValue(round, out var list))
                    {
                        list = [];
                        byRound[round] = list;
                    }

                    list.Add(new PodiumEntry(
                        position,
                        (string?)driver?["code"] ?? "",
                        $"{(string?)driver?["givenName"]} {(string?)driver?["familyName"]}".Trim(),
                        (string?)result["Constructor"]?["name"] ?? ""));
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // A missing podium is not worth failing the calendar over.
                logger.LogWarning(e, "Could not fetch {Year} results for position {Position}", year, position);
            }
        }

        foreach (var list in byRound.Values) list.Sort((a, b) => a.Position.CompareTo(b.Position));
        return byRound;
    }

    /// <summary>The next round that has not finished, and how far away it is.</summary>
    public async Task<NextSession> NextAsync(int year, CancellationToken ct)
    {
        var rounds = await SeasonAsync(year, ct).ConfigureAwait(false);

        var upcoming = rounds
            .Where(r => r.Status is "upcoming" or "live")
            .OrderBy(r => r.StartUtc)
            .FirstOrDefault();

        if (upcoming?.StartUtc is null) return new NextSession(upcoming, null);

        return DateTime.TryParse(upcoming.StartUtc, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var start)
            ? new NextSession(upcoming, Math.Round((start - DateTime.UtcNow).TotalHours, 1))
            : new NextSession(upcoming, null);
    }

    /// <summary>
    /// Status is derived at read time, not stored: a cached calendar would
    /// otherwise report a race as upcoming for up to a day after it ran.
    /// </summary>
    private static IReadOnlyList<ScheduledRound> Restatus(IReadOnlyList<ScheduledRound> rounds)
    {
        var now = DateTime.UtcNow;

        return [.. rounds.Select(r =>
        {
            if (r.StartUtc is null
                || !DateTime.TryParse(r.StartUtc, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var start))
            {
                return r with { Status = "upcoming" };
            }

            // A grand prix runs about two hours; the window is padded so a race
            // in progress reads as live rather than already finished.
            return r with
            {
                Status = now < start ? "upcoming"
                    : now < start.AddHours(3) ? "live"
                    : "finished",
            };
        })];
    }
}
