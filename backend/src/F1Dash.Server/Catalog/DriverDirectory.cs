using System.Text.Json.Nodes;
using F1Dash.Core.Archive;

namespace F1Dash.Server.Catalog;

public sealed record DriverProfile(
    string RacingNumber,
    string Tla,
    string FullName,
    string FirstName,
    string LastName,
    string TeamName,
    /// <summary>Hex without a leading '#', as the feed sends it.</summary>
    string TeamColour,
    string CountryCode,
    /// <summary>
    /// The feed's own headshot URL. Only this exact URL resolves to a real
    /// photograph — a reconstructed one returns F1's 700-byte fallback.
    /// </summary>
    string? HeadshotUrl,
    string Reference);

/// <summary>
/// Driver profiles for a season, read from the archive's own DriverList.
///
/// Jolpica gives names and nationalities but no photographs or team colours;
/// the F1 feed gives all of it. The DriverList keyframe is a small file, so one
/// request per season is enough — no session needs downloading.
/// </summary>
public sealed class DriverDirectory(ArchiveClient archive, ILogger<DriverDirectory> logger)
{
    private readonly Dictionary<int, IReadOnlyList<DriverProfile>> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<DriverProfile>> ForSeasonAsync(int year, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(year, out var cached)) return cached;

            var sessions = await archive.ListSessionsAsync(year, ct).ConfigureAwait(false);

            // The LAST race of the season has the most complete grid: mid-season
            // replacements are present and pre-season entries that never raced
            // are not.
            var source = sessions.LastOrDefault(s => s.SessionName == "Race")
                ?? sessions.LastOrDefault();

            if (source is null) return [];

            var profiles = await ReadDriverListAsync(source.Path, ct).ConfigureAwait(false);
            _cache[year] = profiles;
            return profiles;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "Could not read the {Year} driver list", year);
            return [];
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<DriverProfile>> ReadDriverListAsync(string sessionPath, CancellationToken ct)
    {
        using var http = ArchiveClient.CreateHttpClient();

        // The keyframe is the whole DriverList in one small document — far
        // cheaper than the stream, which is tens of megabytes.
        var url = $"{ArchiveClient.DefaultBaseUrl}{sessionPath}DriverList.json";
        var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);

        if (JsonNode.Parse(text.TrimStart('﻿')) is not JsonObject list) return [];

        var profiles = new List<DriverProfile>();

        foreach (var (number, raw) in list)
        {
            if (raw is not JsonObject d) continue;
            if ((string?)d["Tla"] is not { Length: > 0 } tla) continue;

            profiles.Add(new DriverProfile(
                RacingNumber: (string?)d["RacingNumber"] ?? number,
                Tla: tla,
                FullName: (string?)d["FullName"] ?? "",
                FirstName: (string?)d["FirstName"] ?? "",
                LastName: (string?)d["LastName"] ?? "",
                TeamName: (string?)d["TeamName"] ?? "",
                TeamColour: (string?)d["TeamColour"] ?? "",
                CountryCode: (string?)d["CountryCode"] ?? "",
                HeadshotUrl: (string?)d["HeadshotUrl"],
                Reference: (string?)d["Reference"] ?? ""));
        }

        profiles.Sort((a, b) => string.Compare(a.Tla, b.Tla, StringComparison.Ordinal));
        return profiles;
    }
}
