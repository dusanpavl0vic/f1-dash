using System.Globalization;
using Npgsql;

namespace F1Dash.Server.Storage;

public sealed record CircuitRecord(
    int Year, string Meeting, string SessionName, string Code, string TeamName, double LapSeconds, int Lap);

public sealed record DriverSeasonSummary(
    int Year, int Sessions, int Laps, double? BestLapSeconds, double? MedianLapSeconds);

public sealed record StrategyRow(
    int Year, string Meeting, string Code, int Stops, string Compounds);

public sealed record SpeedRow(string Driver, string Session, int Lap, int TopSpeed);

/// <summary>
/// The cross-session questions. These are the entire reason the indexes exist.
///
/// Every one of them is a full scan of the archive without a database — a
/// season is roughly 120 sessions, and answering "who was quickest here since
/// 2018" would mean opening every file in it. With an index each is one query.
///
/// Each method returns an empty list rather than throwing when its store is
/// unavailable. A page that shows nothing is a correct answer for a deployment
/// that runs no databases; an error page is not.
/// </summary>
public sealed class InsightsService(
    PostgresStore postgres, InfluxStore influx, ILogger<InsightsService> logger)
{
    public bool RelationalAvailable => postgres.Available;
    public bool TelemetryAvailable => influx.Available;

    /// <summary>Fastest laps ever recorded at one circuit, across every indexed season.</summary>
    public Task<IReadOnlyList<CircuitRecord>> CircuitRecordsAsync(
        string meetingSlug, int limit, CancellationToken ct) =>
        QueryAsync("""
            SELECT s.year, s.meeting_name, s.session_name, d.code,
                   COALESCE(e.team_name, ''), l.lap_time_ms, l.lap
            FROM laps l
            JOIN sessions s ON s.id = l.session_id
            JOIN drivers  d ON d.id = l.driver_id
            LEFT JOIN session_entries e
                   ON e.session_id = l.session_id AND e.driver_id = l.driver_id
            WHERE s.meeting_slug = @slug
              AND l.lap_time_ms IS NOT NULL
              -- In and out laps say nothing about pace.
              AND NOT l.is_pit_in AND NOT l.is_pit_out
            ORDER BY l.lap_time_ms
            LIMIT @limit
            """,
            reader => new CircuitRecord(
                reader.GetInt16(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                reader.GetInt32(5) / 1000.0, reader.GetInt16(6)),
            ct, ("slug", meetingSlug), ("limit", limit));

    /// <summary>One driver's pace, season by season.</summary>
    public Task<IReadOnlyList<DriverSeasonSummary>> DriverHistoryAsync(
        string code, CancellationToken ct) =>
        QueryAsync("""
            SELECT s.year,
                   COUNT(DISTINCT s.id),
                   COUNT(*),
                   MIN(l.lap_time_ms),
                   -- Median, not mean: one safety-car lap moves a mean by
                   -- seconds and a median not at all.
                   PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY l.lap_time_ms)
            FROM laps l
            JOIN sessions s ON s.id = l.session_id
            JOIN drivers  d ON d.id = l.driver_id
            WHERE d.code = @code
              AND l.lap_time_ms IS NOT NULL
              AND NOT l.is_pit_in AND NOT l.is_pit_out
            GROUP BY s.year
            ORDER BY s.year DESC
            """,
            reader => new DriverSeasonSummary(
                reader.GetInt16(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3) / 1000.0,
                reader.IsDBNull(4) ? null : reader.GetDouble(4) / 1000.0),
            ct, ("code", code));

    /// <summary>Who ran which strategy, for a whole season at once.</summary>
    public Task<IReadOnlyList<StrategyRow>> StrategiesAsync(
        int year, int? stops, CancellationToken ct)
    {
        // The stop filter is composed into the SQL rather than passed as a
        // nullable parameter. `HAVING @stops IS NULL OR ...` looks tidier but
        // sends an untyped NULL, which Postgres cannot resolve — and the whole
        // query then returns nothing at all.
        var having = stops is null ? "" : "HAVING COUNT(*) - 1 = @stops";

        var sql = $"""
            SELECT s.year, s.meeting_name, d.code,
                   COUNT(*) - 1 AS stops,
                   STRING_AGG(st.compound, ' → ' ORDER BY st.stint)
            FROM stints st
            JOIN sessions s ON s.id = st.session_id
            JOIN drivers  d ON d.id = st.driver_id
            WHERE s.year = @year AND s.session_type = 'Race'
            GROUP BY s.year, s.meeting_name, d.code
            {having}
            ORDER BY s.meeting_name, stops DESC, d.code
            """;

        (string, object)[] parameters = stops is { } value
            ? [("year", year), ("stops", value)]
            : [("year", year)];

        return QueryAsync(sql,
            reader => new StrategyRow(
                reader.GetInt16(0), reader.GetString(1), reader.GetString(2),
                (int)reader.GetInt64(3), reader.GetString(4)),
            ct, parameters);
    }

    /// <summary>
    /// Laps whose peak speed exceeded a threshold, from the telemetry index.
    ///
    /// The one question here that is genuinely a time series rather than a
    /// join, and the reason InfluxDB is in the stack at all.
    /// </summary>
    public async Task<IReadOnlyList<SpeedRow>> FastestLapsAsync(
        int above, int limit, CancellationToken ct)
    {
        if (!influx.Available) return [];

        var flux = $$"""
            from(bucket: "telemetry")
              |> range(start: 2015-01-01T00:00:00Z, stop: 2035-01-01T00:00:00Z)
              |> filter(fn: (r) => r._measurement == "speed")
              |> group(columns: ["driver", "lap", "session"])
              |> max()
              |> filter(fn: (r) => r._value > {{above}})
              |> group()
              |> sort(columns: ["_value"], desc: true)
              |> limit(n: {{limit}})
              |> keep(columns: ["driver", "session", "lap", "_value"])
            """;

        var csv = await influx.QueryAsync(flux, ct).ConfigureAwait(false);
        if (csv is null) return [];

        return ParseSpeedCsv(csv);
    }

    /// <summary>
    /// Parses Influx's annotated CSV.
    ///
    /// Column ORDER is not fixed between queries, so the header row is read
    /// rather than assumed — an index hard-coded from one result set silently
    /// returns the wrong field when the query changes.
    /// </summary>
    internal static IReadOnlyList<SpeedRow> ParseSpeedCsv(string csv)
    {
        var rows = new List<SpeedRow>();
        Dictionary<string, int>? columns = null;

        foreach (var raw in csv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var cells = line.Split(',');

            if (columns is null || cells.Contains("_value"))
            {
                columns = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < cells.Length; i++) columns[cells[i]] = i;
                continue;
            }

            if (!columns.TryGetValue("_value", out var valueAt)) continue;
            if (valueAt >= cells.Length) continue;
            if (!int.TryParse(cells[valueAt], CultureInfo.InvariantCulture, out var speed)) continue;

            rows.Add(new SpeedRow(
                Cell(cells, columns, "driver"),
                Cell(cells, columns, "session"),
                int.TryParse(Cell(cells, columns, "lap"), out var lap) ? lap : 0,
                speed));
        }

        return rows;
    }

    private static string Cell(string[] cells, Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var i) && i < cells.Length ? cells[i] : "";

    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        if (!postgres.Available) return [];

        try
        {
            await using var connection = new NpgsqlConnection(
                Environment.GetEnvironmentVariable("POSTGRES_URL"));
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            var results = new List<T>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(map(reader));
            }
            return results;
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(e, "Insight query failed");
            return [];
        }
    }
}
