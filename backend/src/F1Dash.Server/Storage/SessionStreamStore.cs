using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using F1Dash.Core.Archive;
using Npgsql;
using NpgsqlTypes;

namespace F1Dash.Server.Storage;

/// <summary>
/// The raw session stream, in the database rather than on local disk.
///
/// Stored as DEFLATE-compressed chunks of JSONL. Measured on the 2024 Italian
/// Grand Prix: 76 MB of text becomes 8.1 MB, and the whole archive of 420 MB
/// becomes roughly 45 MB.
///
/// Chunked by session time, not by size. A replay seek has to reach an
/// arbitrary offset, and one blob per session would mean decompressing an
/// entire race to start at lap 30. With time chunks the reader skips straight
/// to the window it needs.
///
/// The live merge is NOT affected by any of this. Deltas are merged in memory
/// at ten a second; a database round trip per delta would destroy the only
/// thing this application cannot afford to be slow at.
/// </summary>
public sealed class SessionStreamStore(PostgresStore postgres, ILogger<SessionStreamStore> logger)
{
    /// <summary>
    /// Session-time covered by one chunk.
    ///
    /// Two minutes is roughly 200 KB compressed — small enough that a seek
    /// wastes little, large enough that a two-hour race is about sixty rows
    /// rather than thousands.
    /// </summary>
    private const long ChunkMs = 120_000;

    public bool Available => postgres.Available;

    public async Task InitialiseAsync(CancellationToken ct)
    {
        if (!Available) return;

        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS session_streams (
                    session_id BIGINT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                    chunk      INTEGER NOT NULL,
                    from_ms    BIGINT NOT NULL,
                    to_ms      BIGINT NOT NULL,
                    frames     INTEGER NOT NULL,
                    payload    BYTEA NOT NULL,
                    PRIMARY KEY (session_id, chunk)
                );
                CREATE INDEX IF NOT EXISTS session_streams_range
                    ON session_streams (session_id, from_ms);
                """, connection);

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (NpgsqlException e)
        {
            logger.LogWarning(e, "Could not create the session stream table");
        }
    }

    /// <summary>True when this session's stream is in the database.</summary>
    public async Task<bool> HasAsync(SessionKey key, CancellationToken ct)
    {
        if (!Available) return false;

        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1 FROM session_streams st
                    JOIN sessions s ON s.id = st.session_id
                    WHERE s.year = @year AND s.meeting_slug = @meeting
                      AND s.session_slug = @session)
                """, connection);

