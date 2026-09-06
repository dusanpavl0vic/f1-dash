using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1Dash.Core.Archive;

/// <summary>Metadata for one archived session.</summary>
public sealed record ArchivedSession(
    int Year,
    string MeetingName,
    string SessionName,
    string SessionType,
    string Path);

/// <summary>
/// Reads the F1 static archive (docs/03 §2). Every session since 2018 is here,
/// and this is the backbone of both replay and every test fixture.
///
/// Unlike the live SignalR feed this host is NOT IP-blocked, so it works from
/// anywhere including a datacentre.
/// </summary>
public sealed class ArchiveClient(HttpClient http)
{
    public const string DefaultBaseUrl = "https://livetiming.formula1.com/static/";

    /// <summary>
    /// Archive files are served with a UTF-8 BOM. Decoding as plain UTF-8 leaves
    /// the BOM on the first line and breaks the first record of every file.
    /// </summary>
    private static readonly UTF8Encoding Utf8Sig = new(encoderShouldEmitUTF8Identifier: false);

    public static HttpClient CreateHttpClient(string? proxy = null)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new System.Net.WebProxy(proxy);
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        // A polite, identifying agent. The static archive does not require the
        // "BestHTTP" spoof that the live SignalR endpoint insists on.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("f1-dash", "0.1"));
        return client;
    }

    /// <summary>Lists every session in a season, in calendar order.</summary>
    public async Task<IReadOnlyList<ArchivedSession>> ListSessionsAsync(int year, CancellationToken ct = default)
    {
        var index = await GetJsonAsync($"{year}/Index.json", ct).ConfigureAwait(false);
        var sessions = new List<ArchivedSession>();

        if (index?["Meetings"] is not JsonArray meetings) return sessions;

        foreach (var meeting in meetings.OfType<JsonObject>())
        {
            var meetingName = (string?)meeting["Name"] ?? "";

            if (meeting["Sessions"] is not JsonArray meetingSessions) continue;

            foreach (var session in meetingSessions.OfType<JsonObject>())
            {
                var path = (string?)session["Path"];
                // Sessions that never ran (cancelled, or not yet held) have no path.
                if (string.IsNullOrWhiteSpace(path)) continue;

                sessions.Add(new ArchivedSession(
                    year,
                    meetingName,
                    (string?)session["Name"] ?? "",
                    (string?)session["Type"] ?? "",
                    path));
            }
        }

        return sessions;
    }

    /// <summary>Which feeds this session actually published, and their stream paths.</summary>
    public async Task<IReadOnlyDictionary<string, string>> ListFeedsAsync(string sessionPath, CancellationToken ct = default)
    {
        var index = await GetJsonAsync($"{sessionPath}Index.json", ct).ConfigureAwait(false);
        var feeds = new Dictionary<string, string>(StringComparer.Ordinal);

        if (index?["Feeds"] is not JsonObject feedObject) return feeds;

        foreach (var (topic, descriptor) in feedObject)
        {
            if (descriptor?["StreamPath"] is not JsonValue v) continue;
            if (v.TryGetValue<string>(out var streamPath) && !string.IsNullOrWhiteSpace(streamPath))
            {
                feeds[topic] = streamPath;
            }
        }

        return feeds;
    }

    /// <summary>Downloads and parses one topic's stream file.</summary>
    public async Task<List<StreamEntry>> DownloadTopicAsync(
        string sessionPath, string topic, string streamPath, CancellationToken ct = default)
    {
        var url = BaseUrl + sessionPath + streamPath;
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Utf8Sig, detectEncodingFromByteOrderMarks: true);

        var entries = new List<StreamEntry>();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (JsonStreamParser.TryParseLine(line, topic, out var entry))
            {
                entries.Add(entry);
            }
        }
        return entries;
    }

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    private async Task<JsonObject?> GetJsonAsync(string relative, CancellationToken ct)
    {
        using var response = await http.GetAsync(BaseUrl + relative, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Utf8Sig, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        return JsonNode.Parse(text.TrimStart('﻿')) as JsonObject;
    }
}
