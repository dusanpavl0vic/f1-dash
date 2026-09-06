using System.Diagnostics;
using F1Dash.Core.Archive;

namespace F1Dash.Server.Cli;

/// <summary>
/// Downloads an archived session into a time-ordered stream on disk.
///
/// This is the command that makes the whole project developable: F1 runs on
/// roughly 24 weekends a year, and everything downstream of here is verified
/// against the files this produces.
/// </summary>
public static class ArchiveCommand
{
    public static async Task<int> ListAsync(int year, CancellationToken ct)
    {
        using var http = ArchiveClient.CreateHttpClient(Environment.GetEnvironmentVariable("F1_HTTP_PROXY"));
        var client = new ArchiveClient(http);

        var sessions = await client.ListSessionsAsync(year, ct);
        Console.WriteLine($"{sessions.Count} sessions in {year}\n");

        foreach (var group in sessions.GroupBy(s => s.MeetingName))
        {
            Console.WriteLine(group.Key);
            foreach (var s in group)
            {
                Console.WriteLine($"    {s.SessionName,-16} {s.SessionType,-12} {s.Path}");
            }
        }
        return 0;
    }

    public static async Task<int> DownloadAsync(
        int year, string meetingFilter, string sessionFilter, string outputRoot, bool includeTelemetry, CancellationToken ct)
    {
        using var http = ArchiveClient.CreateHttpClient(Environment.GetEnvironmentVariable("F1_HTTP_PROXY"));
        var client = new ArchiveClient(http);

        var sessions = await client.ListSessionsAsync(year, ct);

        var match = sessions.FirstOrDefault(s =>
            s.MeetingName.Contains(meetingFilter, StringComparison.OrdinalIgnoreCase) &&
            s.SessionName.Equals(sessionFilter, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            Console.Error.WriteLine($"No session matched meeting '{meetingFilter}' session '{sessionFilter}' in {year}.");
            Console.Error.WriteLine("Run `list` to see what is available.");
            return 1;
        }

        Console.WriteLine($"{match.MeetingName} — {match.SessionName}");
        Console.WriteLine($"path: {match.Path}");

        var sw = Stopwatch.StartNew();
        var downloader = new SessionDownloader(client);
        var entries = await downloader.DownloadAsync(
            match.Path,
            (stage, done, total) => Console.WriteLine($"  [{done}/{total}] {stage}"),
            includeTelemetry,
            ct);

        var slug = Slug(match.MeetingName);
        var outPath = Path.Combine(outputRoot, year.ToString(), slug, Slug(match.SessionName), "stream.jsonl");
        await SessionDownloader.WriteJsonlAsync(entries, outPath, ct);

        var durationMs = entries.Count > 0 ? entries[^1].OffsetMs : 0;
        Console.WriteLine();
        Console.WriteLine($"  entries : {entries.Count:N0}");
        Console.WriteLine($"  duration: {TimeSpan.FromMilliseconds(durationMs):hh\\:mm\\:ss}");
        Console.WriteLine($"  topics  : {string.Join(", ", entries.Select(e => e.Topic).Distinct().Order())}");
        Console.WriteLine($"  written : {outPath} ({new FileInfo(outPath).Length / 1024 / 1024:N0} MB)");
        Console.WriteLine($"  elapsed : {sw.Elapsed:mm\\:ss}");
        return 0;
    }

    private static string Slug(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-'))
              .Trim('-')
              .Replace("--", "-");
}
