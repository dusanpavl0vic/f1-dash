using F1Dash.Core;
using F1Dash.Core.Signalr;
using System.Text.Json.Nodes;
using Xunit;

namespace F1Dash.Core.Tests;

public class CoreFrameParserTests
{
    private const string RS = "";

    [Fact]
    public void SplitMessages_SeparatesOnTheRecordSeparator()
    {
        var buffer = "{\"type\":6}" + RS
                   + "{\"type\":1,\"target\":\"feed\",\"arguments\":[\"TrackStatus\",{\"Status\":\"1\"}]}" + RS;

        Assert.Equal(2, CoreFrameParser.SplitMessages(buffer).Count());
    }

    [Fact]
    public void SplitMessages_IgnoresATrailingEmptySegment()
    {
        Assert.Single(CoreFrameParser.SplitMessages("{\"type\":6}" + RS));
    }

    [Fact]
    public void FeedInvocation_YieldsATopicUpdate()
    {
        const string message =
            """{"type":1,"target":"feed","arguments":["TrackStatus",{"Status":"2","Message":"Yellow"},"2026-09-06T15:04:22.1Z"]}""";

        var update = Assert.Single(CoreFrameParser.Parse(message));

        Assert.Equal(Topics.TrackStatus, update.Topic);
        Assert.Equal("2", (string?)update.Payload["Status"]);
        Assert.Equal("2026-09-06T15:04:22.1Z", update.Timestamp);
        Assert.False(update.IsSnapshot);
    }

    [Fact]
    public void CompletionFrame_CarriesTheFullInitialState()
    {
        const string message =
            """{"type":3,"invocationId":"0","result":{"TrackStatus":{"Status":"1"},"LapCount":{"CurrentLap":1}}}""";

        var updates = CoreFrameParser.Parse(message).ToList();

        Assert.Equal(2, updates.Count);
        Assert.All(updates, u => Assert.True(u.IsSnapshot));
    }

    [Fact]
    public void CompressedTopic_IsInflatedAndTheSuffixStripped()
    {
        var payload = JsonNode.Parse("""{"Entries":{"1":{"X":7}}}""");
        var message =
            $$"""{"type":1,"target":"feed","arguments":["Position.z","{{Inflate.Encode(payload)}}","t"]}""";

        var update = Assert.Single(CoreFrameParser.Parse(message));

        Assert.Equal(Topics.Position, update.Topic);
        Assert.Equal(7, (int?)update.Payload["Entries"]!["1"]!["X"]);
    }

    [Fact]
    public void PingAndCloseFrames_YieldNothing()
    {
        Assert.Empty(CoreFrameParser.Parse("""{"type":6}"""));
        Assert.Empty(CoreFrameParser.Parse("""{"type":7}"""));
        Assert.Empty(CoreFrameParser.Parse("{}"));
    }

    [Fact]
    public void MalformedMessage_DoesNotThrow() =>
        Assert.Empty(CoreFrameParser.Parse("{not json"));
}
