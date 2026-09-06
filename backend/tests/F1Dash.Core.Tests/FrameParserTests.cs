using System.Text.Json.Nodes;
using F1Dash.Core;
using F1Dash.Core.Signalr;
using Xunit;

namespace F1Dash.Core.Tests;

public class InflateTests
{
    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var original = JsonNode.Parse("""
            {"Position":[{"Timestamp":"2024-06-23T15:04:22.123Z","Entries":{"1":{"Status":"OnTrack","X":1234,"Y":-5678,"Z":101}}}]}
            """);

        var decoded = Inflate.Decode(Inflate.Encode(original));

        Assert.Equal(original!.ToJsonString(), decoded!.ToJsonString());
    }

    [Fact]
    public void Decode_HandlesAPayloadWithNoZlibHeader()
    {
        // The feed sends raw DEFLATE. This is the assertion that catches someone
        // swapping DeflateStream for ZLibStream, which throws on the header.
        var encoded = Inflate.Encode(JsonNode.Parse("""{"Entries":[]}"""));
        Assert.NotNull(Inflate.Decode(encoded));
    }

    [Fact]
    public void Decode_RejectsInvalidBase64WithAClearError() =>
        Assert.Throws<FormatException>(() => Inflate.Decode("not base64 at all !!"));
}

public class FrameParserTests
{
    [Fact]
    public void KeepAlive_YieldsNothing()
    {
        Assert.Empty(FrameParser.Parse("{}"));
        Assert.Empty(FrameParser.Parse(""));
    }

    [Fact]
    public void InitialState_YieldsEveryTopic()
    {
        var frame = """
            {"R":{"Heartbeat":{"Utc":"x"},"DriverList":{"44":{"Tla":"HAM"},"_kf":true},"TrackStatus":{"Status":"1"}},"I":"1"}
            """;

        var updates = FrameParser.Parse(frame).ToList();

        Assert.Equal(3, updates.Count);
        Assert.All(updates, u => Assert.True(u.IsSnapshot));
        Assert.Contains(updates, u => u.Topic == Topics.DriverList);
    }

    [Fact]
    public void DeltaFrame_WithThreeFeedEntries_YieldsThreeUpdates()
    {
        // docs/04: "A single message can carry multiple topic updates. Never
        // assume M has length 1." This is the acceptance criterion verbatim.
        var frame = """
            {"C":"d-ABC","M":[
              {"H":"Streaming","M":"feed","A":["TimingData",{"Lines":{"44":{"Position":"1"}}},"2024-06-23T15:04:22.123Z"]},
              {"H":"Streaming","M":"feed","A":["TrackStatus",{"Status":"2"},"2024-06-23T15:04:22.456Z"]},
              {"H":"Streaming","M":"feed","A":["LapCount",{"CurrentLap":34},"2024-06-23T15:04:22.789Z"]}
            ]}
            """;

        var updates = FrameParser.Parse(frame).ToList();

        Assert.Equal(3, updates.Count);
        Assert.Equal(Topics.TimingData, updates[0].Topic);
        Assert.Equal(Topics.TrackStatus, updates[1].Topic);
        Assert.Equal(Topics.LapCount, updates[2].Topic);
        Assert.Equal("2024-06-23T15:04:22.456Z", updates[1].Timestamp);
        Assert.All(updates, u => Assert.False(u.IsSnapshot));
    }

    [Fact]
    public void CompressedTopic_IsInflatedAndTheSuffixStripped()
    {
        var payload = JsonNode.Parse("""{"Entries":{"1":{"X":10,"Y":20,"Z":30}}}""");
        var frame = $$"""
            {"M":[{"H":"Streaming","M":"feed","A":["Position.z","{{Inflate.Encode(payload)}}","t"]}]}
            """;

        var update = Assert.Single(FrameParser.Parse(frame));

        Assert.Equal(Topics.Position, update.Topic);
        Assert.Equal(10, (int?)update.Payload["Entries"]!["1"]!["X"]);
    }

    [Fact]
    public void NonFeedInvocations_AreIgnored()
    {
        var frame = """{"M":[{"H":"Streaming","M":"somethingElse","A":["TimingData",{}]}]}""";
        Assert.Empty(FrameParser.Parse(frame));
    }

    [Fact]
    public void MalformedFrame_DoesNotThrow()
    {
        // One bad payload must never take down the connection mid-session.
        Assert.Empty(FrameParser.Parse("{not json"));
        Assert.Empty(FrameParser.Parse("""{"M":[{"H":"Streaming","M":"feed","A":["Position.z","!!!not-base64!!!","t"]}]}"""));
    }

    [Fact]
    public void ArrayPayload_IsNormalisedToAnIndexKeyedObject()
    {
        var frame = """{"M":[{"H":"Streaming","M":"feed","A":["RaceControlMessages",[{"Message":"GREEN"}],"t"]}]}""";

        var update = Assert.Single(FrameParser.Parse(frame));

        Assert.Equal("GREEN", (string?)update.Payload["0"]!["Message"]);
    }
}
