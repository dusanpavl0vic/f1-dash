using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using F1Dash.Core.Merge;

namespace F1Dash.Core.Analysis;

/// <summary>
/// Captures per-lap telemetry traces while a session runs.
///
/// CarData holds only the latest batch, so a lap's telemetry exists only in the
/// moment it streams past. This buffers the current lap per driver and writes it
/// out the instant the lap completes, which is what keeps memory flat: a full
/// race for twenty cars is roughly 400,000 samples across six channels, far too
/// much to hold and serialise at the end.
///
/// Only enabled for 2026 and later (docs/20 Phase H). Older seasons still get
/// laps, stints and sector comparisons; keeping full telemetry for every
/// archived season back to 2018 would be tens of gigabytes of data that is
/// already in the archive and replayable on demand.
/// </summary>
public sealed class TelemetryRecorder : IDisposable
{
    /// <summary>The first season for which telemetry history is kept.</summary>
    public const int FirstRecordedSeason = 2026;

    /// <summary>
    /// A hard cap on samples held for one lap, about eight minutes at the feed's
    /// ~4 Hz — generous even for a lap behind the safety car.
    ///
    /// Without it the first "lap" swallows the whole pre-race period: the buffer
    /// opens when telemetry starts and does not close until the lap counter
    /// first advances, which produced a 12,922-sample lap 1 where a real lap is
    /// about 340. Trimming from the FRONT keeps the samples nearest the lap
    /// boundary, which are the ones that belong to the lap.
    /// </summary>
    public const int MaxSamplesPerLap = 2000;

    private readonly string _directory;
    private readonly Dictionary<string, LapBuffer> _buffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StreamWriter> _writers = new(StringComparer.Ordinal);

    public bool Enabled { get; }
    public int LapsWritten { get; private set; }

    public TelemetryRecorder(string directory, int? year)
    {
        _directory = directory;
        Enabled = year is >= FirstRecordedSeason;

        if (Enabled) Directory.CreateDirectory(directory);
    }

    public static bool IsSupported(int? year) => year is >= FirstRecordedSeason;

    /// <summary>Called after each update is applied, with the topic that changed.</summary>
    public void Observe(StateAccumulator state, string topic)
    {
        if (!Enabled) return;

        switch (topic)
        {
            case Topics.CarData:
                Sample(state);
                break;

            // The lap counter is the only reliable boundary: CarData carries no
            // lap of its own.
            case Topics.TimingData:
                FlushCompletedLaps(state);
                break;
        }
    }

    private void Sample(StateAccumulator state)
    {
        var frames = Ordered(state[Topics.CarData]?["Entries"]);

        foreach (var (_, frame) in frames)
        {
            var utc = (string?)frame["Utc"];
            if (frame["Cars"] is not JsonObject cars) continue;

            var timestamp = ParseUtc(utc);

            foreach (var (number, raw) in cars)
            {
                if (raw is not JsonObject car || car["Channels"] is not JsonObject channels) continue;

                var buffer = Buffer(number);
                buffer.LapStart ??= timestamp;

                var offset = timestamp is { } t && buffer.LapStart is { } start
                    ? (int)(t - start).TotalMilliseconds
                    : buffer.Offset.Count * 250;   // ~4 Hz when the feed omits Utc

                // Duplicate frames are common; a repeated timestamp is not a
                // new sample and would flat-line the chart.
                if (buffer.Offset.Count > 0 && offset <= buffer.Offset[^1]) continue;

                buffer.Offset.Add(offset);
                buffer.Speed.Add(Channel(channels, CarDataChannel.Speed));
                buffer.Throttle.Add(Channel(channels, CarDataChannel.Throttle));
                buffer.Brake.Add(Channel(channels, CarDataChannel.Brake));
                buffer.Gear.Add(Channel(channels, CarDataChannel.Gear));
                buffer.Rpm.Add(Channel(channels, CarDataChannel.Rpm));

                buffer.TrimTo(MaxSamplesPerLap);
            }
        }
    }

    private void FlushCompletedLaps(StateAccumulator state)
    {
        if (state[Topics.TimingData]?["Lines"] is not JsonObject lines) return;

        foreach (var (number, raw) in lines)
        {
            if (raw is not JsonObject line) continue;
            if (ReadInt(line["NumberOfLaps"]) is not { } lap) continue;

            var buffer = Buffer(number);

            if (buffer.Lap == 0)
            {
                buffer.Lap = lap;
                continue;
            }

            if (lap <= buffer.Lap) continue;

            // A lap with only a handful of samples is a feed gap, not a lap.
            if (buffer.Offset.Count >= 20)
            {
                Write(number, buffer.Lap, buffer);
                LapsWritten++;
            }

            buffer.Reset(lap);
        }
    }

