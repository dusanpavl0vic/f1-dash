using F1Dash.Core.Analysis;
using System.Net.Sockets;
using Npgsql;

namespace F1Dash.Server.Storage;

/// <summary>
/// The relational index: sessions, drivers, laps, stints.
///
/// This is the store that earns its place most clearly. "Every driver's Monza
/// stint history since 2018" is one indexed query here and a walk over every
/// file in the archive without it.
///
/// Schema and reasoning are in docs/22 §3. The one thing worth repeating: a
/// driver is keyed by a stable reference, never by racing number, because
/// numbers are reassigned between seasons and keying on them silently merges
/// two people's careers while looking correct.
/// </summary>
public sealed class PostgresStore : IStorageIndex
{
    private readonly ILogger _logger;
    private readonly string? _connectionString;

    public PostgresStore(ILogger<PostgresStore> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("POSTGRES_URL");
        Available = !string.IsNullOrWhiteSpace(_connectionString);
    }

    public string Name => "postgres";
    public bool Available { get; private set; }

    public async Task InitialiseAsync(CancellationToken ct)
    {
        if (!Available) return;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(Schema, connection);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("PostgreSQL schema ready");
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or SocketException)
        {
            _logger.LogWarning(e, "PostgreSQL unreachable; relational indexing disabled.");
            Available = false;
        }
    }

    public async Task IndexAsync(SessionKey key, SessionAnalysis analysis, CancellationToken ct)
    {
        if (!Available) return;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            // One transaction per session. A half-indexed session is worse than
            // an unindexed one: a query would return it and quietly under-report.
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            var sessionId = await UpsertSessionAsync(connection, key, analysis.Meta, ct)
                .ConfigureAwait(false);

            // Replacing rather than merging. Re-indexing exists to repair, and a
            // merge would leave rows from a previous, wrong parse in place.
            await ExecuteAsync(connection,
                "DELETE FROM laps WHERE session_id = @s", ct, ("s", sessionId)).ConfigureAwait(false);
            await ExecuteAsync(connection,
                "DELETE FROM stints WHERE session_id = @s", ct, ("s", sessionId)).ConfigureAwait(false);

            foreach (var driver in analysis.Drivers)
            {
                var driverId = await UpsertDriverAsync(connection, driver, ct).ConfigureAwait(false);
                await UpsertEntryAsync(connection, sessionId, driverId, driver, ct).ConfigureAwait(false);
                await InsertLapsAsync(connection, sessionId, driverId, driver, ct).ConfigureAwait(false);
                await InsertStintsAsync(connection, sessionId, driverId, driver, ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Indexed {Key} into PostgreSQL ({Drivers} drivers)",
                key, analysis.Drivers.Count);
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or SocketException)
        {
            _logger.LogWarning(e, "Could not index {Key} into PostgreSQL", key);
        }
    }

    private static async Task<long> UpsertSessionAsync(
        NpgsqlConnection connection, SessionKey key, AnalysisMeta meta, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO sessions
                (year, meeting_name, meeting_slug, session_name, session_slug,
                 session_type, circuit_key, circuit_name, start_utc, total_laps,
                 downloaded, ingested_at)
            VALUES
                (@year, @meeting, @meetingSlug, @session, @sessionSlug,
                 @type, @circuitKey, @circuit, @start, @totalLaps, TRUE, NOW())
            ON CONFLICT (year, meeting_slug, session_slug) DO UPDATE SET
                meeting_name = EXCLUDED.meeting_name,
                session_name = EXCLUDED.session_name,
                session_type = EXCLUDED.session_type,
                circuit_key  = EXCLUDED.circuit_key,
                circuit_name = EXCLUDED.circuit_name,
                start_utc    = EXCLUDED.start_utc,
                total_laps   = EXCLUDED.total_laps,
                downloaded   = TRUE,
                ingested_at  = NOW()
            RETURNING id
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("year", key.Year);
        command.Parameters.AddWithValue("meeting", meta.Meeting);
        command.Parameters.AddWithValue("meetingSlug", key.MeetingSlug);
        command.Parameters.AddWithValue("session", meta.SessionName);
        command.Parameters.AddWithValue("sessionSlug", key.SessionSlug);
        command.Parameters.AddWithValue("type", meta.SessionType);
        command.Parameters.AddWithValue("circuitKey", (object?)meta.CircuitKey ?? DBNull.Value);
        command.Parameters.AddWithValue("circuit", meta.Circuit);
        command.Parameters.AddWithValue("start",
            DateTime.TryParse(meta.StartDate, out var start)
                ? DateTime.SpecifyKind(start, DateTimeKind.Utc)
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("totalLaps", meta.TotalLaps);

        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static async Task<long> UpsertDriverAsync(
        NpgsqlConnection connection, DriverAnalysis driver, CancellationToken ct)
    {
        // The TLA is the stable identifier available here — the feed does not
        // carry Jolpica's driver id. It is stable within and across seasons in
        // practice, and unlike the racing number it is not reassigned.
        const string sql = """
            INSERT INTO drivers (driver_ref, code)
            VALUES (@ref, @code)
            ON CONFLICT (driver_ref) DO UPDATE SET code = EXCLUDED.code
            RETURNING id
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ref", driver.Tla);
        command.Parameters.AddWithValue("code", driver.Tla);

        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static Task UpsertEntryAsync(
        NpgsqlConnection connection, long sessionId, long driverId,
        DriverAnalysis driver, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO session_entries
                (session_id, driver_id, racing_number, tla, team_name, team_colour)
            VALUES (@s, @d, @number, @tla, @team, @colour)
            ON CONFLICT (session_id, driver_id) DO UPDATE SET
                team_name = EXCLUDED.team_name,
                team_colour = EXCLUDED.team_colour
            """;

        return ExecuteAsync(connection, sql, ct,
            ("s", sessionId), ("d", driverId),
            ("number", int.TryParse(driver.RacingNumber, out var n) ? n : 0),
            ("tla", driver.Tla), ("team", driver.TeamName), ("colour", driver.TeamColour));
    }

    private static async Task InsertLapsAsync(
        NpgsqlConnection connection, long sessionId, long driverId,
        DriverAnalysis driver, CancellationToken ct)
    {
        if (driver.Laps.Count == 0) return;

        // Binary COPY rather than one INSERT per lap. A season is roughly
        // 120,000 laps; at a round trip each that is minutes of pure latency.
        await using var writer = await connection.BeginBinaryImportAsync(
            """
            COPY laps (session_id, driver_id, lap, lap_time_ms,
                       sector1_ms, sector2_ms, sector3_ms,
                       position, track_status, is_pit_out, is_pit_in)
            FROM STDIN (FORMAT BINARY)
            """, ct).ConfigureAwait(false);

        foreach (var lap in driver.Laps)
        {
            await writer.StartRowAsync(ct).ConfigureAwait(false);
            await writer.WriteAsync(sessionId, ct).ConfigureAwait(false);
            await writer.WriteAsync(driverId, ct).ConfigureAwait(false);
            await writer.WriteAsync((short)lap.Lap, ct).ConfigureAwait(false);
            await WriteMsAsync(writer, lap.TimeSeconds, ct).ConfigureAwait(false);
            await WriteMsAsync(writer, lap.Sector1, ct).ConfigureAwait(false);
            await WriteMsAsync(writer, lap.Sector2, ct).ConfigureAwait(false);
            await WriteMsAsync(writer, lap.Sector3, ct).ConfigureAwait(false);
            await WriteShortAsync(writer, lap.Position, ct).ConfigureAwait(false);
            await WriteShortAsync(writer,
                short.TryParse(lap.TrackStatus, out var status) ? status : null, ct).ConfigureAwait(false);
            await writer.WriteAsync(lap.PitOut, ct).ConfigureAwait(false);
            await writer.WriteAsync(lap.InPit, ct).ConfigureAwait(false);
        }

        await writer.CompleteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Seconds to whole milliseconds — the column is an integer on purpose.</summary>
    private static Task WriteMsAsync(NpgsqlBinaryImporter writer, double? seconds, CancellationToken ct) =>
        seconds is { } value and > 0
            ? writer.WriteAsync((int)Math.Round(value * 1000), NpgsqlTypes.NpgsqlDbType.Integer, ct)
            : writer.WriteNullAsync(ct);

    private static Task WriteShortAsync(NpgsqlBinaryImporter writer, int? value, CancellationToken ct) =>
        value is { } v
            ? writer.WriteAsync((short)v, NpgsqlTypes.NpgsqlDbType.Smallint, ct)
            : writer.WriteNullAsync(ct);

    private static async Task InsertStintsAsync(
        NpgsqlConnection connection, long sessionId, long driverId,
        DriverAnalysis driver, CancellationToken ct)
    {
        foreach (var stint in driver.Stints)
        {
            await ExecuteAsync(connection, """
                INSERT INTO stints
                    (session_id, driver_id, stint, compound, is_new,
                     start_lap, end_lap, laps_on_tyre)
                VALUES (@s, @d, @i, @c, @new, @from, @to, @laps)
                """, ct,
                ("s", sessionId), ("d", driverId), ("i", (short)stint.Index),
                ("c", stint.Compound), ("new", stint.NewTyres),
                ("from", (short)stint.StartLap), ("to", (short)stint.EndLap),
                ("laps", (short)stint.Laps)).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Idempotent DDL, applied on every start. See docs/22 §3.</summary>
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS sessions (
            id BIGSERIAL PRIMARY KEY,
            year SMALLINT NOT NULL,
            round SMALLINT,
            meeting_name TEXT NOT NULL,
            meeting_slug TEXT NOT NULL,
            session_name TEXT NOT NULL,
            session_slug TEXT NOT NULL,
            session_type TEXT NOT NULL,
            circuit_key INTEGER,
            circuit_name TEXT,
            country_code TEXT,
            start_utc TIMESTAMPTZ,
            end_utc TIMESTAMPTZ,
            downloaded BOOLEAN NOT NULL DEFAULT FALSE,
            total_laps SMALLINT,
            ingested_at TIMESTAMPTZ,
            UNIQUE (year, meeting_slug, session_slug)
        );
        CREATE INDEX IF NOT EXISTS sessions_year_idx ON sessions (year, round);
        CREATE INDEX IF NOT EXISTS sessions_circuit_idx ON sessions (circuit_key, year);

        CREATE TABLE IF NOT EXISTS drivers (
            id BIGSERIAL PRIMARY KEY,
            driver_ref TEXT NOT NULL UNIQUE,
            code TEXT,
            first_name TEXT,
            last_name TEXT,
            nationality TEXT,
            country_code TEXT
        );

        CREATE TABLE IF NOT EXISTS session_entries (
            session_id BIGINT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
            driver_id BIGINT NOT NULL REFERENCES drivers(id),
            racing_number SMALLINT NOT NULL,
            tla TEXT NOT NULL,
            team_name TEXT,
            team_colour TEXT,
            PRIMARY KEY (session_id, driver_id)
        );

        CREATE TABLE IF NOT EXISTS laps (
            session_id BIGINT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
            driver_id BIGINT NOT NULL REFERENCES drivers(id),
            lap SMALLINT NOT NULL,
            lap_time_ms INTEGER,
            sector1_ms INTEGER,
            sector2_ms INTEGER,
            sector3_ms INTEGER,
            position SMALLINT,
            track_status SMALLINT,
            is_pit_out BOOLEAN NOT NULL DEFAULT FALSE,
            is_pit_in BOOLEAN NOT NULL DEFAULT FALSE,
            PRIMARY KEY (session_id, driver_id, lap)
        );
        CREATE INDEX IF NOT EXISTS laps_time_idx ON laps (session_id, lap_time_ms)
            WHERE lap_time_ms IS NOT NULL;

        CREATE TABLE IF NOT EXISTS stints (
            session_id BIGINT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
            driver_id BIGINT NOT NULL REFERENCES drivers(id),
            stint SMALLINT NOT NULL,
            compound TEXT NOT NULL,
            is_new BOOLEAN,
            start_lap SMALLINT NOT NULL,
            end_lap SMALLINT,
            laps_on_tyre SMALLINT,
            PRIMARY KEY (session_id, driver_id, stint)
        );

        CREATE TABLE IF NOT EXISTS results (
            session_id BIGINT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
            driver_id BIGINT NOT NULL REFERENCES drivers(id),
            position SMALLINT,
            grid SMALLINT,
            points NUMERIC(5,2) NOT NULL DEFAULT 0,
            status TEXT,
            laps_completed SMALLINT,
            PRIMARY KEY (session_id, driver_id)
        );
        """;
}
