using System.Net;
using System.Text;
using F1Dash.Core.Archive;
using F1Dash.Server.Ingest;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace F1Dash.Core.Tests;

/// <summary>
/// A ranged read almost never lands on a line boundary. These pin the one
/// property that matters: polling a file in arbitrary chunks must produce
/// exactly what reading it whole produces — no duplicates, no losses, no
/// truncated payloads.
/// </summary>
public class StaticPollingSourceTests
{
    private const string Body =
        "﻿00:00:01.000{\"Lines\":{\"1\":{\"Position\":\"1\"}}}\r\n" +
        "00:00:02.500{\"Lines\":{\"1\":{\"Position\":\"2\"}}}\r\n" +
        "00:00:03.250{\"Lines\":{\"44\":{\"Position\":\"3\"}}}\r\n" +
        "00:00:04.000{\"Lines\":{\"44\":{\"GapToLeader\":\"+1.2\"}}}\r\n";

    private static async Task<List<string>> CollectAsync(int chunkSize)
    {
        var handler = new ChunkedArchiveHandler(Body, chunkSize);
        using var http = new HttpClient(handler);

        var source = new StaticPollingSource(
            http, NullLogger.Instance, TimeSpan.FromMilliseconds(1), "https://example.test/static/");

        var positions = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var update in source.ReadAsync(cts.Token))
        {
            // The payload identifies the record well enough to compare runs.
            positions.Add(update.Payload.ToJsonString());
            if (positions.Count == 4) break;
        }

        return positions;
    }

    [Theory]
    [InlineData(4096)]  // whole file in one read
    [InlineData(30)]    // splits mid-payload
    [InlineData(13)]    // splits just past the timestamp
    [InlineData(7)]     // splits inside the timestamp itself
    [InlineData(1)]     // one byte at a time
    public async Task Chunking_does_not_change_the_result(int chunkSize)
    {
        var whole = await CollectAsync(4096);
        var chunked = await CollectAsync(chunkSize);

        Assert.Equal(4, whole.Count);
        Assert.Equal(whole, chunked);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(3)]
    [InlineData(1)]
    public async Task Multi_byte_characters_survive_a_chunk_boundary(int chunkSize)
    {
        // This is what the stateful decoder is actually for. Driver names are
        // full of non-ASCII, and decoding each ranged read independently turns
        // any character straddling the boundary into replacement marks.
        const string accented =
            "\uFEFF00:00:01.000{\"Lines\":{\"27\":{\"Name\":\"H\u00fclkenberg\"}}}\r\n" +
            "00:00:02.000{\"Lines\":{\"11\":{\"Name\":\"P\u00e9rez\"}}}\r\n";

        var handler = new ChunkedArchiveHandler(accented, chunkSize);
        using var http = new HttpClient(handler);

        var source = new StaticPollingSource(
            http, NullLogger.Instance, TimeSpan.FromMilliseconds(1), "https://example.test/static/");

        var seen = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var update in source.ReadAsync(cts.Token))
        {
            // The decoded VALUE, not the serialised form: System.Text.Json
            // escapes non-ASCII on output, so asserting against the JSON text
            // would test the encoder rather than the decoder.
            var line = update.Payload["Lines"]!.AsObject().First().Value!;
            seen.Add((string)line["Name"]!);
            if (seen.Count == 2) break;
        }

        Assert.Equal("H\u00fclkenberg", seen[0]);
        Assert.Equal("P\u00e9rez", seen[1]);
    }

    [Fact]
    public async Task The_byte_order_mark_does_not_corrupt_the_first_record()
    {
        // The archive is served with a BOM. Left in place it lands in front of
        // the first timestamp, and the first record of every file fails to
        // parse — silently, since a bad line is skipped by design.
        var entries = await CollectAsync(4096);

        Assert.Contains("\"Position\":\"1\"", entries[0]);
    }
}

/// <summary>
/// Serves the archive endpoints, honouring Range, and never returning more than
/// <paramref name="chunkSize"/> bytes at a time.
/// </summary>
internal sealed class ChunkedArchiveHandler(string body, int chunkSize) : HttpMessageHandler
{
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes(body);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();

        if (url.EndsWith("SessionInfo.json", StringComparison.Ordinal))
        {
            return Json("""{"Path":"2026/session/","Name":"Race"}""");
        }

        if (url.EndsWith("Index.json", StringComparison.Ordinal))
        {
            return Json("""{"Feeds":{"TimingData":{"StreamPath":"TimingData.jsonStream"}}}""");
        }

        var from = (int?)request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;

        if (from >= _bytes.Length)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
        }

        var length = Math.Min(chunkSize, _bytes.Length - from);
        var slice = _bytes.AsSpan(from, length).ToArray();

        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(slice),
        };
        return Task.FromResult(response);
    }

    private static Task<HttpResponseMessage> Json(string text) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "application/json"),
        });
}
