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
    public void A_session_epoch_is_the_same_in_every_process()
    {
        // Pinned to a literal, not compared to itself. The original version
        // used string.GetHashCode(), which .NET randomises PER PROCESS — so a
        // same-process comparison passed while every restart silently wrote a
        // duplicate copy of the telemetry at a new point on the time axis.
        // Only a fixed expected value can catch that.
        Assert.Equal(1767277221171, InfluxStore.EpochForSession(
            new SessionKey(2026, "italian-grand-prix", "race")));
    }

    [Fact]
    public void Re_indexing_lands_on_exactly_the_same_epoch()
    {
        var key = new SessionKey(2024, "monaco", "race");
        Assert.Equal(InfluxStore.EpochForSession(key), InfluxStore.EpochForSession(key));
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

public class InfluxCsvTests
{
    /// <summary>
    /// Influx returns annotated CSV whose column ORDER is not fixed between
    /// queries. Reading fields by hard-coded index works until the query
    /// changes, and then returns the wrong field rather than failing.
    /// </summary>
    [Fact]
    public void Fields_are_read_by_name_not_position()
    {
        const string csv = """
            #datatype,string,long,long,string,string,string
            #group,false,false,false,true,true,true
            #default,_result,,,,,
            ,result,table,_value,driver,lap,session
            ,,0,351,HAM,1,2026/italian-grand-prix/race
            ,,0,350,NOR,52,2026/italian-grand-prix/race
            """;

        var rows = InsightsService.ParseSpeedCsv(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal("HAM", rows[0].Driver);
        Assert.Equal(351, rows[0].TopSpeed);
        Assert.Equal(1, rows[0].Lap);
        Assert.Equal("2026/italian-grand-prix/race", rows[0].Session);
    }

    [Fact]
    public void A_different_column_order_still_parses()
    {
        const string csv = """
            ,result,table,session,driver,_value,lap
            ,,0,2024/monza/race,VER,340,7
            """;

        var rows = InsightsService.ParseSpeedCsv(csv);

        Assert.Single(rows);
        Assert.Equal("VER", rows[0].Driver);
        Assert.Equal(340, rows[0].TopSpeed);
        Assert.Equal(7, rows[0].Lap);
    }

    [Fact]
    public void Annotation_lines_and_blanks_are_ignored()
    {
        const string csv = "#datatype,string\n\n,result,table,_value,driver\n,,0,300,HAM\n\n";

        Assert.Single(InsightsService.ParseSpeedCsv(csv));
    }

    [Fact]
    public void An_empty_result_is_an_empty_list_not_a_crash()
    {
        // A query matching nothing is the normal case for a fresh install.
        Assert.Empty(InsightsService.ParseSpeedCsv(""));
        Assert.Empty(InsightsService.ParseSpeedCsv("#datatype,string\n"));
    }
}
