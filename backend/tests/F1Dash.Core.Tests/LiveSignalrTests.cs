using F1Dash.Core.Signalr;
using F1Dash.Server.Ingest;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace F1Dash.Core.Tests;

/// <summary>
/// Touches the real F1 endpoint, so it is opt-in via F1_LIVE_TEST=1 and never
/// runs in CI. It exists because the SignalR handshake is the riskiest part of
/// this project (DECISIONS D-001) and a mocked version would only prove the
/// mock is right.
/// </summary>
public class LiveSignalrTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("F1_LIVE_TEST") == "1";
    private static string? Proxy => Environment.GetEnvironmentVariable("F1_HTTP_PROXY");

    [SkippableFact]
    public async Task Negotiate_ReturnsAConnectionTokenAndTheLoadBalancerCookie()
    {
        Skip.IfNot(Enabled, "Set F1_LIVE_TEST=1 to run against the live F1 endpoint.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (token, cookie) = await LiveSignalrSource.NegotiateAsync(cts.Token, Proxy);

        output.WriteLine($"token  : {token}");
        output.WriteLine($"cookie : {(cookie.Length > 0 ? cookie[..Math.Min(48, cookie.Length)] + "..." : "(none)")}");

        Assert.NotEmpty(token);
        // AWSALB pins the connection to one load-balancer node; without it the
        // upgrade can land on a node that has never seen the token.
        Assert.Contains("AWSALB", cookie, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Connect_CompletesTheHandshakeAndReceivesFrames()
    {
        Skip.IfNot(Enabled, "Set F1_LIVE_TEST=1 to run against the live F1 endpoint.");

        var source = new LiveSignalrSource(Proxy, NullLogger<LiveSignalrSource>.Instance);

        // Out of session the feed is quiet, so this proves the handshake and the
        // subscription, not that a race is running.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        var topics = new List<string>();
        try
        {
            await foreach (var update in source.ReadAsync(cts.Token))
            {
                topics.Add(update.Topic);
                if (topics.Count >= 40) break;
            }
        }
        catch (OperationCanceledException)
        {
            // The window elapsed; whatever arrived is the result.
        }

        output.WriteLine($"{topics.Count} updates: {string.Join(", ", topics.Distinct().Order())}");

        Assert.NotEmpty(topics);
    }

    [Fact]
    public void RecordSeparator_IsTheAsciiUnitOfTheCoreProtocol()
    {
        // Offline guard: an editor or a copy-paste that mangles this byte breaks
        // every frame silently, and the failure looks like a dead feed.
        Assert.Equal(0x1E, (int)CoreFrameParser.RecordSeparator);
    }
}
