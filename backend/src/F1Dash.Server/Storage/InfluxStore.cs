using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using F1Dash.Core.Analysis;

namespace F1Dash.Server.Storage;

/// <summary>
/// Per-lap telemetry channels, as time series.
///
/// Written over Influx's HTTP line protocol with a plain HttpClient rather than
/// the official client package. That is not stubbornness: the write path is one
/// text format and one POST, the read path is one Flux query and one POST, and
/// the package would pull a dependency tree into a backend that currently has
/// none — for two endpoints.
///
/// Telemetry stays in `telemetry/*.jsonl` regardless. This exists for the
/// questions a file layout cannot answer: anything spanning sessions.
/// </summary>
public sealed class InfluxStore : ITelemetryIndex
{
    /// <summary>
    /// Samples per write request.
    ///
    /// A race is roughly 400,000 points across all drivers. Sent as one body
    /// that is a ~30 MB POST that times out; sent one at a time it is 400,000
    /// round trips. Five thousand lines is about 300 KB, which is comfortable.
    /// </summary>
    private const int BatchSize = 5_000;

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string? _url;
    private readonly string? _org;
    private readonly string? _bucket;

    public InfluxStore(HttpClient http, ILogger<InfluxStore> logger)
    {
        _http = http;
        _logger = logger;

        _url = Environment.GetEnvironmentVariable("INFLUX_URL")?.TrimEnd('/');
        _org = Environment.GetEnvironmentVariable("INFLUX_ORG") ?? "apex";
        _bucket = Environment.GetEnvironmentVariable("INFLUX_BUCKET") ?? "telemetry";

        var token = Environment.GetEnvironmentVariable("INFLUX_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", token);
        }

