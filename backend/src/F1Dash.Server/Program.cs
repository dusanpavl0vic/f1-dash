using F1Dash.Core.Projections;
using F1Dash.Core.Analysis;
using F1Dash.Core.Archive;
using F1Dash.Core.Track;
using F1Dash.Server.Catalog;
using F1Dash.Server.Cli;
using F1Dash.Server.Ingest;
using F1Dash.Server.Realtime;

// The backend is one application. It runs as a web host by default and exposes
// operational subcommands for work that happens outside a request — downloading
// archived sessions being the one the whole project depends on, because F1 runs
// on roughly 24 weekends a year and everything else is developed against those
// files.

const string Usage = """
    Usage:
      (no args)                                    run the server
      list <year>                                  list archived sessions for a season
      archive <year> <meeting> <session> [--no-telemetry]
                                                   download one session to ARCHIVE_PATH

    Examples:
      list 2024
      archive 2024 Italian Race
    """;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var archiveRoot = Environment.GetEnvironmentVariable("ARCHIVE_PATH") ?? "./data/archive";

if (args.Length > 0)
{
    if (args[0] == "list" && args.Length >= 2 && int.TryParse(args[1], out var listYear))
    {
        return await ArchiveCommand.ListAsync(listYear, cts.Token);
    }

    if (args[0] == "archive" && args.Length >= 4 && int.TryParse(args[1], out var year))
    {
        return await ArchiveCommand.DownloadAsync(
            year, args[2], args[3], archiveRoot,
            includeTelemetry: !args.Contains("--no-telemetry"),
            cts.Token);
    }

    Console.Error.WriteLine(args[0] is "list" or "archive"
        ? Usage
        : $"Unknown command '{args[0]}'.\n\n{Usage}");
    return 1;
}

var builder = WebApplication.CreateBuilder();

builder.Services.AddSingleton<LiveSessionState>();

// The listening half of the relay. Always registered, even when unused: it is
// an empty channel until a collector connects, and having it present means
// switching to relay mode needs no restart.
builder.Services.AddSingleton(sp => new RelaySessionSource(
    sp.GetRequiredService<ILogger<RelaySessionSource>>()));

builder.Services.AddSingleton<RelayCollector>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RelayCollector>());

builder.Services.AddSingleton(_ => new TrackService(
    TrackService.CreateHttpClient(),
    Path.Combine(archiveRoot, "track-cache")));

// The SPA is served from a different origin in development.
var origins = (Environment.GetEnvironmentVariable("ORIGIN") ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

// REPLAY_STREAM points at a recorded session. It is the development default and
// mirrors f1-dash's own F1_DEV_URL: the whole stack must run out of season.
// The live SignalR source lands in IMPL-34 and slots in behind this same
// interface.
var replayStream = Environment.GetEnvironmentVariable("REPLAY_STREAM")
    ?? Path.Combine(archiveRoot, "2024", "italian-grand-prix", "race", "stream.jsonl");

builder.Services.AddSingleton(_ => new ArchiveClient(ArchiveClient.CreateHttpClient(
    Environment.GetEnvironmentVariable("F1_HTTP_PROXY"))));

builder.Services.AddSingleton(sp => new SessionCatalog(
    sp.GetRequiredService<ArchiveClient>(), archiveRoot));

builder.Services.AddSingleton(sp => new AnalysisPrecomputer(
    sp.GetRequiredService<ArchiveClient>(), archiveRoot,
    sp.GetRequiredService<ILogger<AnalysisPrecomputer>>()));

builder.Services.AddSingleton(sp => new ScheduleService(
    ArchiveClient.CreateHttpClient(), sp.GetRequiredService<ILogger<ScheduleService>>()));

builder.Services.AddSingleton(sp => new DriverDirectory(
    sp.GetRequiredService<ArchiveClient>(), sp.GetRequiredService<ILogger<DriverDirectory>>()));

builder.Services.AddSingleton(sp => new ResultsService(
    ArchiveClient.CreateHttpClient(), sp.GetRequiredService<ILogger<ResultsService>>()));

builder.Services.AddSingleton(sp => new StandingsService(
    ArchiveClient.CreateHttpClient(), sp.GetRequiredService<ILogger<StandingsService>>()));

builder.Services.AddSingleton(sp => new DriverSeasonService(
    ArchiveClient.CreateHttpClient(), sp.GetRequiredService<ILogger<DriverSeasonService>>()));

// Replay and live are the same feature behind one manager, switchable at
// runtime, so the dashboard can move between a 2018 race and a session running
// right now without a restart.
builder.Services.AddSingleton(sp => new SessionManager(
    sp.GetRequiredService<LiveSessionState>(),
    sp.GetRequiredService<ArchiveClient>(),
    archiveRoot,
    sp.GetRequiredService<RelaySessionSource>(),
    sp.GetRequiredService<ILoggerFactory>()));

builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionManager>());

