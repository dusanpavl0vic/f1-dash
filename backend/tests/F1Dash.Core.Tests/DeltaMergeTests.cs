using System.Text.Json;
using System.Text.Json.Nodes;
using F1Dash.Core.Merge;
using Xunit;

namespace F1Dash.Core.Tests;

/// <summary>
/// The four cases named in docs/06 plus the fixture-driven suite. The fixtures
/// are shared byte-for-byte with the TypeScript merge tests: if the two
/// implementations diverge, one of the two suites fails.
/// </summary>
public class DeltaMergeTests
{
    private static JsonObject Obj(string json) =>
        JsonNode.Parse(json)!.AsObject();

    private static string Canonical(JsonNode? node) =>
        node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";

    // ---- the four cases docs/06 names explicitly -------------------------

    [Fact]
    public void SparseSectorPatch_LeavesOtherSectorsUntouched()
    {
        var state = Obj("""
            {"Lines":{"44":{"Sectors":{"0":{"Value":"28.1"},"1":{"Value":"31.2"},"2":{"Value":"24.0"}}}}}
            """);

        DeltaMerge.Merge(state, Obj("""{"Lines":{"44":{"Sectors":{"1":{"Value":"30.9"}}}}}"""));

        var sectors = state["Lines"]!["44"]!["Sectors"]!;
        Assert.Equal("28.1", (string?)sectors["0"]!["Value"]);  // untouched
        Assert.Equal("30.9", (string?)sectors["1"]!["Value"]);  // updated
        Assert.Equal("24.0", (string?)sectors["2"]!["Value"]);  // untouched
    }

    [Fact]
    public void SingleDriverPatch_DoesNotWipeOtherDrivers()
    {
        var state = Obj("""{"Lines":{"1":{"Position":"1"},"44":{"Position":"2"}}}""");

        DeltaMerge.Merge(state, Obj("""{"Lines":{"44":{"Position":"1"}}}"""));

        Assert.Equal("1", (string?)state["Lines"]!["1"]!["Position"]);
    }

    [Fact]
    public void KeyframeMarker_IsStripped()
    {
        var state = new JsonObject();

        DeltaMerge.Merge(state, Obj("""{"Status":"1","_kf":true}"""));

        Assert.False(state.ContainsKey("_kf"));
        Assert.Equal("1", (string?)state["Status"]);
    }

    [Fact]
    public void DeletedKey_IsRemoved()
    {
        var state = Obj("""{"Lines":{"1":{},"44":{}}}""");

        DeltaMerge.Merge(state, Obj("""{"Lines":{"_deleted":["1"]}}"""));

        Assert.False(state["Lines"]!.AsObject().ContainsKey("1"));
        Assert.True(state["Lines"]!.AsObject().ContainsKey("44"));
    }

    // ---- properties that hold for every patch ---------------------------

    [Fact]
    public void Merge_IsIdempotent_ForARepeatedValue()
    {
        // The reconnect path deliberately replays deltas that may already have
        // been applied. Duplicating a delta must be harmless; missing one is not.
        var once = Obj("""{"Lines":{"44":{"Position":"2"}}}""");
        var twice = Obj("""{"Lines":{"44":{"Position":"2"}}}""");
        var patch = """{"Lines":{"44":{"Position":"1","Sectors":{"0":{"Value":"28.1"}}}}}""";

        DeltaMerge.Merge(once, Obj(patch));
        DeltaMerge.Merge(twice, Obj(patch));
        DeltaMerge.Merge(twice, Obj(patch));

        Assert.Equal(Canonical(once), Canonical(twice));
    }

    [Fact]
    public void Merge_DoesNotAliasNodesFromThePatch()
    {
        // A JsonNode may have only one parent. Assigning without cloning either
        // throws or silently detaches the patch's own subtree, and the bug only
        // shows up when the same delta is applied twice.
        var state = new JsonObject();
        var patch = Obj("""{"Lines":{"44":{"Position":"1"}}}""");

        DeltaMerge.Merge(state, patch);
        state["Lines"]!["44"]!["Position"] = "9";

        Assert.Equal("1", (string?)patch["Lines"]!["44"]!["Position"]);
    }

    [Fact]
    public void Normalise_TurnsATopLevelArrayIntoAnIndexKeyedObject()
    {
        var result = DeltaMerge.Normalise(JsonNode.Parse("""[{"a":1},{"a":2}]"""));

        Assert.Equal(2, result.Count);
        Assert.Equal(1, (int?)result["0"]!["a"]);
        Assert.Equal(2, (int?)result["1"]!["a"]);
    }

    // ---- the shared fixture suite ---------------------------------------

    public static TheoryData<string> FixtureFiles()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixtureDirectory, "*.json").Order())
        {
            data.Add(Path.GetFileName(path));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void SharedFixture_ProducesTheExpectedState(string fileName)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, fileName)))!.AsObject();

        var state = fixture["initial"]!.DeepClone().AsObject();
        DeltaMerge.Merge(state, fixture["patch"]!.AsObject());

        Assert.Equal(Canonical(fixture["expected"]), Canonical(state));
    }

    [Fact]
    public void SharedFixtures_AreActuallyPresent()
    {
        // Guards against the suite silently passing with zero fixtures if the
        // path resolution breaks — a green build that tested nothing.
        Assert.True(Directory.EnumerateFiles(FixtureDirectory, "*.json").Count() >= 9);
    }

    /// <summary>
    /// shared-fixtures/ sits at the repository root so both language suites read
    /// the same bytes. Walk up until it is found rather than hardcoding a depth,
    /// which differs between `dotnet test` and the IDE.
    /// </summary>
    private static string FixtureDirectory { get; } = ResolveFixtureDirectory();

    private static string ResolveFixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "shared-fixtures", "merge");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "shared-fixtures/merge not found walking up from " + AppContext.BaseDirectory);
    }
}
