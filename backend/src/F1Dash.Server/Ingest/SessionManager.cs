using System.Text;
using F1Dash.Core;
using F1Dash.Core.Analysis;
using F1Dash.Core.Archive;
using F1Dash.Server.Realtime;
using F1Dash.Server.Storage;

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
    RelaySessionSource relay,
    StorageIndexer indexer,
    ILoggerFactory loggers) : IHostedService
{
    private readonly ILogger _logger = loggers.CreateLogger<SessionManager>();
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    private CancellationTokenSource? _running;
    private Task? _task;

    private ReplayController? _controller;
    private SessionRequest? _lastReplay;
    private AnalysisBuilder? _analysis;
    private TelemetryRecorder? _telemetry;
    private AnalysisStore? _store;
    private string? _sessionDirectory;
    private FailoverSessionSource? _failover;

    public CurrentSession Current { get; private set; } = new(SessionMode.None, "idle");

    /// <summary>The analysis for whatever is currently playing, built so far.</summary>
    public SessionAnalysis? Analysis =>
        _analysis is null ? null : _analysis.Build(state.Accumulator, CurrentMeta());

    public AnalysisStore? Store => _store;

    /// <summary>Transport state, when a replay is running.</summary>
    public ReplayController? Controller => _controller;

    /// <summary>
    /// Which live source is actually delivering data, once one is.
    ///
    /// Surfaced because the two are not equivalent: polling is a second or
    /// three behind the socket, and a dashboard silently running behind is
    /// exactly the kind of staleness a user cannot detect.
    /// </summary>
    public string? LiveSource =>
        _failover?.Active ?? (Current.Mode == SessionMode.Live ? relay.Description : null);

    /// <summary>Total length of the loaded stream, so the scrub bar has a scale.</summary>
    public long DurationMs { get; private set; }
    public bool TelemetryEnabled => _telemetry?.Enabled ?? false;

    public async Task StartAsync(CancellationToken ct)
    {
        // Start from the environment so a plain `docker compose up` plays
        // something without anyone having to call the API.
        var mode =
            Environment.GetEnvironmentVariable("F1_RELAY") == "1" ? "relay"
            : Environment.GetEnvironmentVariable("F1_LIVE") == "1" ? "live"
            : "replay";
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
        else if (mode is "live" or "relay")
        {
            await SwitchAsync(new SessionRequest(mode), ct).ConfigureAwait(false);
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

            if (request.Mode == "relay")
            {
                // The collector is already connected, or will be. This source
                // simply waits for it rather than reaching out to F1 itself.
                _sessionDirectory = null;
                _controller = null;
                _lastReplay = null;
                _failover = null;

                Start(relay, new CurrentSession(SessionMode.Live, relay.Description));
                return Current;
            }

            if (request.Mode == "live")
            {
                _sessionDirectory = null;
                _controller = null;
                _lastReplay = null;
                var proxy = Environment.GetEnvironmentVariable("F1_HTTP_PROXY");

                // Ordered cheapest-first. The socket is lowest latency; polling
                // the static archive is 1-3 s behind but reads from a CDN, so it
                // survives the origin refusing the server's address entirely.
                var source = new FailoverSessionSource(
                    [
                        new LiveSignalrSource(proxy, loggers.CreateLogger<LiveSignalrSource>()),
                        new StaticPollingSource(
                            ArchiveClient.CreateHttpClient(proxy),
                            loggers.CreateLogger<StaticPollingSource>()),
                    ],
                    loggers.CreateLogger<FailoverSessionSource>());

                _failover = source;
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

            _lastReplay = request;
            _failover = null;

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
        // Scanning the file for its last offset is what gives the scrub bar a
        // scale. It is a linear read of a large file, so it happens once per
        // switch rather than per request.
        DurationMs = LastOffset(path);

        _controller = new ReplayController { Speed = speed, DurationMs = DurationMs };
        _controller.SetPosition(startMs);

        var source = new ReplaySessionSource(path, speed, startMs, loop, _controller);
        _sessionDirectory = Path.GetDirectoryName(path);
        Start(source, description with { Speed = speed, Loop = loop });
        return Task.CompletedTask;
    }

    private static long LastOffset(string path)
    {
        long last = 0;
        foreach (var entry in SessionDownloader.ReadJsonl(path)) last = entry.OffsetMs;
        return last;
    }

    /// <summary>
    /// Moves the replay to a new position.
    ///
    /// A seek is a restart at the new offset, not a jump: state is
    /// delta-accumulated, so there is no way to read the state at time t without
    /// replaying to it. Restarting reuses the rebuild path — entries before the
    /// offset are applied at full speed — which is already the code that makes
    /// REPLAY_START_MS correct.
    /// </summary>
    public async Task<CurrentSession> SeekAsync(long offsetMs, CancellationToken ct)
    {
        if (_lastReplay is null) return Current with { Error = "Nothing is being replayed." };

        // The transport settings are the viewer's, not the original request's.
        // Seeking at 8x and landing back at 1x would make the speed control feel
        // like it silently reset itself.
        var speed = _controller?.Speed ?? _lastReplay.Speed;
        var wasPlaying = _controller?.Playing ?? true;

        var result = await SwitchAsync(
            _lastReplay with { StartMs = Math.Max(0, offsetMs), Speed = speed }, ct).ConfigureAwait(false);

        if (!wasPlaying) _controller?.Pause();
        return result;
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

    /// <summary>
    /// Pushes the finished session into every configured index.
    ///
    /// Best-effort and off the live path: a database being down delays indexing
    /// and costs a re-run of the backfill, nothing more.
    /// </summary>
    private async Task IndexCurrentSessionAsync()
    {
        if (_analysis is null || _sessionDirectory is null) return;
        if (indexer.AvailableStores.Count == 0) return;

        try
        {
            var meta = CurrentMeta();
            var built = _analysis.Build(state.Accumulator, meta);

            await indexer.IndexSessionAsync(
                SessionKey.From(meta), built, _sessionDirectory, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not index the finished session");
        }
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

                // Indexing happens AFTER the analysis is on disk, never before.
                // The archive is the source of truth; an index written from
                // memory could outlive a save that failed.
                await IndexCurrentSessionAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Filesystem-safe, and stable enough to match a user's input.
    ///
    /// Diacritics are folded. F1 publishes "São Paulo Grand Prix" with the
    /// accent, and <c>char.IsLetterOrDigit</c> happily keeps 'ã' — so a user
    /// typing "Sao Paulo", which is the natural thing to type, produced a
    /// different slug and matched nothing.
    ///
    /// The fold is an explicit table rather than Unicode normalisation because
    /// this project builds with <c>InvariantGlobalization</c>, where
    /// <c>string.Normalize</c> is a no-op and silently returns the accented
    /// character unchanged. That was not a guess: the first attempt used
    /// NFD and the test proved it did nothing.
    ///
    /// Note for an existing deployment: an archive directory already named with
    /// an accent will no longer be found and the session is downloaded once
    /// more. A one-off cost, and preferable to a name a user cannot type.
    /// </summary>
    public static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            var folded = Fold(c);
            builder.Append(char.IsLetterOrDigit(folded) ? char.ToLowerInvariant(folded) : '-');
        }

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug;
    }

    /// <summary>
    /// Latin-1 and Latin Extended-A letters folded to ASCII.
    ///
    /// Covers every accented character that has appeared in an F1 meeting or
    /// circuit name — São Paulo, Nürburgring, México, Türkiye — and anything
    /// outside the table falls through unchanged, becoming a hyphen. A name
    /// made entirely of unfoldable characters would slug to nothing, which is
    /// the same outcome as today and has never occurred.
    /// </summary>
    private static char Fold(char c) => c switch
    {
        >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' => c,
        'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' or 'ā' or 'ă' or 'ą' => 'a',
        'À' or 'Á' or 'Â' or 'Ã' or 'Ä' or 'Å' or 'Ā' or 'Ă' or 'Ą' => 'A',
        'è' or 'é' or 'ê' or 'ë' or 'ē' or 'ĕ' or 'ė' or 'ę' or 'ě' => 'e',
        'È' or 'É' or 'Ê' or 'Ë' or 'Ē' or 'Ĕ' or 'Ė' or 'Ę' or 'Ě' => 'E',
        'ì' or 'í' or 'î' or 'ï' or 'ī' or 'į' => 'i',
        'Ì' or 'Í' or 'Î' or 'Ï' or 'Ī' or 'Į' => 'I',
        'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' or 'ō' or 'ő' => 'o',
        'Ò' or 'Ó' or 'Ô' or 'Õ' or 'Ö' or 'Ø' or 'Ō' or 'Ő' => 'O',
        'ù' or 'ú' or 'û' or 'ü' or 'ū' or 'ů' or 'ű' => 'u',
        'Ù' or 'Ú' or 'Û' or 'Ü' or 'Ū' or 'Ů' or 'Ű' => 'U',
        'ç' or 'ć' or 'č' => 'c',
        'Ç' or 'Ć' or 'Č' => 'C',
        'ñ' or 'ń' or 'ň' => 'n',
        'Ñ' or 'Ń' or 'Ň' => 'N',
        'ś' or 'š' => 's',
        'Ś' or 'Š' => 'S',
        'ý' or 'ÿ' => 'y',
        'Ý' => 'Y',
        'ź' or 'ż' or 'ž' => 'z',
        'Ź' or 'Ż' or 'Ž' => 'Z',
        'ğ' => 'g',
        'Ğ' => 'G',
        'ı' => 'i',
        'İ' => 'I',
        'ł' => 'l',
        'Ł' => 'L',
        'ř' => 'r',
        'Ř' => 'R',
        'ť' => 't',
        'Ť' => 'T',
        'đ' => 'd',
        'Đ' => 'D',
        _ => c,
    };
}