var app = builder.Build();
app.UseWebSockets();
app.UseCors();

var live = app.Services.GetRequiredService<LiveSessionState>();

app.MapGet("/health", (SessionManager sessions) => Results.Ok(new
{
    ok = true,
    live = live.HasData,
    clients = live.ClientCount,
    sequence = live.Sequence,
    mode = sessions.Current.Mode.ToString(),
    session = sessions.Current.Description,
}));

// A plain-JSON view of the current state, for debugging without a WebSocket.
app.MapGet("/state", () => Results.Text(live.Snapshot().ToJsonString(), "application/json"));

app.MapGet("/classification", () => Results.Ok(Classification.From(live.Snapshot())));

// Circuit geometry, already rotated, Y-flipped and turned into an SVG path, so
// the client renders it with no maths of its own. Immutable: a circuit's shape
// does not change mid-season.
// --- session catalog and switching -----------------------------------------

app.MapGet("/api/seasons", () => Results.Ok(SessionCatalog.Seasons()));

app.MapGet("/api/seasons/{year:int}/sessions",
    async (int year, SessionCatalog catalog, CancellationToken ct) =>
        Results.Ok(await catalog.SessionsAsync(year, ct)));

app.MapGet("/api/session/live",
    async (SessionCatalog catalog, CancellationToken ct) =>
        Results.Ok(await catalog.LiveAsync(ct)));

app.MapGet("/api/session", (SessionManager sessions) => Results.Ok(new
{
    sessions.Current.Mode,
    sessions.Current.Description,
    sessions.Current.Year,
    sessions.Current.Meeting,
    sessions.Current.Session,
    sessions.Current.Loop,
    sessions.Current.Error,
    // Transport state, so a scrub bar can render without a second request.
    positionMs = sessions.Controller?.PositionMs ?? 0,
    durationMs = sessions.DurationMs,
    playing = sessions.Controller?.Playing ?? false,
    // The controller's speed, not the request's: it changes at runtime, and
    // exposing both under camelCase collides on "speed".
    speed = sessions.Controller?.Speed ?? sessions.Current.Speed,
    // Which live source won, so the UI can say when it is a second or three
    // behind rather than letting the user assume it is not.
    liveSource = sessions.LiveSource,
}));

// Transport controls. Play, pause and speed are immediate; seek restarts the
// source at the new offset, because state is delta-accumulated and cannot be
// read at an arbitrary point without replaying to it.
app.MapPost("/api/session/control",
    async (ControlRequest request, SessionManager sessions, CancellationToken ct) =>
    {
        var controller = sessions.Controller;

        switch (request.Action)
        {
            case "play":  controller?.Play();  break;
            case "pause": controller?.Pause(); break;

            case "speed" when request.Value is { } speed:
                if (controller is null) return Results.BadRequest(new { error = "Nothing is being replayed." });
                controller.Speed = speed;
                break;

            case "seek" when request.Value is { } target:
                var seeked = await sessions.SeekAsync((long)target, ct);
                if (seeked.Error is not null) return Results.BadRequest(seeked);
                // Falls through to the shared transport response below: one
                // response shape for every action means the client does not have
                // to special-case which button it pressed.
                controller = sessions.Controller;
                break;

            default:
                return Results.BadRequest(new { error = $"Unknown action '{request.Action}'." });
        }

        return Results.Ok(new
        {
            playing = controller?.Playing ?? false,
            speed = controller?.Speed ?? 1,
            positionMs = controller?.PositionMs ?? 0,
            durationMs = sessions.DurationMs,
        });
    });

// Switching downloads the session first if it is not already on disk, so a
// first request for an old race can take a while. It is deliberately
// synchronous: a progress stream is IMPL-42.
app.MapPost("/api/session",
    async (SessionRequest request, SessionManager sessions, CancellationToken ct) =>
    {
        var current = await sessions.SwitchAsync(request, ct);
        return current.Error is null ? Results.Ok(current) : Results.BadRequest(current);
    });

// --- session analysis -------------------------------------------------------