        Available = !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(token);
    }

    public string Name => "influxdb";
    public bool Available { get; private set; }

    public async Task InitialiseAsync(CancellationToken ct)
    {
        if (!Available) return;

        try
        {
            using var response = await _http.GetAsync($"{_url}/ping", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("InfluxDB ping returned {Status}; telemetry indexing disabled.",
                    response.StatusCode);
                Available = false;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(e, "InfluxDB unreachable; telemetry indexing disabled.");
            Available = false;
        }
    }

    /// <summary>Lap metadata goes to Postgres, not here. Influx has no joins.</summary>
    public Task IndexAsync(SessionKey key, SessionAnalysis analysis, CancellationToken ct) =>
        Task.CompletedTask;

    public async Task IndexTelemetryAsync(
        SessionKey key, string tla, IReadOnlyList<TelemetryLap> laps, CancellationToken ct)
    {
        if (!Available || laps.Count == 0) return;

        var lines = new StringBuilder();
        var pending = 0;

        foreach (var lap in laps)
        {
            // The session's own start is unknown here, so offsets are written
            // relative to a fixed epoch per session. The absolute wall time of a
            // 2018 sample is meaningless; its position within the session is not.
            var lapBase = EpochForSession(key);

            for (var i = 0; i < lap.OffsetMs.Count; i++)
            {
                var timestamp = (lapBase + lap.OffsetMs[i]) * 1_000_000L; // ns

                Append(lines, "speed", key, tla, lap.Lap, At(lap.Speed, i), timestamp);
                Append(lines, "throttle", key, tla, lap.Lap, At(lap.Throttle, i), timestamp);
                Append(lines, "brake", key, tla, lap.Lap, At(lap.Brake, i), timestamp);
                Append(lines, "rpm", key, tla, lap.Lap, At(lap.Rpm, i), timestamp);
                Append(lines, "gear", key, tla, lap.Lap, At(lap.Gear, i), timestamp);

                pending += 5;
                if (pending < BatchSize) continue;

                await FlushAsync(lines, ct).ConfigureAwait(false);
                pending = 0;
            }
        }

        await FlushAsync(lines, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A stable, arbitrary epoch per session.
    ///
    /// Real session start times are not always in the archive, and two sessions
    /// must not overlap in the time axis or a query for one would return the
    /// other's samples. Deriving the epoch from the key guarantees separation.
    ///
    /// The hash is FNV-1a, computed here, and NOT <c>string.GetHashCode()</c>.
    /// .NET randomises string hashing per process, so the same session indexed
    /// after a restart landed at a different point on the time axis — every
    /// re-index appended a duplicate copy instead of overwriting. It showed up
    /// as a lap with three times its real sample count spread over eight hours.
    /// </summary>
    internal static long EpochForSession(SessionKey key)
    {
        var hash = Fnv1a(key.ToString()) % 86_400_000;
        return new DateTimeOffset(key.Year, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds()
            + (long)hash;
    }

    /// <summary>FNV-1a, 64-bit. Deterministic across processes and runtimes.</summary>
    private static ulong Fnv1a(string value)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static int? At(IReadOnlyList<int> values, int i) =>
        i < values.Count ? values[i] : null;

    private static void Append(
        StringBuilder lines, string measurement, SessionKey key,
        string tla, int lap, int? value, long timestampNs)
    {
        if (value is not { } v) return;

        lines.Append(measurement)
            .Append(",session=").Append(EscapeTag(key.ToString()))
            .Append(",driver=").Append(EscapeTag(tla))
            .Append(",lap=").Append(lap)
            .Append(" value=").Append(v.ToString(CultureInfo.InvariantCulture)).Append('i')
            .Append(' ').Append(timestampNs)
            .Append('\n');
    }

    /// <summary>Line protocol tag values escape commas, spaces and equals signs.</summary>
    internal static string EscapeTag(string value) => value
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace(" ", "\\ ", StringComparison.Ordinal)
        .Replace("=", "\\=", StringComparison.Ordinal);

    private async Task FlushAsync(StringBuilder lines, CancellationToken ct)
    {
        if (lines.Length == 0) return;

        var body = lines.ToString();
        lines.Clear();

        using var content = new StringContent(body, Encoding.UTF8, "text/plain");
        var url = $"{_url}/api/v2/write?org={Uri.EscapeDataString(_org!)}"
            + $"&bucket={Uri.EscapeDataString(_bucket!)}&precision=ns";

        try
        {
            using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Influx write failed {Status}: {Detail}",
                    response.StatusCode, detail[..Math.Min(200, detail.Length)]);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Indexing is best-effort by design. The archive still holds
            // everything, so a failed write costs a re-index, not data.
            _logger.LogWarning(e, "Influx write failed");
        }
    }

    /// <summary>
    /// Reads one lap of telemetry back out of the index.
    ///
    /// The write side splits a lap into one point per channel per sample; this
    /// reassembles them into the parallel arrays the charts consume. Pivoting
    /// happens in Flux rather than here because the alternative is five
    /// separate queries and a join in C#.
    ///
    /// Returns null when the lap is not indexed, so the caller can fall back to
    /// the file without treating absence as an error.
    /// </summary>
    public async Task<TelemetryLap?> ReadLapAsync(
        SessionKey key, string driver, int lap, CancellationToken ct)
    {
        if (!Available) return null;

        var flux = $$"""
            from(bucket: "{{_bucket}}")
              |> range(start: 2015-01-01T00:00:00Z, stop: 2035-01-01T00:00:00Z)
              |> filter(fn: (r) => r.session == "{{key}}" and r.driver == "{{driver}}"
                                   and r.lap == "{{lap}}")
              |> pivot(rowKey: ["_time"], columnKey: ["_measurement"], valueColumn: "_value")
              |> sort(columns: ["_time"])
              |> keep(columns: ["_time", "speed", "throttle", "brake", "gear", "rpm"])
            """;

        var csv = await QueryAsync(flux, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(csv)) return null;

        return ParseLapCsv(lap, csv);
    }

    /// <summary>
    /// Rebuilds a lap from Influx's annotated CSV.
    ///
    /// Columns are located by header name, never by index: Flux does not
    /// guarantee an order, and a pivot that returns channels in a different
    /// sequence would otherwise put brake values in the speed array.
    /// </summary>
    internal static TelemetryLap? ParseLapCsv(int lap, string csv)
    {
        Dictionary<string, int>? columns = null;
        var time = new List<int>();
        var speed = new List<int>();
        var throttle = new List<int>();
        var brake = new List<int>();
        var gear = new List<int>();
        var rpm = new List<int>();
        long? firstTicks = null;

        foreach (var raw in csv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var cells = line.Split(',');

            if (columns is null || cells.Contains("_time"))
            {
                columns = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < cells.Length; i++) columns[cells[i]] = i;
                continue;
            }

            if (!columns.TryGetValue("_time", out var timeAt) || timeAt >= cells.Length) continue;
            if (!DateTimeOffset.TryParse(cells[timeAt], CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var when)) continue;

            firstTicks ??= when.ToUnixTimeMilliseconds();

            // Offsets are rebuilt relative to the lap's own first sample. The
            // absolute timestamps are a synthetic per-session epoch (see
            // EpochForSession) and mean nothing outside this store.
            time.Add((int)(when.ToUnixTimeMilliseconds() - firstTicks.Value));
            speed.Add(Value(cells, columns, "speed"));
            throttle.Add(Value(cells, columns, "throttle"));
            brake.Add(Value(cells, columns, "brake"));
            gear.Add(Value(cells, columns, "gear"));
            rpm.Add(Value(cells, columns, "rpm"));
        }

        if (time.Count == 0) return null;

        // X and Y are not indexed: they are only used to draw a racing line
        // over a circuit outline, which is a per-session view that already has
        // the file. Returning empty arrays is honest about that.
        return new TelemetryLap(lap, time, speed, throttle, brake, gear, rpm, [], []);
    }

    private static int Value(string[] cells, Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var i) && i < cells.Length
        && int.TryParse(cells[i], CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    /// <summary>
    /// Runs a Flux query and returns the raw CSV.
    ///
    /// Deliberately not parsed into a typed model here: the queries this serves
    /// are exploratory and cross-session, and each endpoint knows the shape it
    /// asked for better than a shared parser would.
    /// </summary>
    public async Task<string?> QueryAsync(string flux, CancellationToken ct)
    {
        if (!Available) return null;

        using var content = new StringContent(flux, Encoding.UTF8, "application/vnd.flux");
        var url = $"{_url}/api/v2/query?org={Uri.EscapeDataString(_org!)}";

        try
        {
            using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(e, "Influx query failed");
            return null;
        }
    }
}
