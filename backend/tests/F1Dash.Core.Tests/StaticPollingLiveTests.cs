using F1Dash.Core.Archive;
using F1Dash.Server.Ingest;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace F1Dash.Core.Tests;

/// <summary>
/// Points the poller at F1's real static archive. Opt-in via F1_LIVE_TEST=1,
/// because it depends on the network and on whatever session F1 currently
/// advertises.
///
/// The fake-handler tests prove the chunking logic; this proves the thing the
/// fake cannot — that the real server honours Range and that the real files
/// parse.
/// </summary>
public class StaticPollingLiveTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Reads_the_session_F1_is_advertising_now()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("F1_LIVE_TEST") == "1",
            "Set F1_LIVE_TEST=1 to run against F1's real archive.");

        using var http = ArchiveClient.CreateHttpClient();
        var source = new StaticPollingSource(http, NullLogger.Instance, TimeSpan.FromMilliseconds(200));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var topics = new Dictionary<string, int>(StringComparer.Ordinal);
        var count = 0;

        await foreach (var update in source.ReadAsync(cts.Token))
        {
            topics[update.Topic] = topics.GetValueOrDefault(update.Topic) + 1;
            if (++count >= 4000) break;
        }

        foreach (var (topic, n) in topics.OrderByDescending(t => t.Value))
        {
            output.WriteLine($"  {topic,-24} {n,6}");
        }

        Assert.True(count > 0, "The poller returned nothing from the real archive.");
        Assert.True(topics.Count > 1, "Only one topic was read; the feed list is probably wrong.");
    }
}
