using F1Dash.Core.Analysis;
using F1Dash.Core.Archive;
using F1Dash.Core.Merge;
using F1Dash.Core.Signalr;
using Xunit;
using Xunit.Abstractions;

namespace F1Dash.Core.Tests;

public class TelemetryRecorderTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(2024, false)]
    [InlineData(2025, false)]
    [InlineData(2026, true)]
    [InlineData(2027, true)]
    [InlineData(null, false)]
    public void TelemetryIsRecordedFor2026Onwards(int? year, bool expected) =>
        Assert.Equal(expected, TelemetryRecorder.IsSupported(year));

    [SkippableFact]
    public void RecordingA2026Race_CapturesRealPerLapTraces()
    {
        var path = FixturePath(2026);
        Skip.If(path is null, "2026 fixture not downloaded.");

        var directory = Path.Combine(Path.GetTempPath(), "f1dash-telemetry-" + Guid.NewGuid().ToString("N"));

        try
        {
            var accumulator = new StateAccumulator();
            using (var recorder = new TelemetryRecorder(directory, 2026))
            {
                Assert.True(recorder.Enabled);

                var seen = 0;
                foreach (var entry in SessionDownloader.ReadJsonl(path!))
                {
                    accumulator.Apply(new TopicUpdate(entry.Topic, entry.Payload, null, false));
                    recorder.Observe(accumulator, entry.Topic);

                    // A slice is enough to prove the mechanism without a
                    // multi-minute test.
                    if (++seen > 40_000) break;
                }

                output.WriteLine($"laps written: {recorder.LapsWritten}");
                Assert.True(recorder.LapsWritten > 0, "No laps were written.");
            }

            var files = Directory.GetFiles(directory, "*.jsonl");
            Assert.NotEmpty(files);

            var number = Path.GetFileNameWithoutExtension(files[0]);
            var laps = TelemetryRecorder.Read(directory, number).ToList();
            Assert.NotEmpty(laps);

            var lap = laps[0];
            output.WriteLine(
                $"car {number} lap {lap.Lap}: {lap.Speed.Count} samples, " +
                $"speed {lap.Speed.Min()}-{lap.Speed.Max()} km/h, " +
                $"gear {lap.Gear.Min()}-{lap.Gear.Max()}, " +
                $"rpm max {lap.Rpm.Max()}");

            // Every channel must be the same length or the arrays cannot be
            // read as parallel.
            Assert.Equal(lap.Speed.Count, lap.OffsetMs.Count);
            Assert.Equal(lap.Speed.Count, lap.Brake.Count);
            Assert.Equal(lap.Speed.Count, lap.Throttle.Count);

            // Real F1 telemetry, not zeros or noise.
            Assert.True(lap.Speed.Max() > 200, $"Top speed only {lap.Speed.Max()} km/h.");
            Assert.InRange(lap.Gear.Max(), 5, 8);
            Assert.True(lap.Rpm.Max() > 5000);

            // Time must advance strictly, or the chart doubles back on itself.
            for (var i = 1; i < lap.OffsetMs.Count; i++)
            {
                Assert.True(lap.OffsetMs[i] > lap.OffsetMs[i - 1]);
            }

            Assert.Contains(lap.Lap, TelemetryRecorder.AvailableLaps(directory, number));

            // Bounded: without the cap the first "lap" swallowed the whole
            // pre-race period and ran to 12,922 samples.
            foreach (var recorded in laps)
            {
                Assert.InRange(recorded.Speed.Count, 20, TelemetryRecorder.MaxSamplesPerLap);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecorderIsInertForAnOlderSeason()
    {
        var directory = Path.Combine(Path.GetTempPath(), "f1dash-telemetry-" + Guid.NewGuid().ToString("N"));
        using var recorder = new TelemetryRecorder(directory, 2024);

        Assert.False(recorder.Enabled);
        // Nothing is created for a season we deliberately do not record.
        Assert.False(Directory.Exists(directory));
    }

    private static string? FixturePath(int year)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "data", "archive", year.ToString(), "italian-grand-prix", "race", "stream.jsonl");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
