using F1Dash.Core;
using F1Dash.Core.Archive;
using F1Dash.Core.Signalr;
using System.Text.Json.Nodes;
using Xunit;

namespace F1Dash.Core.Tests;

public class JsonStreamParserTests
{
    [Theory]
    [InlineData("00:00:00.000", 0L)]
    [InlineData("00:00:12.345", 12_345L)]
    [InlineData("01:23:45.678", 5_025_678L)]
    [InlineData("27:00:00.000", 97_200_000L)] // hours may exceed 24
    public void TryParseOffset_ReadsTheTwelveCharacterStamp(string stamp, long expected)
    {
        Assert.True(JsonStreamParser.TryParseOffset(stamp, out var ms));
        Assert.Equal(expected, ms);
    }

    [Theory]
    [InlineData("00:00:12,345")]  // wrong decimal separator
    [InlineData("0:00:12.345")]   // too short
    [InlineData("aa:bb:cc.ddd")]
    public void TryParseOffset_RejectsMalformedStamps(string stamp) =>
        Assert.False(JsonStreamParser.TryParseOffset(stamp, out _));

    [Fact]
    public void TryParseLine_SplitsStampFromPayloadWithNoSeparator()
    {
        var ok = JsonStreamParser.TryParseLine(
            """00:00:12.345{"Status":"1","Message":"AllClear"}""", Topics.TrackStatus, out var entry);

        Assert.True(ok);
        Assert.Equal(12_345, entry.OffsetMs);
        Assert.Equal(Topics.TrackStatus, entry.Topic);
        Assert.Equal("AllClear", (string?)entry.Payload["Message"]);
    }

    [Fact]
    public void TryParseLine_StripsAUtf8Bom()
    {
        // The archive serves every file with a BOM. Reading as plain utf-8
        // instead of utf-8-sig breaks the first line of every single file.
        var ok = JsonStreamParser.TryParseLine(
            "﻿" + """00:00:01.000{"Status":"1"}""", Topics.TrackStatus, out var entry);

        Assert.True(ok);
        Assert.Equal(1000, entry.OffsetMs);
    }

    [Fact]
    public void TryParseLine_InflatesACompressedTopicAndStripsTheSuffix()
    {
        var payload = JsonNode.Parse("""{"Entries":{"44":{"X":1,"Y":2,"Z":3}}}""");
        var line = "00:01:00.000" + System.Text.Json.JsonSerializer.Serialize(Inflate.Encode(payload));

        var ok = JsonStreamParser.TryParseLine(line, "Position.z", out var entry);

        Assert.True(ok);
        Assert.Equal(Topics.Position, entry.Topic);
        Assert.Equal(60_000, entry.OffsetMs);
        Assert.Equal(1, (int?)entry.Payload["Entries"]!["44"]!["X"]);
    }

    [Fact]
    public void TryParseLine_SkipsCorruptLinesRatherThanThrowing()
    {
        // One bad line must not abort a two-hour session.
        Assert.False(JsonStreamParser.TryParseLine("", Topics.TrackStatus, out _));
        Assert.False(JsonStreamParser.TryParseLine("00:00:01.000{not json", Topics.TrackStatus, out _));
        Assert.False(JsonStreamParser.TryParseLine("garbage", Topics.TrackStatus, out _));
    }

    [Fact]
    public void Parse_ReadsAWholeFileInOrder()
    {
        var text = string.Join("\n",
            """00:00:01.000{"Status":"1"}""",
            "",
            """00:00:02.500{"Status":"2"}""",
            """00:00:03.000{"Status":"4"}""");

        var entries = JsonStreamParser.Parse(new StringReader(text), Topics.TrackStatus).ToList();

        Assert.Equal(3, entries.Count);
        Assert.Equal([1000L, 2500L, 3000L], entries.Select(e => e.OffsetMs));
    }
}
