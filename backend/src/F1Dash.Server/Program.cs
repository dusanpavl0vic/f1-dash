using F1Dash.Server.Cli;

// The backend is one application. It runs as a web host by default and exposes
// a few operational subcommands for work that happens outside a request —
// downloading archived sessions being the one the whole project depends on,
// because F1 runs on roughly 24 weekends a year and everything else is
// developed against those files.

const string Usage = """
    Usage:
      list <year>                                  list archived sessions for a season
      archive <year> <meeting> <session> [--no-telemetry]
                                                   download one session to ARCHIVE_PATH

    Examples:
      list 2024
      archive 2024 Bahrain Race
      archive 2024 Monaco Qualifying --no-telemetry
    """;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (args.Length > 0)
{
    var archiveRoot = Environment.GetEnvironmentVariable("ARCHIVE_PATH") ?? "./data/archive";

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
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

await app.RunAsync(cts.Token);
return 0;
