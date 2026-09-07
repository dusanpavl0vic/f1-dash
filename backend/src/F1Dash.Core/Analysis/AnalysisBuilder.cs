using System.Text.Json.Nodes;
using F1Dash.Core.Merge;

namespace F1Dash.Core.Analysis;

/// <summary>
/// Accumulates a session's history from the same topic stream the dashboard
/// consumes.
///
/// This exists because the feed is ephemeral: TimingData carries only a
/// driver's *current* lap time, CarData only the *latest* telemetry batch, and
/// nothing is ever republished. A lap that is not captured as it passes is gone
/// for a live session — there is exactly one chance.
///
/// It runs for replayed sessions too. A path exercised only on the ~24 live
/// weekends a year is a path that breaks on race day.
/// </summary>
public sealed class AnalysisBuilder
{
    private readonly Dictionary<string, DriverHistory> _drivers = new(StringComparer.Ordinal);
    private string _trackStatus = "1";
    private int _currentLap;

    public int TotalLaps { get; private set; }

    /// <summary>
    /// Reads the merged state after an update has been applied.
    ///
    /// It reads the accumulator directly rather than a snapshot clone: cloning a
    /// 1–2 MB state on every delta would cost more than everything else in the
    /// ingest path put together.
    /// </summary>
    public void Observe(StateAccumulator state, string topic)
    {
        switch (topic)
        {
            case Topics.TrackStatus:
                _trackStatus = (string?)state[Topics.TrackStatus]?["Status"] ?? _trackStatus;
                break;

            case Topics.LapCount:
                _currentLap = ReadInt(state[Topics.LapCount]?["CurrentLap"]) ?? _currentLap;
                TotalLaps = ReadInt(state[Topics.LapCount]?["TotalLaps"]) ?? TotalLaps;
                break;

            case Topics.TimingData:
                ObserveTiming(state);
                break;

            case Topics.TimingAppData:
                ObserveStints(state);
                break;

            case Topics.CurrentTyres:
                ObserveCurrentTyres(state);
                break;
        }
    }

    private void ObserveTiming(StateAccumulator state)
    {
        if (state[Topics.TimingData]?["Lines"] is not JsonObject lines) return;

        foreach (var (number, raw) in lines)
        {
            if (raw is not JsonObject line) continue;

            var history = History(number);

            history.Position = ReadInt(line["Position"]) ?? history.Position;
            history.InPit = (bool?)line["InPit"] ?? false;
            history.PitOut = (bool?)line["PitOut"] ?? false;

            // Sectors are read continuously; whichever values stand when the lap
            // completes are that lap's.
            if (line["Sectors"] is JsonObject sectors)
            {
                history.Sector1 = LapTime.Parse((string?)sectors["0"]?["Value"]) ?? history.Sector1;
                history.Sector2 = LapTime.Parse((string?)sectors["1"]?["Value"]) ?? history.Sector2;
                history.Sector3 = LapTime.Parse((string?)sectors["2"]?["Value"]) ?? history.Sector3;
            }

            var lapNumber = ReadInt(line["NumberOfLaps"]);
            var lastLap = LapTime.Parse((string?)line["LastLapTime"]?["Value"]);

            // A lap is complete when the feed publishes a new LastLapTime. The
            // lap counter is used for the number because it is authoritative,
            // and the guard stops a repeated LastLapTime delta from recording
            // the same lap twice.
            if (lastLap is null || lapNumber is not { } lap || lap <= history.LastRecordedLap) continue;

            history.Laps.Add(new LapRecord(
                Lap: lap,
                TimeSeconds: lastLap,
                Sector1: history.Sector1,
                Sector2: history.Sector2,
                Sector3: history.Sector3,
                Position: history.Position,
                Compound: history.Compound,
                TyreAge: history.TyreAge,
                InPit: history.InPit,
                PitOut: history.PitOut,
                TrackStatus: _trackStatus));

            history.LastRecordedLap = lap;
            history.Sector1 = history.Sector2 = history.Sector3 = null;
        }
    }

    /// <summary>
    /// The authoritative compound. TimingAppData leaves Compound null for the
    /// STARTING stint — it publishes the age but not what the tyre is — which
    /// silently drops the opening stint of every strategy. CurrentTyres carries
    /// it, and is published before the race even starts.
    /// </summary>
    private void ObserveCurrentTyres(StateAccumulator state)
    {
        if (state[Topics.CurrentTyres]?["Tyres"] is not JsonObject tyres) return;

        foreach (var (number, raw) in tyres)
        {
            if (raw is not JsonObject tyre) continue;

            var history = History(number);

            if ((string?)tyre["Compound"] is { Length: > 0 } compound)
            {
                history.Compound = compound;
            }

            // Here New is a real boolean; in TimingAppData it is the STRING
            // "true". Both shapes are accepted rather than trusted blindly.
            history.NewTyres = tyre["New"] switch
            {
                JsonValue v when v.TryGetValue<bool>(out var b) => b,
                JsonValue v when v.TryGetValue<string>(out var s2) => s2 == "true",
                _ => history.NewTyres,
            };
        }
    }

