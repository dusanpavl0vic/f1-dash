using System.Text.Json.Nodes;
using F1Dash.Core;
using F1Dash.Core.Merge;
using F1Dash.Core.Signalr;
using Xunit;

namespace F1Dash.Core.Tests;

public class StateAccumulatorTests
{
    private static TopicUpdate Update(string topic, string json, bool snapshot = false) =>
        new(topic, JsonNode.Parse(json)!.AsObject(), null, snapshot);

    [Fact]
    public void TimingData_MergesRatherThanReplacing()
    {
        var acc = new StateAccumulator();
        acc.Apply(Update(Topics.TimingData, """{"Lines":{"1":{"Position":"1"},"44":{"Position":"2"}}}"""));
        acc.Apply(Update(Topics.TimingData, """{"Lines":{"44":{"Position":"1"}}}"""));

        var lines = acc[Topics.TimingData]!["Lines"]!;
        Assert.Equal("1", (string?)lines["1"]!["Position"]);
        Assert.Equal("1", (string?)lines["44"]!["Position"]);
    }

    [Fact]
    public void Position_ReplacesSoStaleCarsDoNotAccumulate()
    {
        // Only the newest frame matters. If Position merged, a car that dropped
        // out of the feed would sit on the map forever and the heap would grow
        // across a two-hour race.
        var acc = new StateAccumulator();
        acc.Apply(Update(Topics.Position, """{"Position":{"0":{"Entries":{"1":{"X":1},"44":{"X":2}}}}}"""));
        acc.Apply(Update(Topics.Position, """{"Position":{"0":{"Entries":{"1":{"X":9}}}}}"""));

        var entries = acc[Topics.Position]!["Position"]!["0"]!["Entries"]!.AsObject();
        Assert.Single(entries);
        Assert.Equal(9, (int?)entries["1"]!["X"]);
    }

    [Fact]
    public void SessionInfo_ReplacesWholesale()
    {
        var acc = new StateAccumulator();
        acc.Apply(Update(Topics.SessionInfo, """{"Key":1,"Name":"Practice 1","Type":"Practice"}"""));
        acc.Apply(Update(Topics.SessionInfo, """{"Key":2,"Name":"Race"}"""));

        Assert.Equal(2, (int?)acc[Topics.SessionInfo]!["Key"]);
        Assert.Null(acc[Topics.SessionInfo]!["Type"]);
    }

    [Fact]
    public void RaceControlMessages_AppendAndNeverOverwriteAnEarlierMessage()
    {
        var acc = new StateAccumulator();
        acc.Apply(Update(Topics.RaceControlMessages,
            """{"Messages":{"0":{"Message":"GREEN"},"1":{"Message":"YELLOW"}}}""", snapshot: true));

        // A delta re-uses index 0 — as the feed does when it sends a bare array.
        acc.Apply(Update(Topics.RaceControlMessages, """{"Messages":{"0":{"Message":"SAFETY CAR"}}}"""));

        var messages = acc[Topics.RaceControlMessages]!["Messages"]!.AsObject();
        Assert.Equal(3, messages.Count);
        Assert.Equal("GREEN", (string?)messages["0"]!["Message"]);      // preserved
        Assert.Equal("YELLOW", (string?)messages["1"]!["Message"]);     // preserved
        Assert.Equal("SAFETY CAR", (string?)messages["2"]!["Message"]); // appended
    }

    [Fact]
    public void RaceControlMessages_DeltaWithANewIndexKeepsThatIndex()
    {
        var acc = new StateAccumulator();
        acc.Apply(Update(Topics.RaceControlMessages, """{"Messages":{"0":{"Message":"GREEN"}}}""", snapshot: true));
        acc.Apply(Update(Topics.RaceControlMessages, """{"Messages":{"7":{"Message":"RED"}}}"""));

        Assert.Equal("RED", (string?)acc[Topics.RaceControlMessages]!["Messages"]!["7"]!["Message"]);
    }

    [Fact]
    public void Apply_ReturnsADeltaShapedForTheClientMerge()
    {
        var acc = new StateAccumulator();
        var delta = acc.Apply(Update(Topics.TrackStatus, """{"Status":"2","Message":"Yellow"}"""));

        Assert.NotNull(delta);
        Assert.Equal("2", (string?)delta![Topics.TrackStatus]!["Status"]);

        // The delta must merge cleanly into a client's own state with the same
        // algorithm — that is the entire contract between server and browser.
        var clientState = new JsonObject();
        DeltaMerge.Merge(clientState, delta);
        Assert.Equal("2", (string?)clientState[Topics.TrackStatus]!["Status"]);
    }

    [Fact]
    public void Snapshot_IsIndependentOfLaterMutation()
    {
        var acc = new StateAccumulator();
        acc.Apply(Update(Topics.TrackStatus, """{"Status":"1"}"""));
        var snapshot = acc.Snapshot();
        acc.Apply(Update(Topics.TrackStatus, """{"Status":"5"}"""));

        Assert.Equal("1", (string?)snapshot[Topics.TrackStatus]!["Status"]);
    }

    [Fact]
    public void UnknownTopic_IsStoredRatherThanDropped()
    {
        // docs/13: never drop an unrecognised topic. A feed change must surface,
        // not vanish.
        var acc = new StateAccumulator();
        acc.Apply(Update("SomeBrandNewTopic", """{"Value":1}"""));

        Assert.Equal(1, (int?)acc["SomeBrandNewTopic"]!["Value"]);
    }
}
