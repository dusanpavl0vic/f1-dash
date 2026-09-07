using F1Dash.Core.Analysis;
using F1Dash.Core.Archive;
using F1Dash.Core.Merge;
using F1Dash.Core.Signalr;
using Xunit;
using Xunit.Abstractions;

namespace F1Dash.Core.Tests;

public class LapTimeTests
{
    [Theory]
    [InlineData("1:23.456", 83.456)]
    [InlineData("23.456", 23.456)]
    [InlineData("2:05.983", 125.983)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("—", null)]
    public void Parse_ReadsTheFeedsTimeShapes(string? value, double? expected)
    {
        var parsed = LapTime.Parse(value);
        if (expected is null) Assert.Null(parsed);
        else Assert.Equal(expected.Value, parsed!.Value, 3);
    }

    [Theory]
    [InlineData(83.456, "1:23.456")]
    [InlineData(23.456, "23.456")]
    public void Format_RoundTrips(double seconds, string expected) =>
        Assert.Equal(expected, LapTime.Format(seconds));
}

/// <summary>
/// Runs the builder over a real recorded race. The fixture is not committed, so
/// these skip on a clean checkout; `make fixture` provides it.
/// </summary>
public class AnalysisBuilderTests(ITestOutputHelper output)
{
    private const int OfficialLapCount = 53;

    [SkippableFact]
    public void ReplayingTheRace_ProducesALapTableMatchingTheOfficialResult()
    {
        var path = FixturePath();
        Skip.If(path is null, "Race fixture not downloaded. Run: make fixture");

        var (analysis, _) = Replay(path!);

        output.WriteLine($"{analysis.Drivers.Count} drivers, {analysis.Meta.TotalLaps} laps");
        output.WriteLine("");
        output.WriteLine("  TLA   LAPS  BEST      STINTS");
        foreach (var d in analysis.Drivers.Take(10))
        {
            var stints = string.Join(" ", d.Stints.Select(s => $"{s.Compound[0]}{s.Laps}"));
            output.WriteLine($"  {d.Tla,-4} {d.Laps.Count,5}  {LapTime.Format(d.BestLapSeconds),-9} {stints}");
        }

        Assert.Equal(OfficialLapCount, analysis.Meta.TotalLaps);

        // The winner completed every lap, so the lap table must be complete for
        // them — a gap means laps were dropped as they passed.
        var winner = analysis.Drivers[0];
        Assert.True(winner.Laps.Count >= OfficialLapCount - 1,
            $"{winner.Tla} recorded only {winner.Laps.Count} of {OfficialLapCount} laps.");

        // Monza 2024 was a one-stop race; nobody's fastest lap was anywhere near
        // two minutes.
        Assert.InRange(winner.BestLapSeconds ?? 0, 78, 95);
    }

    [SkippableFact]
    public void LapNumbersAreUniqueAndAscending()
    {
        var path = FixturePath();
        Skip.If(path is null, "Race fixture not downloaded. Run: make fixture");

        var (analysis, _) = Replay(path!);

        foreach (var driver in analysis.Drivers)
        {
            var laps = driver.Laps.Select(l => l.Lap).ToList();
            Assert.Equal(laps.OrderBy(x => x).ToList(), laps);
            Assert.Equal(laps.Distinct().Count(), laps.Count);
        }
    }

    [SkippableFact]
    public void StintsCoverEveryLapAndUseRealCompounds()
    {
        var path = FixturePath();
        Skip.If(path is null, "Race fixture not downloaded. Run: make fixture");

        var (analysis, _) = Replay(path!);
        string[] valid = ["SOFT", "MEDIUM", "HARD", "INTERMEDIATE", "WET", "UNKNOWN"];

        var withStints = analysis.Drivers.Where(d => d.Stints.Count > 0).ToList();
        Assert.NotEmpty(withStints);

        foreach (var driver in withStints)
        {
            foreach (var stint in driver.Stints)
            {
                Assert.Contains(stint.Compound.ToUpperInvariant(), valid);
                Assert.True(stint.Laps > 0);
                Assert.True(stint.EndLap >= stint.StartLap);
            }

            // A one- or two-stop race means two or three stints. More than five
            // would mean the derivation is splitting a single stint.
            Assert.InRange(driver.Stints.Count, 1, 5);
        }

        output.WriteLine("  TLA   STRATEGY");
        foreach (var d in withStints.Take(8))
        {
            output.WriteLine($"  {d.Tla,-4}  " + string.Join(" -> ",
                d.Stints.Select(st => $"{st.Compound} L{st.StartLap}-{st.EndLap} ({st.Laps})")));
        }
    }

    private static (SessionAnalysis, StateAccumulator) Replay(string path)
    {
        var accumulator = new StateAccumulator();
        var builder = new AnalysisBuilder();

        foreach (var entry in SessionDownloader.ReadJsonl(path))
        {
            accumulator.Apply(new TopicUpdate(entry.Topic, entry.Payload, null, IsSnapshot: false));
            builder.Observe(accumulator, entry.Topic);
        }

        var meta = new AnalysisMeta(2024, "Italian Grand Prix", "Race", "Race", "Monza", 39,
            "2024-09-01T15:00:00", 0, false, DateTime.UtcNow.ToString("O"));

        return (builder.Build(accumulator, meta), accumulator);
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
