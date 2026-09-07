namespace F1Dash.Server.Ingest;

/// <summary>
/// Transport state for a replay: playing, speed and where it has reached.
///
/// Shared between the API and the running source, which polls it each entry.
/// Play, pause and speed take effect immediately because they only change how
/// the source waits.
///
/// Seek is NOT here. State is delta-accumulated, so moving the position means
/// rebuilding from the start — there is no way to read the state at time t
/// without replaying to it (docs/08). SessionManager handles a seek by
/// restarting the source at the new offset, which reuses the rebuild path that
/// already exists and is tested.
/// </summary>
public sealed class ReplayController
{
    private long _positionMs;
    private double _speed = 1.0;
    private int _playing = 1;

    /// <summary>Session time reached, in milliseconds.</summary>
    public long PositionMs => Interlocked.Read(ref _positionMs);

    /// <summary>Total session length, once known.</summary>
    public long DurationMs { get; set; }

    public bool Playing => Volatile.Read(ref _playing) == 1;

    public double Speed
    {
        get => Volatile.Read(ref _speed);
        // Clamped: zero would stall the source in a tight loop rather than
        // pausing it, and beyond 60x the feed outruns any client.
        set => Volatile.Write(ref _speed, Math.Clamp(value, 0.25, 60));
    }

    public void Play() => Volatile.Write(ref _playing, 1);
    public void Pause() => Volatile.Write(ref _playing, 0);
    public void SetPosition(long ms) => Interlocked.Exchange(ref _positionMs, ms);
}
