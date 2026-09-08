using F1Dash.Core.Analysis;
using F1Dash.Server.Storage;
using Xunit;

namespace F1Dash.Core.Tests;

public class SessionKeyTests
{
    [Fact]
    public void The_key_is_derived_from_the_archive_path_shape()
    {
        // Three stores that share no transaction need a key each can compute
        // independently from the same source. Anything database-assigned would
        // not be that.
        var key = SessionKey.From(2024, "Italian Grand Prix", "Race");

        Assert.Equal(2024, key.Year);
        Assert.Equal("italian-grand-prix", key.MeetingSlug);
        Assert.Equal("race", key.SessionSlug);
        Assert.Equal("2024/italian-grand-prix/race", key.ToString());
    }

    [Fact]
    public void Punctuation_and_case_cannot_produce_two_keys_for_one_session()
    {
        Assert.Equal(
            SessionKey.From(2024, "São Paulo Grand Prix", "Sprint Qualifying"),
            SessionKey.From(2024, "sao--paulo grand   prix", "SPRINT QUALIFYING"));
    }

    [Fact]
    public void A_missing_year_does_not_throw()
    {
        // Older archived sessions have no year in their metadata. A key of
        // year 0 is wrong but recoverable; an exception during indexing would
        // abandon the whole backfill.
        var meta = new AnalysisMeta(
            null, "Italian Grand Prix", "Race", "Race", "Monza", 39, null, 53, false, "");

        Assert.Equal(0, SessionKey.From(meta).Year);
    }
}

public class InfluxLineProtocolTests
{
    /// <summary>
    /// Line protocol is whitespace- and comma-delimited, so an unescaped tag
    /// value silently becomes a different tag — or a parse error that Influx
    /// reports as a generic 400 with no indication of which line was wrong.
    /// </summary>
    [Theory]
    [InlineData("2024/italian-grand-prix/race", "2024/italian-grand-prix/race")]
    [InlineData("a,b", "a\\,b")]
    [InlineData("a b", "a\\ b")]
    [InlineData("a=b", "a\\=b")]
    [InlineData("Sao Paulo, 2024", "Sao\\ Paulo\\,\\ 2024")]
    public void Tag_values_are_escaped(string input, string expected)
    {
        Assert.Equal(expected, InfluxStore.EscapeTag(input));
    }

    [Fact]
    public void Session_epochs_do_not_collide()
    {
        // Two sessions sharing a point on the time axis would make a query for
        // one return the other's samples.
        var a = InfluxStore.EpochForSession(new SessionKey(2024, "italian-grand-prix", "race"));
        var b = InfluxStore.EpochForSession(new SessionKey(2024, "italian-grand-prix", "qualifying"));
        var c = InfluxStore.EpochForSession(new SessionKey(2026, "italian-grand-prix", "race"));

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void A_session_epoch_is_stable_across_runs()
    {
        // Re-indexing must overwrite the same points, not write a second copy
        // at a new offset.
        var first = InfluxStore.EpochForSession(new SessionKey(2026, "monaco", "race"));
        var second = InfluxStore.EpochForSession(new SessionKey(2026, "monaco", "race"));

        Assert.Equal(first, second);
    }
}

public class SlugTests
{
    [Theory]
    [InlineData("São Paulo Grand Prix", "sao-paulo-grand-prix")]
    [InlineData("Sao Paulo Grand Prix", "sao-paulo-grand-prix")]
    [InlineData("Nürburgring", "nurburgring")]
    [InlineData("México City", "mexico-city")]
    [InlineData("Türkiye", "turkiye")]
    [InlineData("Emilia-Romagna Grand Prix", "emilia-romagna-grand-prix")]
    public void Accents_fold_so_a_typed_name_matches(string input, string expected)
    {
        // The project builds with InvariantGlobalization, where
        // string.Normalize(FormD) silently returns the accented character
        // unchanged. An explicit fold is the only thing that works here, and
        // this test is what proved normalisation did not.
        Assert.Equal(expected, F1Dash.Server.Ingest.SessionManager.Slug(input));
    }

    [Fact]
    public void Runs_of_separators_collapse()
    {
        Assert.Equal("sprint-qualifying",
            F1Dash.Server.Ingest.SessionManager.Slug("  Sprint --- Qualifying  "));
    }
}
