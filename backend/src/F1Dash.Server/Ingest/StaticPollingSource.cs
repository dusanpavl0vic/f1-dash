using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using F1Dash.Core;
using F1Dash.Core.Archive;
using F1Dash.Core.Signalr;

namespace F1Dash.Server.Ingest;

/// <summary>
/// Follows a live session by re-reading the static archive, instead of holding
/// a socket open to the SignalR origin.
///
/// The reason this exists is measurable rather than theoretical. From a plain
/// server, `signalr/negotiate` answers 401 while `static/…` answers 200 — the
/// static path is S3 behind CloudFront, an ordinary CDN, and is not subject to
/// the origin's IP filtering. So when the socket is refused, the same data is
/// still readable a second at a time.
///
/// It is deliberately the SAME data, not an approximation: the archive files
/// are written during the session in exactly the format the replay source
/// already parses, so `JsonStreamParser` is reused rather than duplicated.
///
/// The cost is latency equal to the poll interval — one to three seconds behind
/// the socket. Against a television feed that is 5-60 seconds behind, that is
/// invisible.
/// </summary>
public sealed class StaticPollingSource(
    HttpClient http,
    ILogger logger,
    TimeSpan? interval = null,
    string baseUrl = ArchiveClient.DefaultBaseUrl) : ISessionSource
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(1);

    /// <summary>Byte offset already consumed, per topic stream.</summary>
    private readonly Dictionary<string, long> _offsets = [];

    /// <summary>
    /// Bytes read but not yet forming a complete line, per topic.
    ///
    /// A ranged read almost always lands mid-line. Parsing the tail would
    /// produce a truncated JSON payload, and — worse — skipping it would lose
    /// the record entirely, so the remainder is carried into the next poll.
    /// </summary>
    private readonly Dictionary<string, string> _remainders = [];

    /// <summary>
    /// A stateful UTF-8 decoder per topic.
    ///
    /// Decoding each ranged read independently corrupts any multi-byte
    /// character that straddles the boundary — and driver names are full of
    /// them ("Hulkenberg" and "Perez" are not spelled that way in the feed).
    /// A Decoder holds the incomplete sequence and completes it on the next
    /// call, which is the only correct way to decode a stream in pieces.
    /// </summary>
    private readonly Dictionary<string, Decoder> _decoders = [];

    /// <summary>Topics whose leading byte order mark has already been consumed.</summary>
    private readonly HashSet<string> _bomStripped = [];

    public string Description => "live (static archive, polled)";

    public async IAsyncEnumerable<TopicUpdate> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sessionPath = await ResolveSessionPathAsync(ct).ConfigureAwait(false);
        if (sessionPath is null)
        {
            logger.LogWarning("No live session is published; nothing to poll.");
            yield break;
        }

        logger.LogInformation("Polling {Path} every {Interval}", sessionPath, _interval);

        var feeds = await FeedsAsync(sessionPath, ct).ConfigureAwait(false);
        if (feeds.Count == 0)
        {
            logger.LogWarning("Session {Path} publishes no feeds yet.", sessionPath);
            yield break;
        }

        var idleRounds = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = new List<StreamEntry>();

            foreach (var (topic, streamPath) in feeds)
            {
                var entries = await PollTopicAsync(sessionPath, topic, streamPath, ct)
                    .ConfigureAwait(false);
                batch.AddRange(entries);
            }

            // Ordered across topics, not just within one. Downstream state is
            // delta-accumulated, so applying a later CarData frame before an
            // earlier TimingData frame would leave the two describing different
            // moments.
            foreach (var entry in batch.OrderBy(e => e.OffsetMs))
            {
                yield return new TopicUpdate(entry.Topic, entry.Payload, null, false);
            }

            // A session that stops growing has ended. Re-reading a finished
            // session forever would keep a dead source alive and hide the fact
            // that live has finished.
            idleRounds = batch.Count == 0 ? idleRounds + 1 : 0;
            if (idleRounds > 600)
            {
                logger.LogInformation("No new data for 10 minutes; the session has ended.");
                yield break;
            }

            await Task.Delay(_interval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reads whatever has been appended to one topic since the last poll.</summary>
    private async Task<List<StreamEntry>> PollTopicAsync(
        string sessionPath, string topic, string streamPath, CancellationToken ct)
    {
        var url = baseUrl + sessionPath + streamPath;
        var from = _offsets.GetValueOrDefault(topic);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, null);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            // One topic failing must not stop the others. The offset is left
            // untouched, so the next poll simply asks again.
            logger.LogDebug(e, "Poll failed for {Topic}", topic);
            return [];
        }

        using (response)
        {
            // 416 means the file has not grown past what we already read. That
            // is the normal quiet case, not an error.
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) return [];

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Poll for {Topic} returned {Status}", topic, response.StatusCode);
                return [];
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0) return [];

            // A server that ignores Range answers 200 with the whole file. The
            // offset must then be treated as absolute, or every poll would
            // replay the session from the start.
            var wholeFile = response.StatusCode != HttpStatusCode.PartialContent;
            if (wholeFile && from > 0)
            {
                if (bytes.Length <= from) return [];
                bytes = bytes[(int)from..];
            }

            _offsets[topic] = from + bytes.Length;

            if (!_decoders.TryGetValue(topic, out var decoder))
            {
                decoder = Encoding.UTF8.GetDecoder();
                _decoders[topic] = decoder;
            }

            var chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length, flush: false)];
            var written = decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
            var text = new string(chars, 0, written);

            // Once decoded the BOM is a single character, wherever its bytes
            // happened to be split. Stripped once per topic rather than on every
            // chunk: U+FEFF later in a stream is a zero-width no-break space and
            // is not ours to remove.
            if (_bomStripped.Add(topic) && text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text[1..];
            }

            return ParseComplete(topic, text);
        }
    }

    /// <summary>
    /// Parses whole lines only, carrying any partial trailing line forward.
    /// </summary>
    private List<StreamEntry> ParseComplete(string topic, string chunk)
    {
        var text = _remainders.GetValueOrDefault(topic, "") + chunk;
        var entries = new List<StreamEntry>();

        var start = 0;
        var lastBreak = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;

            var line = text.AsSpan(start, i - start);
            if (JsonStreamParser.TryParseLine(line, topic, out var entry)) entries.Add(entry);

            start = i + 1;
            lastBreak = i;
        }

        _remainders[topic] = lastBreak < 0 ? text : text[(lastBreak + 1)..];
        return entries;
    }

    /// <summary>The path of the session running now, or null when none is.</summary>
    private async Task<string?> ResolveSessionPathAsync(CancellationToken ct)
    {
        try
        {
            var text = await http.GetStringAsync($"{baseUrl}SessionInfo.json", ct).ConfigureAwait(false);
            var info = JsonNode.Parse(text.TrimStart('﻿')) as JsonObject;

            var path = (string?)info?["Path"];
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "Could not resolve the current session path.");
            return null;
        }
    }

    /// <summary>The topics this session publishes, filtered to the ones we use.</summary>
    private async Task<Dictionary<string, string>> FeedsAsync(string sessionPath, CancellationToken ct)
    {
        var client = new ArchiveClient(http) { BaseUrl = baseUrl };
        var feeds = await client.ListFeedsAsync(sessionPath, ct).ConfigureAwait(false);

        // Subscribing to everything would poll feeds nothing renders. The
        // subscription list is the same one the socket uses, so both sources
        // deliver the same set of topics.
        var wanted = new HashSet<string>(Topics.Subscription, StringComparer.Ordinal);

        return feeds
            .Where(f => wanted.Contains(Topics.Normalise(f.Key)))
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
    }
}