            AddKey(command, key);
            return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }
        catch (NpgsqlException e)
        {
            logger.LogWarning(e, "Could not check for {Key}", key);
            return false;
        }
    }

    /// <summary>Uploads a stream file, replacing whatever was stored before.</summary>
    public async Task<int> StoreAsync(SessionKey key, string path, CancellationToken ct)
    {
        if (!Available || !File.Exists(path)) return 0;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);

        var sessionId = await ResolveSessionIdAsync(connection, key, ct).ConfigureAwait(false);
        if (sessionId is null)
        {
            logger.LogWarning("{Key} is not in the sessions table; index it first", key);
            return 0;
        }

        // One transaction: a partly-stored stream would replay as a session
        // that mysteriously stops, which is worse than one that is absent.
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var clear = new NpgsqlCommand(
            "DELETE FROM session_streams WHERE session_id = @id", connection))
        {
            clear.Parameters.AddWithValue("id", sessionId.Value);
            await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var chunks = 0;
        var buffer = new StringBuilder();
        var frames = 0;
        long chunkStart = 0;
        long lastOffset = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;

            var offset = OffsetOf(line);
            if (offset is null) continue;

            if (frames == 0) chunkStart = offset.Value;
            lastOffset = offset.Value;

            buffer.Append(line).Append('\n');
            frames++;

            if (offset.Value - chunkStart < ChunkMs) continue;

            await WriteChunkAsync(connection, sessionId.Value, chunks++, chunkStart,
                lastOffset, frames, buffer, ct).ConfigureAwait(false);
            buffer.Clear();
            frames = 0;
        }

        if (frames > 0)
        {
            await WriteChunkAsync(connection, sessionId.Value, chunks++, chunkStart,
                lastOffset, frames, buffer, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Stored {Key} as {Chunks} chunks", key, chunks);
        return chunks;
    }

    /// <summary>
    /// Reads a session back, starting at or before <paramref name="fromMs"/>.
    ///
    /// Streamed chunk by chunk rather than materialised: a race is half a
    /// million frames, and holding them all to replay them one at a time would
    /// undo the point of storing them compressed.
    /// </summary>
    public async IAsyncEnumerable<StreamEntry> ReadAsync(
        SessionKey key, long fromMs,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!Available) yield break;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);

        await using var command = new NpgsqlCommand("""
            SELECT st.payload
            FROM session_streams st
            JOIN sessions s ON s.id = st.session_id
            WHERE s.year = @year AND s.meeting_slug = @meeting AND s.session_slug = @session
              -- to_ms, not from_ms: the chunk CONTAINING the offset starts
              -- before it, and skipping it would lose the state built up to
              -- that point.
              AND st.to_ms >= @from
            ORDER BY st.chunk
            """, connection);

        AddKey(command, key);
        command.Parameters.AddWithValue("from", fromMs);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var compressed = (byte[])reader[0];

            foreach (var entry in Decode(compressed))
            {
                yield return entry;
            }
        }
    }

    /// <summary>The last offset stored, so the transport bar has a scale.</summary>
    public async Task<long> DurationAsync(SessionKey key, CancellationToken ct)
    {
        if (!Available) return 0;

        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(MAX(st.to_ms), 0)
                FROM session_streams st
                JOIN sessions s ON s.id = st.session_id
                WHERE s.year = @year AND s.meeting_slug = @meeting AND s.session_slug = @session
                """, connection);

            AddKey(command, key);
            return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }
        catch (NpgsqlException)
        {
            return 0;
        }
    }

    private static IEnumerable<StreamEntry> Decode(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        // Raw DEFLATE, matching the rest of the codebase. ZLibStream would fail
        // on the first byte.
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            if (JsonNode.Parse(line) is not JsonObject o) continue;
            if (o["p"] is not JsonObject payload) continue;

            yield return new StreamEntry(
                (long?)o["o"] ?? 0,
                (string?)o["t"] ?? "",
                payload.DeepClone().AsObject());
        }
    }

    private static async Task WriteChunkAsync(
        NpgsqlConnection connection, long sessionId, int chunk,
        long fromMs, long toMs, int frames, StringBuilder text, CancellationToken ct)
    {
        using var output = new MemoryStream();
        await using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(text.ToString());
            await deflate.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        await using var command = new NpgsqlCommand("""
            INSERT INTO session_streams (session_id, chunk, from_ms, to_ms, frames, payload)
            VALUES (@id, @chunk, @from, @to, @frames, @payload)
            """, connection);

        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("chunk", chunk);
        command.Parameters.AddWithValue("from", fromMs);
        command.Parameters.AddWithValue("to", toMs);
        command.Parameters.AddWithValue("frames", frames);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea)
        {
            Value = output.ToArray(),
        });

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads the offset without parsing the whole line.</summary>
    internal static long? OffsetOf(string line)
    {
        const string marker = "\"o\":";
        var at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;

        var start = at + marker.Length;
        var end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-')) end++;

        return end > start && long.TryParse(line.AsSpan(start, end - start), out var value)
            ? value
            : null;
    }

    private static void AddKey(NpgsqlCommand command, SessionKey key)
    {
        command.Parameters.AddWithValue("year", key.Year);
        command.Parameters.AddWithValue("meeting", key.MeetingSlug);
        command.Parameters.AddWithValue("session", key.SessionSlug);
    }

    private static async Task<long?> ResolveSessionIdAsync(
        NpgsqlConnection connection, SessionKey key, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id FROM sessions
            WHERE year = @year AND meeting_slug = @meeting AND session_slug = @session
            """, connection);

        AddKey(command, key);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as long?;
    }

    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("POSTGRES_URL"));
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