    private void Write(string number, int lap, LapBuffer buffer)
    {
        var record = new JsonObject
        {
            ["lap"] = lap,
            ["t"] = ToArray(buffer.Offset),
            ["speed"] = ToArray(buffer.Speed),
            ["throttle"] = ToArray(buffer.Throttle),
            ["brake"] = ToArray(buffer.Brake),
            ["gear"] = ToArray(buffer.Gear),
            ["rpm"] = ToArray(buffer.Rpm),
        };

        Writer(number).WriteLine(record.ToJsonString());
    }

    /// <summary>Reads one driver's laps back, optionally just one lap.</summary>
    public static IEnumerable<TelemetryLap> Read(string directory, string number, int? lap = null)
    {
        var path = Path.Combine(directory, $"{number}.jsonl");
        if (!File.Exists(path)) yield break;

        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            if (JsonNode.Parse(line) is not JsonObject o) continue;

            var lapNumber = (int?)o["lap"] ?? 0;
            if (lap is { } wanted && lapNumber != wanted) continue;

            yield return new TelemetryLap(
                lapNumber,
                Ints(o["t"]), Ints(o["speed"]), Ints(o["throttle"]),
                Ints(o["brake"]), Ints(o["gear"]), Ints(o["rpm"]));

            if (lap is not null) yield break;
        }
    }

    /// <summary>Which laps were captured for a driver, without reading the traces.</summary>
    public static IReadOnlyList<int> AvailableLaps(string directory, string number)
    {
        var path = Path.Combine(directory, $"{number}.jsonl");
        if (!File.Exists(path)) return [];

        var laps = new List<int>();
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } line)
        {
            if (JsonNode.Parse(line) is JsonObject o && (int?)o["lap"] is { } lap) laps.Add(lap);
        }
        return laps;
    }

    public void Dispose()
    {
        foreach (var writer in _writers.Values)
        {
            writer.Flush();
            writer.Dispose();
        }
        _writers.Clear();
    }

    private StreamWriter Writer(string number)
    {
        if (_writers.TryGetValue(number, out var existing)) return existing;

        var writer = new StreamWriter(Path.Combine(_directory, $"{number}.jsonl"), append: false)
        {
            AutoFlush = true,
        };
        _writers[number] = writer;
        return writer;
    }

    private LapBuffer Buffer(string number)
    {
        if (_buffers.TryGetValue(number, out var existing)) return existing;

        var created = new LapBuffer();
        _buffers[number] = created;
        return created;
    }

    private static JsonArray ToArray(List<int> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    private static int[] Ints(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(v => (int?)v ?? 0)] : [];

    private static int Channel(JsonObject channels, string key) =>
        channels[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;

    private static DateTime? ParseUtc(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var i)) return i;
        return value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) ? parsed : null;
    }

    private static List<(int, JsonObject)> Ordered(JsonNode? node)
    {
        var result = new List<(int, JsonObject)>();

        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonObject o) result.Add((i, o));
                }
                break;

            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (int.TryParse(key, out var index) && value is JsonObject o) result.Add((index, o));
                }
                result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                break;
        }

        return result;
    }

    private sealed class LapBuffer
    {
        public int Lap { get; set; }
        public DateTime? LapStart { get; set; }
        public List<int> Offset { get; } = [];
        public List<int> Speed { get; } = [];
        public List<int> Throttle { get; } = [];
        public List<int> Brake { get; } = [];
        public List<int> Gear { get; } = [];
        public List<int> Rpm { get; } = [];

        /// <summary>Keeps the newest <paramref name="max"/> samples.</summary>
        public void TrimTo(int max)
        {
            var excess = Offset.Count - max;
            if (excess <= 0) return;

            Offset.RemoveRange(0, excess);
            Speed.RemoveRange(0, excess);
            Throttle.RemoveRange(0, excess);
            Brake.RemoveRange(0, excess);
            Gear.RemoveRange(0, excess);
            Rpm.RemoveRange(0, excess);
        }

        public void Reset(int lap)
        {
            Lap = lap;
            LapStart = null;
            Offset.Clear(); Speed.Clear(); Throttle.Clear();
            Brake.Clear(); Gear.Clear(); Rpm.Clear();
        }
    }
}
