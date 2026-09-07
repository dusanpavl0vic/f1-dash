using F1Dash.Core.Projections;
using F1Dash.Core.Track;
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

if (File.Exists(replayStream))
{
    var speed = double.TryParse(Environment.GetEnvironmentVariable("REPLAY_SPEED"), out var s) ? s : 1.0;
    var start = long.TryParse(Environment.GetEnvironmentVariable("REPLAY_START_MS"), out var st) ? st : 0;
    var loop = Environment.GetEnvironmentVariable("REPLAY_LOOP") == "1";

    builder.Services.AddSingleton<ISessionSource>(
        new ReplaySessionSource(replayStream, speed, start, loop));
    builder.Services.AddHostedService<IngestService>();
}

var app = builder.Build();
app.UseWebSockets();
app.UseCors();

var live = app.Services.GetRequiredService<LiveSessionState>();

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    live = live.HasData,
    clients = live.ClientCount,
    sequence = live.Sequence,
}));

// A plain-JSON view of the current state, for debugging without a WebSocket.
app.MapGet("/state", () => Results.Text(live.Snapshot().ToJsonString(), "application/json"));

app.MapGet("/classification", () => Results.Ok(Classification.From(live.Snapshot())));

// Circuit geometry, already rotated, Y-flipped and turned into an SVG path, so
// the client renders it with no maths of its own. Immutable: a circuit's shape
// does not change mid-season.
app.MapGet("/api/track/{circuitKey:int}/{year:int}",
    async (int circuitKey, int year, TrackService tracks, HttpContext http, CancellationToken ct) =>
    {
        var geometry = await tracks.GetAsync(circuitKey, year, ct);
        if (geometry is null) return Results.NotFound(new { error = "Track geometry unavailable" });

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.Ok(geometry);
    });

app.MapLiveSocket();

if (!File.Exists(replayStream))
{
    app.Logger.LogWarning(
        "No session source. {Path} does not exist — run `make fixture`. Serving an empty state.",
        replayStream);
}

await app.RunAsync(cts.Token);
return 0;
