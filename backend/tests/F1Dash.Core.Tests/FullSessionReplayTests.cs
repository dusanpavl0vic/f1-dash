using System.Diagnostics;
using F1Dash.Core.Archive;
using F1Dash.Core.Merge;
using F1Dash.Core.Projections;
using F1Dash.Core.Signalr;
using Xunit;
using Xunit.Abstractions;

namespace F1Dash.Core.Tests;

/// <summary>
/// The single most valuable test in the project (docs/12 Phase 2, docs/14 §2).
///
/// Replaying a recorded race through the accumulator and comparing the final
/// classification against the official result validates parsing, .z
/// decompression, the merge algorithm, the per-topic special cases and the
/// state model — all in one assertion. If this passes, the whole ingest layer
/// is very probably correct. If it breaks, something fundamental broke.
///
/// The fixture is 75 MB and is not committed. `make fixture` downloads it:
///   dotnet run --project src/F1Dash.Server -- archive 2024 Italian Race
/// Tests skip rather than fail when it is absent, so a clean checkout is green.
/// </summary>
public class FullSessionReplayTests(ITestOutputHelper output)
{
    /// <summary>
    /// The official 2024 Italian Grand Prix classification, taken from the
    /// Jolpica results API — an independent source, not from this codebase and
    /// not from the same archive being replayed.
    /// </summary>
    private static readonly string[] OfficialTopTen =
        ["LEC", "PIA", "NOR", "SAI", "HAM", "VER", "RUS", "PER", "ALB", "MAG"];

    private const int OfficialLapCount = 53;
    private const int OfficialFinishers = 20;

    [SkippableFact]
    public void ReplayingTheRace_ProducesTheOfficialClassification()
    {
        var path = FixturePath();
        Skip.If(path is null, "Race fixture not downloaded. Run: make fixture");

        var sw = Stopwatch.StartNew();
        var state = ReplayAll(path!, out var entryCount);
        var elapsed = sw.Elapsed;

        var classification = Classification.From(state);

        output.WriteLine($"replayed {entryCount:N0} entries in {elapsed.TotalSeconds:F1}s");
        output.WriteLine("");
        output.WriteLine("  POS  TLA   TEAM               LAPS  BEST");
        foreach (var d in classification.Take(12))
        {
            output.WriteLine($"  {d.Position,3}  {d.Tla,-4}  {d.TeamName,-18} {d.Laps,4}  {d.BestLapTime}");
        }

        Assert.Equal(OfficialTopTen, classification.Take(10).Select(d => d.Tla).ToArray());
    }

    [SkippableFact]
    public void ReplayingTheRace_ProducesTheOfficialLapCountAndFieldSize()
    {
        var path = FixturePath();
        Skip.If(path is null, "Race fixture not downloaded. Run: make fixture");

        var state = ReplayAll(path!, out _);
        var classification = Classification.From(state);

        Assert.Equal(OfficialFinishers, classification.Count);
        Assert.Equal(OfficialLapCount, classification[0].Laps);
    }

    [SkippableFact]
    public void ReplayingTheRace_HoldsOnlyTheLatestPositionBatch()
    {
        // Position and CarData must replace, not merge. At ~4 Hz across two
        // hours, merging would be the difference between a flat heap and
        // hundreds of megabytes, and stale cars would sit on the map forever.
        //
        // The precise assertion: after replaying the whole race, the state must
        // hold exactly the frames of the LAST batch — not the sum of all of them.
        var path = FixturePath();
        Skip.If(path is null, "Race fixture not downloaded. Run: make fixture");

        var accumulator = new StateAccumulator();
        var lastPositionFrames = 0;
        var lastCarDataFrames = 0;
        var positionBatches = 0;

        foreach (var entry in SessionDownloader.ReadJsonl(path!))
        {
            accumulator.Apply(new TopicUpdate(entry.Topic, entry.Payload, null, IsSnapshot: false));

            if (entry.Topic == Topics.Position)
            {
                lastPositionFrames = (entry.Payload["Position"] as System.Text.Json.Nodes.JsonObject)?.Count ?? 0;
                positionBatches++;
            }
            else if (entry.Topic == Topics.CarData)
            {
                lastCarDataFrames = (entry.Payload["Entries"] as System.Text.Json.Nodes.JsonObject)?.Count ?? 0;
            }
        }

        var state = accumulator.Snapshot();
        var heldPosition = (state[Topics.Position]?["Position"] as System.Text.Json.Nodes.JsonObject)?.Count ?? 0;
        var heldCarData = (state[Topics.CarData]?["Entries"] as System.Text.Json.Nodes.JsonObject)?.Count ?? 0;

        output.WriteLine($"{positionBatches:N0} Position batches arrived; state holds {heldPosition} frame(s)");

        Assert.Equal(lastPositionFrames, heldPosition);
        Assert.Equal(lastCarDataFrames, heldCarData);
    }

    [SkippableFact]
    public void ReplayingTheRace_RecordsEveryRaceControlMessageInOrder()
    {
        var path = FixturePath();
        Skip.If(path is null, "Race fixture not downloaded. Run: make fixture");

        var state = ReplayAll(path!, out _);
        var messages = state[Topics.RaceControlMessages]!["Messages"]!.AsObject();

        output.WriteLine($"{messages.Count} race control messages");

        // A real race always produces a substantial log; a handful would mean
        // the append-only path is overwriting entries.
        Assert.True(messages.Count > 30, $"Only {messages.Count} messages survived — append-only merge is dropping entries.");

        // Indices must be a dense 0..n-1 range, or the client's ordering breaks.
        var indices = messages.Select(kv => int.Parse(kv.Key)).Order().ToArray();
        Assert.Equal(Enumerable.Range(0, messages.Count), indices);
    }

    private static System.Text.Json.Nodes.JsonObject ReplayAll(string path, out int entryCount)
    {
        var accumulator = new StateAccumulator();
        var count = 0;

        foreach (var entry in SessionDownloader.ReadJsonl(path))
        {
            accumulator.Apply(new TopicUpdate(entry.Topic, entry.Payload, null, IsSnapshot: count == 0));
            count++;
        }

        entryCount = count;
        return accumulator.Snapshot();
    }

    private static string? FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "data", "archive", "2024", "italian-grand-prix", "race", "stream.jsonl");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
