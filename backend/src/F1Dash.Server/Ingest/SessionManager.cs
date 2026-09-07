using F1Dash.Core;
using F1Dash.Core.Analysis;
using F1Dash.Core.Archive;
using F1Dash.Server.Realtime;

namespace F1Dash.Server.Ingest;

public enum SessionMode { None, Replay, Live }

/// <summary>What the server is currently playing.</summary>
public sealed record CurrentSession(
    SessionMode Mode,
    string Description,
    int? Year = null,
    string? Meeting = null,
    string? Session = null,
    double Speed = 1.0,
    bool Loop = false,
    string? Error = null);

/// <summary>A request to switch what is playing.</summary>
public sealed record SessionRequest(
    string Mode,
    int? Year = null,
    string? Meeting = null,
    string? Session = null,
    double Speed = 1.0,
    long StartMs = 0,
    bool Loop = true);

/// <summary>
/// Owns whichever source is running and swaps it on request, so replay and live
/// are the same feature rather than two deployments.
///
/// Switching is a full reset: the accumulated state is delta-derived, so
/// carrying it across a switch would leave one session's drivers merged into
/// another's. Connected clients receive a fresh snapshot on the next frame.
/// </summary>
public sealed class SessionManager(
    LiveSessionState state,
    ArchiveClient archive,
    string archiveRoot,
    ILoggerFactory loggers) : IHostedService
{
    private readonly ILogger _logger = loggers.CreateLogger<SessionManager>();
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    private CancellationTokenSource? _running;
    private Task? _task;

    private AnalysisBuilder? _analysis;
    private TelemetryRecorder? _telemetry;
    private AnalysisStore? _store;
    private string? _sessionDirectory;

    public CurrentSession Current { get; private set; } = new(SessionMode.None, "idle");

    /// <summary>The analysis for whatever is currently playing, built so far.</summary>
    public SessionAnalysis? Analysis =>
        _analysis is null ? null : _analysis.Build(state.Accumulator, CurrentMeta());

    public AnalysisStore? Store => _store;
    public bool TelemetryEnabled => _telemetry?.Enabled ?? false;

    public async Task StartAsync(CancellationToken ct)
    {
        // Start from the environment so a plain `docker compose up` plays
        // something without anyone having to call the API.
        var mode = Environment.GetEnvironmentVariable("F1_LIVE") == "1" ? "live" : "replay";
        var stream = Environment.GetEnvironmentVariable("REPLAY_STREAM");

        if (mode == "replay" && !string.IsNullOrWhiteSpace(stream) && File.Exists(stream))
        {
            await SwitchToStreamAsync(
                stream,
                new CurrentSession(SessionMode.Replay, Path.GetFileName(Path.GetDirectoryName(stream)) ?? "replay"),
                double.TryParse(Environment.GetEnvironmentVariable("REPLAY_SPEED"), out var s) ? s : 1.0,
                long.TryParse(Environment.GetEnvironmentVariable("REPLAY_START_MS"), out var st) ? st : 0,
                Environment.GetEnvironmentVariable("REPLAY_LOOP") != "0",
                ct).ConfigureAwait(false);
        }
        else if (mode == "live")
        {
            await SwitchAsync(new SessionRequest("live"), ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning("No session source configured. Use POST /api/session to choose one.");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await StopCurrentAsync().ConfigureAwait(false);
    }

    public async Task<CurrentSession> SwitchAsync(SessionRequest request, CancellationToken ct)
    {
        await _switchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopCurrentAsync().ConfigureAwait(false);

            if (request.Mode == "live")
            {
                _sessionDirectory = null;
                var source = new LiveSignalrSource(
                    Environment.GetEnvironmentVariable("F1_HTTP_PROXY"),
                    loggers.CreateLogger<LiveSignalrSource>());

                Start(source, new CurrentSession(SessionMode.Live, source.Description));
                return Current;
            }

            if (request.Year is not { } year || string.IsNullOrWhiteSpace(request.Meeting)
                || string.IsNullOrWhiteSpace(request.Session))
            {
                Current = new CurrentSession(SessionMode.None, "idle",
                    Error: "A replay needs year, meeting and session.");
                return Current;
            }

            var path = StreamPath(year, request.Meeting, request.Session);

            if (!File.Exists(path))
            {
                _logger.LogInformation("Downloading {Year} {Meeting} {Session}", year, request.Meeting, request.Session);
                var downloaded = await DownloadAsync(year, request.Meeting, request.Session, ct).ConfigureAwait(false);
                if (downloaded is null)
                {
                    Current = new CurrentSession(SessionMode.None, "idle",
                        Error: $"No archived session matched {year} {request.Meeting} {request.Session}.");
                    return Current;
                }
                path = downloaded;
            }

            await SwitchToStreamAsync(
                path,
                new CurrentSession(SessionMode.Replay, $"{request.Meeting} {request.Session} {year}",
                    year, request.Meeting, request.Session, request.Speed, request.Loop),
                request.Speed, request.StartMs, request.Loop, ct).ConfigureAwait(false);

            return Current;
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private Task SwitchToStreamAsync(
        string path, CurrentSession description, double speed, long startMs, bool loop, CancellationToken ct)
    {
        var source = new ReplaySessionSource(path, speed, startMs, loop);
        _sessionDirectory = Path.GetDirectoryName(path);
        Start(source, description with { Speed = speed, Loop = loop });
        return Task.CompletedTask;
    }

    private AnalysisMeta CurrentMeta()
    {
        var info = state.Accumulator[Topics.SessionInfo];
        var meeting = info?["Meeting"];
        var start = (string?)info?["StartDate"];

        return new AnalysisMeta(
            Year: Current.Year ?? (int.TryParse(start?[..Math.Min(4, start.Length)], out var y) ? y : null),
            Meeting: (string?)meeting?["Name"] ?? Current.Meeting ?? "",
            SessionName: (string?)info?["Name"] ?? Current.Session ?? "",
            SessionType: (string?)info?["Type"] ?? "",
            Circuit: (string?)meeting?["Circuit"]?["ShortName"] ?? "",
            CircuitKey: (int?)meeting?["Circuit"]?["Key"],
            StartDate: start,
            TotalLaps: 0,
            HasTelemetry: _telemetry?.Enabled ?? false,
            RecordedAtUtc: DateTime.UtcNow.ToString("O"));
    }

    /// <summary>Writes the analysis for the running session to disk.</summary>
    public async Task<bool> SaveAnalysisAsync(CancellationToken ct)
    {
        if (_analysis is null || _store is null) return false;

        await _store.SaveAsync(_analysis.Build(state.Accumulator, CurrentMeta()), ct).ConfigureAwait(false);
        return true;
    }

    private void Start(ISessionSource source, CurrentSession description)
    {
        // A switch discards the previous session's state entirely. State is
        // delta-accumulated, so keeping it would merge one session's drivers
        // into another's.
        state.Reset();

        _telemetry?.Dispose();
        _analysis = new AnalysisBuilder();

        // Analysis is recorded for replay as well as live. A path exercised only
        // on the ~24 live weekends a year is a path that breaks on race day.
        var directory = _sessionDirectory ?? Path.Combine(archiveRoot, "live", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        _store = new AnalysisStore(directory);
        _telemetry = new TelemetryRecorder(_store.TelemetryDirectory, description.Year);

        _running = new CancellationTokenSource();
        Current = description;

        var token = _running.Token;
        _task = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Session source starting: {Source}", source.Description);
                var analysis = _analysis;
                var telemetry = _telemetry;

                await foreach (var update in source.ReadAsync(token).ConfigureAwait(false))
                {
                    state.Apply(update);

                    // Both read the accumulator directly rather than a snapshot
                    // clone: cloning 1-2 MB per delta would cost more than
                    // everything else in the ingest path combined.
                    analysis?.Observe(state.Accumulator, update.Topic);
                    telemetry?.Observe(state.Accumulator, update.Topic);
                }

                _logger.LogInformation("Session source completed");

                // The session ended on its own, so the analysis is final.
                await SaveAnalysisAsync(CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("Analysis saved to {Directory}", _store?.Directory);
            }
            catch (OperationCanceledException)
            {
                // A deliberate switch.
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Session source failed");
                Current = Current with { Error = e.Message };
            }
        }, CancellationToken.None);
    }

    private async Task StopCurrentAsync()
    {
        if (_running is null) return;

        await _running.CancelAsync().ConfigureAwait(false);
        if (_task is not null)
        {
            try { await _task.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        _running.Dispose();
        _running = null;
        _task = null;
    }

    private string StreamPath(int year, string meeting, string session) =>
        Path.Combine(archiveRoot, year.ToString(), Slug(meeting), Slug(session), "stream.jsonl");

    private async Task<string?> DownloadAsync(int year, string meeting, string session, CancellationToken ct)
    {
        var sessions = await archive.ListSessionsAsync(year, ct).ConfigureAwait(false);

        var match = sessions.FirstOrDefault(s =>
            Slug(s.MeetingName) == Slug(meeting) && Slug(s.SessionName) == Slug(session));

        if (match is null) return null;

        var entries = await new SessionDownloader(archive)
            .DownloadAsync(match.Path, null, includeTelemetry: true, ct).ConfigureAwait(false);

        var path = StreamPath(year, match.MeetingName, match.SessionName);
        await SessionDownloader.WriteJsonlAsync(entries, path, ct).ConfigureAwait(false);
        return path;
    }

    /// <summary>Filesystem-safe, and stable enough to match a user's input.</summary>
    public static string Slug(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        var slug = new string([.. chars]).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug;
    }
}
