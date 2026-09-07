using System.Text.Json.Nodes;
using F1Dash.Core.Archive;
using F1Dash.Server.Ingest;

namespace F1Dash.Server.Catalog;

public sealed record CatalogSession(
    int Year,
    string Meeting,
    string MeetingSlug,
    string Session,
    string SessionSlug,
    string Type,
    bool Downloaded);

public sealed record LiveStatus(bool Live, string? Meeting, string? Session, string? StartDate, string? EndDate);

/// <summary>
/// Lists what can be replayed, and reports whether a session is running now.
///
/// The archive's year index is NOT a complete calendar — 2024 lists only the
/// second half of that season — so the catalog reports what the archive
/// actually offers rather than what the calendar says should exist.
/// </summary>
public sealed class SessionCatalog(ArchiveClient archive, string archiveRoot)
{
    /// <summary>The archive starts in 2018 (docs/03 §2).</summary>
    public const int FirstArchivedSeason = 2018;

    private readonly Dictionary<int, IReadOnlyList<CatalogSession>> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public static IReadOnlyList<int> Seasons()
    {
        var latest = DateTime.UtcNow.Year;
        return [.. Enumerable.Range(FirstArchivedSeason, latest - FirstArchivedSeason + 1).Reverse()];
    }

    public async Task<IReadOnlyList<CatalogSession>> SessionsAsync(int year, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(year, out var cached))
            {
                // Local availability changes as sessions are downloaded, so it
                // is re-evaluated even on a cache hit.
                return [.. cached.Select(s => s with { Downloaded = IsDownloaded(s) })];
            }

            var sessions = await archive.ListSessionsAsync(year, ct).ConfigureAwait(false);

            var mapped = sessions.Select(s => new CatalogSession(
                Year: year,
                Meeting: s.MeetingName,
                MeetingSlug: SessionManager.Slug(s.MeetingName),
                Session: s.SessionName,
                SessionSlug: SessionManager.Slug(s.SessionName),
                Type: s.SessionType,
                Downloaded: false)).ToList();

            _cache[year] = mapped;
            return [.. mapped.Select(s => s with { Downloaded = IsDownloaded(s) })];
        }
        catch (HttpRequestException)
        {
            // Upstream unavailable: fall back to what is on disk so replay still
            // works with no network at all.
            return LocalOnly(year);
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsDownloaded(CatalogSession s) =>
        File.Exists(Path.Combine(archiveRoot, s.Year.ToString(), s.MeetingSlug, s.SessionSlug, "stream.jsonl"));

    private IReadOnlyList<CatalogSession> LocalOnly(int year)
    {
        var root = Path.Combine(archiveRoot, year.ToString());
        if (!Directory.Exists(root)) return [];

        var result = new List<CatalogSession>();
        foreach (var meeting in Directory.EnumerateDirectories(root))
        {
            foreach (var session in Directory.EnumerateDirectories(meeting))
            {
                if (!File.Exists(Path.Combine(session, "stream.jsonl"))) continue;

                var meetingSlug = Path.GetFileName(meeting);
                var sessionSlug = Path.GetFileName(session);
                result.Add(new CatalogSession(
                    year, Titlecase(meetingSlug), meetingSlug,
                    Titlecase(sessionSlug), sessionSlug, "", true));
            }
        }
        return result;
    }

    private static string Titlecase(string slug) =>
        string.Join(' ', slug.Split('-')
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>
    /// Is a session running right now?
    ///
    /// /static/SessionInfo.json points at the most recent session. Comparing its
    /// window against the clock is enough to decide whether to offer live. The
    /// window is padded generously: the feed keeps publishing well after the
    /// chequered flag, and the times are local to the circuit.
    /// </summary>
    public async Task<LiveStatus> LiveAsync(CancellationToken ct)
    {
        try
        {
            using var http = ArchiveClient.CreateHttpClient();
            var text = await http.GetStringAsync(
                "https://livetiming.formula1.com/static/SessionInfo.json", ct).ConfigureAwait(false);

            if (JsonNode.Parse(text.TrimStart('﻿')) is not JsonObject info)
            {
                return new LiveStatus(false, null, null, null, null);
            }

            var start = (string?)info["StartDate"];
            var end = (string?)info["EndDate"];

            var live = DateTime.TryParse(start, out var from)
                && DateTime.TryParse(end, out var to)
                && DateTime.UtcNow >= from.AddHours(-2)
                && DateTime.UtcNow <= to.AddHours(2);

            return new LiveStatus(
                live,
                (string?)info["Meeting"]?["Name"],
                (string?)info["Name"],
                start,
                end);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new LiveStatus(false, null, null, null, null);
        }
    }
}
