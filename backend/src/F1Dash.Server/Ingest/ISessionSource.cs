using F1Dash.Core.Signalr;

namespace F1Dash.Server.Ingest;

/// <summary>
/// A source of topic updates. The ingest loop cannot tell a replayed recording
/// from the live feed, which is the whole point: F1 runs on roughly 24 weekends
/// a year, so if the recorded path were a different code path it would be the
/// one that is actually tested and the live one would break on race day.
/// </summary>
public interface ISessionSource
{
    string Description { get; }

    IAsyncEnumerable<TopicUpdate> ReadAsync(CancellationToken ct);
}
