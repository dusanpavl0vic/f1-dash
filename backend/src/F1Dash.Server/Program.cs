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

// Replay and live are the same feature behind one manager, switchable at
// runtime, so the dashboard can move between a 2018 race and a session running
// right now without a restart.
builder.Services.AddSingleton(sp => new SessionManager(
    sp.GetRequiredService<LiveSessionState>(),
    sp.GetRequiredService<ArchiveClient>(),
    archiveRoot,
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

app.MapGet("/api/session", (SessionManager sessions) => Results.Ok(sessions.Current));

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



await app.RunAsync(cts.Token);
return 0;
