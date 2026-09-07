namespace F1Dash.Core;

/// <summary>
/// The topics we subscribe to, exactly as named by the feed (docs/03 §1).
/// The two ".z" topics are zlib-compressed; everything else is plain JSON.
/// </summary>
public static class Topics
{
    public const string TimingData = "TimingData";
    public const string TimingAppData = "TimingAppData";
    public const string TimingStats = "TimingStats";
    public const string DriverList = "DriverList";
    public const string RaceControlMessages = "RaceControlMessages";
    public const string TrackStatus = "TrackStatus";
    public const string WeatherData = "WeatherData";
    public const string LapCount = "LapCount";
    public const string ExtrapolatedClock = "ExtrapolatedClock";
    public const string SessionInfo = "SessionInfo";
    public const string SessionStatus = "SessionStatus";
    public const string SessionData = "SessionData";
    public const string TopThree = "TopThree";
    public const string TeamRadio = "TeamRadio";
    public const string PitLaneTimeCollection = "PitLaneTimeCollection";
    public const string Position = "Position";
    public const string CarData = "CarData";
    public const string Heartbeat = "Heartbeat";

    // --- new in 2026 (docs/21) -------------------------------------------
    /// <summary>Per-driver overtake counter. Fills the column DRS vacated.</summary>
    public const string OvertakeSeries = "OvertakeSeries";
    /// <summary>Stationary and pit-lane time per stop; not published before 2026.</summary>
    public const string PitStop = "PitStop";

    /// <summary>The exact Subscribe argument list sent to the Streaming hub.</summary>
    public static readonly string[] Subscription =
    [
        TimingData, TimingAppData, TimingStats, DriverList,
        RaceControlMessages, TrackStatus, WeatherData, LapCount,
        ExtrapolatedClock, SessionInfo, SessionStatus, SessionData,
        TopThree, TeamRadio, PitLaneTimeCollection,
        // Absent before 2026; the archive simply does not list them for older
        // seasons and the downloader skips what a session did not publish.
        OvertakeSeries, PitStop,
        "Position.z", "CarData.z",
    ];

    /// <summary>
    /// Strips the ".z" suffix. Internally the topics are Position and CarData;
    /// the suffix is a transport detail, not part of the name.
    /// </summary>
    public static string Normalise(string topic) =>
        topic.EndsWith(".z", StringComparison.Ordinal) ? topic[..^2] : topic;

    public static bool IsCompressed(string topic) =>
        topic.EndsWith(".z", StringComparison.Ordinal);
}

/// <summary>
/// CarData channel numbers are the field names. The numbers carry no meaning to
/// a reader, so they are mapped once, here (docs/04 §5).
/// </summary>
public static class CarDataChannel
{
    public const string Rpm = "0";
    public const string Speed = "2";
    public const string Gear = "3";
    public const string Throttle = "4";
    public const string Brake = "5";
    public const string Drs = "45";

    /// <summary>
    /// DRS values: 0 and 1 are off, 8 means eligible/detected, 10, 12 and 14
    /// mean the flap is open. Anything at or above 10 counts as active.
    /// </summary>
    public const int DrsActiveThreshold = 10;
}

/// <summary>TrackStatus.Status codes (docs/03 §1).</summary>
public static class TrackStatusCode
{
    public const string Clear = "1";
    public const string Yellow = "2";
    public const string SafetyCar = "4";
    public const string Red = "5";
    public const string VirtualSafetyCar = "6";
    public const string VscEnding = "7";
}