// The whole analysis for whatever is playing: per-lap times, sectors,
// positions, compounds and derived stints.
app.MapGet("/api/analysis", (SessionManager sessions, HttpContext http) =>
{
    var analysis = sessions.Analysis;
    if (analysis is null) return Results.NotFound(new { error = "No session is running." });

    // A live session's analysis grows continuously, so it must not be cached.
    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(analysis);
});

// Strategy alone — two orders of magnitude smaller than the lap table.
app.MapGet("/api/analysis/stints", (SessionManager sessions, HttpContext http) =>
{
    var analysis = sessions.Analysis;
    if (analysis is null) return Results.NotFound(new { error = "No session is running." });

    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(analysis.Drivers.Select(d => new
    {
        d.RacingNumber, d.Tla, d.TeamName, d.TeamColour, d.Stints,
    }));
});

// Who is faster in which sector, over each driver's BEST sector rather than the
// sectors of one lap — a driver's quickest S1 and S3 often come from different
// laps, and the question is where each is quicker.
app.MapGet("/api/analysis/compare", (string a, string b, SessionManager sessions, HttpContext http) =>
{
    var analysis = sessions.Analysis;
    if (analysis is null) return Results.NotFound(new { error = "No session is running." });

    var driverA = analysis.Drivers.FirstOrDefault(d => d.Tla.Equals(a, StringComparison.OrdinalIgnoreCase));
    var driverB = analysis.Drivers.FirstOrDefault(d => d.Tla.Equals(b, StringComparison.OrdinalIgnoreCase));

    if (driverA is null || driverB is null)
    {
        return Results.BadRequest(new { error = $"Unknown driver: {(driverA is null ? a : b)}" });
    }

    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(AnalysisStore.Compare(driverA, driverB));
});

// Which laps have a telemetry trace. Absent for pre-2026 sessions by design.
app.MapGet("/api/analysis/telemetry/{number}", (string number, SessionManager sessions) =>
{
    if (sessions.Store is not { } store || !sessions.TelemetryEnabled)
    {
        return Results.Ok(new { enabled = false, laps = Array.Empty<int>() });
    }

    return Results.Ok(new
    {
        enabled = true,
        laps = TelemetryRecorder.AvailableLaps(store.TelemetryDirectory, number),
    });
});

// One lap's trace: speed, throttle, brake, gear and RPM as parallel arrays.
app.MapGet("/api/analysis/telemetry/{number}/{lap:int}",
    (string number, int lap, SessionManager sessions, HttpContext http) =>
{
    if (sessions.Store is not { } store || !sessions.TelemetryEnabled)
    {
        return Results.NotFound(new { error = "Telemetry is recorded for 2026 sessions onward." });
    }

    var trace = TelemetryRecorder.Read(store.TelemetryDirectory, number, lap).FirstOrDefault();
    if (trace is null) return Results.NotFound(new { error = $"No telemetry for car {number} lap {lap}." });

    // A completed lap's trace never changes.
    http.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Ok(trace);
});

// --- analysis for a FINISHED session, with no replay ------------------------

// Reads the stream at full speed and writes the analysis. A second request
// serves the stored documents instead of recomputing.
app.MapPost("/api/analysis/precompute",
    async (PrecomputeRequest request, AnalysisPrecomputer precomputer, CancellationToken ct) =>
    {
        var result = await precomputer.PrecomputeAsync(
            request.Year, request.Meeting, request.Session, request.Force, ct);

        return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
    });

// A stored analysis, readable without the session being loaded into the
// dashboard at all.
app.MapGet("/api/analysis/{year:int}/{meeting}/{session}",
    async (int year, string meeting, string session,
           AnalysisPrecomputer precomputer, HttpContext http, CancellationToken ct) =>
    {
        var analysis = await precomputer.LoadAsync(year, meeting, session, ct);
        if (analysis is null)
        {
            return Results.NotFound(new { error = "Not computed yet. POST /api/analysis/precompute first." });
        }

        // A finished session's analysis never changes.
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Ok(analysis);
    });

// Stored telemetry for a finished session.
app.MapGet("/api/analysis/{year:int}/{meeting}/{session}/telemetry/{number}/{lap:int}",
    (int year, string meeting, string session, string number, int lap,
     AnalysisPrecomputer precomputer, HttpContext http) =>
    {
        var directory = Path.Combine(
            precomputer.SessionDirectory(year, meeting, session), "analysis", "telemetry");

        var trace = TelemetryRecorder.Read(directory, number, lap).FirstOrDefault();
        if (trace is null) return Results.NotFound(new { error = $"No telemetry for car {number} lap {lap}." });

        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Ok(trace);
    });

