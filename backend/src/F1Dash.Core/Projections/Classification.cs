using System.Globalization;
using System.Text.Json.Nodes;

namespace F1Dash.Core.Projections;

/// <summary>One driver's place in the running order.</summary>
public sealed record ClassifiedDriver(
    int Position,
    string RacingNumber,
    string Tla,
    string TeamName,
    int Laps,
    bool Retired,
    string? BestLapTime);

/// <summary>
/// Reads the running order out of accumulated state.
///
/// This exists mainly to make the accumulator falsifiable: replaying a recorded
/// race and comparing the final top ten against the official result validates
/// parsing, decompression, merging and the per-topic special cases in one
/// assertion (docs/12 Phase 2).
/// </summary>
public static class Classification
{
    public static IReadOnlyList<ClassifiedDriver> From(JsonObject state)
    {
        if (state[Topics.TimingData]?["Lines"] is not JsonObject lines)
        {
            return [];
        }

        var drivers = state[Topics.DriverList] as JsonObject;
        var result = new List<ClassifiedDriver>(lines.Count);

        foreach (var (number, line) in lines)
        {
            if (line is not JsonObject timing) continue;

            // The feed stores Position as a string. A driver who never took part
            // may have no position at all.
            var position = ParseInt(timing["Position"]);
            if (position is null) continue;

            var driver = drivers?[number] as JsonObject;

            result.Add(new ClassifiedDriver(
                Position: position.Value,
                RacingNumber: number,
                Tla: (string?)driver?["Tla"] ?? number,
                TeamName: (string?)driver?["TeamName"] ?? "",
                Laps: ParseInt(timing["NumberOfLaps"]) ?? 0,
                Retired: (bool?)timing["Retired"] ?? false,
                BestLapTime: (string?)timing["BestLapTime"]?["Value"]));
        }

        result.Sort((a, b) => a.Position.CompareTo(b.Position));
        return result;
    }

    /// <summary>
    /// The feed is loosely typed: the same field arrives as a JSON number in one
    /// session and a string in another, so both are accepted.
    /// </summary>
    private static int? ParseInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;

        if (value.TryGetValue<int>(out var i)) return i;

        if (value.TryGetValue<string>(out var s)
            && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
