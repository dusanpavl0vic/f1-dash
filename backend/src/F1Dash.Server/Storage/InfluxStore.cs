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
    /// other's samples. Deriving the epoch from the key guarantees separation
    /// and survives re-indexing.
    /// </summary>
    internal static long EpochForSession(SessionKey key)
    {
        var hash = Math.Abs(key.ToString().GetHashCode(StringComparison.Ordinal)) % 86_400_000;
        return new DateTimeOffset(key.Year, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() + hash;
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