// --- season schedule --------------------------------------------------------

// The archive index only lists sessions that already ran, so it cannot answer
// "what is next". Jolpica publishes the full calendar including future rounds.
app.MapGet("/api/schedule/{year:int}",
    async (int year, ScheduleService schedule, HttpContext http, CancellationToken ct) =>
    {
        // Five minutes, not an hour. Round status is derived at read time, so a
        // long client cache would keep showing a race as "upcoming" for an hour
        // after it finished — and it also pinned the response SHAPE, which
        // crashed the page when the podium field was added.
        // The server-side cache is still a day; this only bounds the browser.
        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Ok(await schedule.SeasonAsync(year, ct));
    });

app.MapGet("/api/schedule/{year:int}/next",
    async (int year, ScheduleService schedule, HttpContext http, CancellationToken ct) =>
    {
        // Short, because a countdown goes stale quickly.
        http.Response.Headers.CacheControl = "public, max-age=60";
        return Results.Ok(await schedule.NextAsync(year, ct));
    });

// --- driver profiles --------------------------------------------------------

// Names, team colours and headshot URLs come from the F1 feed's own DriverList;
// Jolpica has none of them.
app.MapGet("/api/drivers/{year:int}",
    async (int year, DriverDirectory directory, HttpContext http, CancellationToken ct) =>
    {
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Ok(await directory.ForSeasonAsync(year, ct));
    });

// --- race results -----------------------------------------------------------

app.MapGet("/api/results/{year:int}/{round:int}",
    async (int year, int round, ResultsService results, HttpContext http, CancellationToken ct) =>
    {
        var race = await results.GetAsync(year, round, ct);
        if (race is null) return Results.NotFound(new { error = $"No results for {year} round {round}." });

        // A completed race's result is final.
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Ok(race);
    });

// One driver's whole season: every round, points progression, and qualifying
// against race result.
app.MapGet("/api/drivers/{year:int}/{driverId}/season",
    async (int year, string driverId, DriverSeasonService seasons, HttpContext http, CancellationToken ct) =>
    {
        var season = await seasons.GetAsync(year, driverId, ct);
        if (season is null) return Results.NotFound(new { error = $"No {year} season for '{driverId}'." });

        // A finished season is immutable; the current one keeps moving.
        http.Response.Headers.CacheControl = year < DateTime.UtcNow.Year
            ? "public, max-age=86400"
            : "public, max-age=300";

        return Results.Ok(season);
    });

// --- championship standings -------------------------------------------------

// The table as it stood after any round. round=0 (or omitted) gives the latest.
app.MapGet("/api/standings/{year:int}",
    async (int year, int? round, StandingsService standings, HttpContext http, CancellationToken ct) =>
    {
        var table = await standings.GetAsync(year, round ?? 0, ct);
        if (table is null) return Results.NotFound(new { error = $"No standings for {year}." });

        // A completed round's table never changes; the latest one keeps moving.
        http.Response.Headers.CacheControl = round is > 0
            ? "public, max-age=86400"
            : "public, max-age=300";

        return Results.Ok(table);
    });

// Forces a write without waiting for the session to end — the button behind
// "export" in the UI.
app.MapPost("/api/analysis/save", async (SessionManager sessions, CancellationToken ct) =>
    await sessions.SaveAnalysisAsync(ct)
        ? Results.Ok(new { saved = true, path = sessions.Store?.Directory })
        : Results.BadRequest(new { error = "No session is running." }));

app.MapGet("/api/track/{circuitKey:int}/{year:int}",
    async (int circuitKey, int year, TrackService tracks, HttpContext http, CancellationToken ct) =>
    {
        var geometry = await tracks.GetAsync(circuitKey, year, ct);
        if (geometry is null) return Results.NotFound(new { error = "Track geometry unavailable" });

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.Ok(geometry);
    });

app.MapLiveSocket();
app.MapRelay(app.Services.GetRequiredService<RelaySessionSource>());



await app.RunAsync(cts.Token);
return 0;

/// <summary>Body of POST /api/analysis/precompute.</summary>
/// <summary>Body of POST /api/session/control.</summary>
internal sealed record ControlRequest(string Action, double? Value = null);

internal sealed record PrecomputeRequest(int Year, string Meeting, string Session, bool Force = false);