    private void ObserveStints(StateAccumulator state)
    {
        if (state[Topics.TimingAppData]?["Lines"] is not JsonObject lines) return;

        foreach (var (number, raw) in lines)
        {
            if (raw is not JsonObject line || line["Stints"] is not JsonObject stints) continue;

            var history = History(number);

            // The newest stint is the current tyre. The feed frequently omits
            // Compound on a stint it already announced, so the walk back finds
            // the last one that names it — the same problem the timing tower
            // hits (see web selectors).
            var ordered = stints
                .Select(kv => (Index: int.TryParse(kv.Key, out var i) ? i : -1, Value: kv.Value as JsonObject))
                .Where(x => x.Index >= 0 && x.Value is not null)
                .OrderBy(x => x.Index)
                .ToList();

            if (ordered.Count == 0) continue;

            var current = ordered[^1];
            history.StintIndex = current.Index;
            history.TyreAge = ReadInt(current.Value!["TotalLaps"]) ?? history.TyreAge;

            // Only fills a gap: CurrentTyres is authoritative and must not be
            // overwritten by a stint entry that omits the compound.
            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                if ((string?)ordered[i].Value!["Compound"] is { Length: > 0 } compound)
                {
                    history.Compound ??= compound;
                    break;
                }
            }
        }
    }

    /// <summary>Builds the finished analysis. Safe to call at any point.</summary>
    public SessionAnalysis Build(StateAccumulator state, AnalysisMeta meta)
    {
        var driverList = state[Topics.DriverList] as JsonObject;
        var drivers = new List<DriverAnalysis>();

        foreach (var (number, history) in _drivers)
        {
            var info = driverList?[number] as JsonObject;

            drivers.Add(new DriverAnalysis(
                RacingNumber: number,
                Tla: (string?)info?["Tla"] ?? number,
                TeamName: (string?)info?["TeamName"] ?? "",
                TeamColour: (string?)info?["TeamColour"] ?? "",
                Laps: history.Laps,
                Stints: DeriveStints(history.Laps)));
        }

        drivers.Sort((a, b) =>
        {
            var pa = a.Laps.Count > 0 ? a.Laps[^1].Position ?? int.MaxValue : int.MaxValue;
            var pb = b.Laps.Count > 0 ? b.Laps[^1].Position ?? int.MaxValue : int.MaxValue;
            return pa.CompareTo(pb);
        });

        return new SessionAnalysis(meta with { TotalLaps = TotalLaps }, drivers);
    }

    /// <summary>
    /// Stints are derived from the per-lap compound and tyre age rather than
    /// taken from the feed's own stint list, so they cannot contradict the lap
    /// table they are drawn beside. A new stint starts when the compound
    /// changes or the age stops increasing.
    /// </summary>
    private static List<StintRecord> DeriveStints(List<LapRecord> laps)
    {
        var stints = new List<StintRecord>();
        if (laps.Count == 0) return stints;

        var startIndex = 0;

        for (var i = 1; i <= laps.Count; i++)
        {
            var boundary = i == laps.Count;

            if (!boundary)
            {
                var previous = laps[i - 1];
                var current = laps[i];
                // A boundary is a compound change, or the tyre age going DOWN
                // (a fresh set). Age merely repeating is a stale update — using
                // `<=` split a single stint into three, which is how PIA's
                // one-stop first read as "HARD -> HARD -> HARD".
                boundary = current.Compound != previous.Compound
                    || (current.TyreAge is { } age && previous.TyreAge is { } before && age < before)
                    || previous.InPit;
            }

            if (!boundary) continue;

            var first = laps[startIndex];
            var last = laps[i - 1];

            if (first.Compound is { Length: > 0 } compound)
            {
                stints.Add(new StintRecord(
                    Index: stints.Count,
                    Compound: compound,
                    StartLap: first.Lap,
                    EndLap: last.Lap,
                    Laps: last.Lap - first.Lap + 1,
                    NewTyres: first.TyreAge is null or <= 1));
            }

            startIndex = i;
        }

        return stints;
    }

    private DriverHistory History(string number)
    {
        if (_drivers.TryGetValue(number, out var existing)) return existing;

        var created = new DriverHistory();
        _drivers[number] = created;
        return created;
    }

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var i)) return i;
        return value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) ? parsed : null;
    }

    private sealed class DriverHistory
    {
        public List<LapRecord> Laps { get; } = [];
        public int LastRecordedLap { get; set; }
        public int? Position { get; set; }
        public double? Sector1 { get; set; }
        public double? Sector2 { get; set; }
        public double? Sector3 { get; set; }
        public string? Compound { get; set; }
        public int? TyreAge { get; set; }
        public bool NewTyres { get; set; }
        public int StintIndex { get; set; }
        public bool InPit { get; set; }
        public bool PitOut { get; set; }
    }
}
