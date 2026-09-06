using System.Globalization;
using System.Text.Json.Nodes;
using F1Dash.Core.Merge;
using F1Dash.Core.Signalr;

namespace F1Dash.Core.Archive;

/// <summary>
/// Parses the archive's ".jsonStream" format (docs/03 §2).
///
/// Each line is a 12-character timestamp followed immediately by a JSON
/// payload, with no separator:
///
///     00:00:12.345{"Status":"1","Message":"AllClear"}
///
/// The timestamp is HH:MM:SS.mmm relative to session start. For ".z" topics the
/// payload is a JSON string holding base64 raw-DEFLATE, not an object.
///
/// The files are served with a UTF-8 BOM; callers must read them as utf-8-sig
/// or the first timestamp of every file fails to parse.
/// </summary>
public static class JsonStreamParser
{
    /// <summary>Length of the "HH:MM:SS.mmm" prefix.</summary>
    public const int TimestampLength = 12;

    /// <summary>
    /// Parses one line. Returns false for blank lines and for lines whose
    /// payload cannot be read — a single corrupt line must not abort a
    /// two-hour session.
    /// </summary>
    public static bool TryParseLine(
        ReadOnlySpan<char> line,
        string topic,
        out StreamEntry entry)
    {
        entry = default;

        // Strip a BOM if the caller did not decode as utf-8-sig.
        if (line.Length > 0 && line[0] == '﻿') line = line[1..];
        line = line.TrimEnd('\r');

        if (line.Length <= TimestampLength) return false;

        if (!TryParseOffset(line[..TimestampLength], out var offsetMs)) return false;

        var payloadText = line[TimestampLength..].ToString();

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payloadText);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        var normalisedTopic = Topics.Normalise(topic);

        if (Topics.IsCompressed(topic))
        {
            // A compressed topic's line payload is a JSON string of base64.
            if (node is not JsonValue value || !value.TryGetValue<string>(out var base64)) return false;

            try
            {
                node = Inflate.Decode(base64);
            }
            catch (Exception e) when (e is FormatException or InvalidDataException or System.Text.Json.JsonException)
            {
                return false;
            }
        }

        entry = new StreamEntry(offsetMs, normalisedTopic, DeltaMerge.Normalise(node));
        return true;
    }

    /// <summary>
    /// Parses "HH:MM:SS.mmm". Hours can exceed 24 in a long archived session, so
    /// this is deliberately not TimeSpan.Parse with a fixed format.
    /// </summary>
    public static bool TryParseOffset(ReadOnlySpan<char> stamp, out long offsetMs)
    {
        offsetMs = 0;
        if (stamp.Length != TimestampLength) return false;
        if (stamp[2] != ':' || stamp[5] != ':' || stamp[8] != '.') return false;

        if (!int.TryParse(stamp[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var h)) return false;
        if (!int.TryParse(stamp.Slice(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return false;
        if (!int.TryParse(stamp.Slice(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var s)) return false;
        if (!int.TryParse(stamp.Slice(9, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var ms)) return false;

        offsetMs = (((h * 60L + m) * 60L + s) * 1000L) + ms;
        return true;
    }

    /// <summary>Parses a whole stream file for one topic.</summary>
    public static IEnumerable<StreamEntry> Parse(TextReader reader, string topic)
    {
        while (reader.ReadLine() is { } line)
        {
            if (TryParseLine(line, topic, out var entry))
            {
                yield return entry;
            }
        }
    }
}
