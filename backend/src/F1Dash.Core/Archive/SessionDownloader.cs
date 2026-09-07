using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1Dash.Core.Archive;

/// <summary>Progress callback: stage name, completed units, total units.</summary>
public delegate void DownloadProgress(string stage, int done, int total);

/// <summary>
/// Downloads every topic of one archived session and merges them into a single
/// time-ordered stream — the artefact the simulator, the replay engine and the
/// classification test all consume (docs/12 Phase 1).
/// </summary>
public sealed class SessionDownloader(ArchiveClient client)
{
    /// <summary>
    /// The topics worth downloading. Others exist in the archive but carry
    /// nothing the dashboard renders, and each one is a multi-megabyte fetch.
    /// </summary>
    private static readonly string[] Wanted =
    [
        Topics.SessionInfo, Topics.SessionStatus, Topics.SessionData,
        Topics.DriverList, Topics.TimingData, Topics.TimingAppData, Topics.TimingStats,
        Topics.TrackStatus, Topics.RaceControlMessages, Topics.WeatherData,
        Topics.LapCount, Topics.ExtrapolatedClock, Topics.TopThree,
        Topics.CurrentTyres, Topics.OvertakeSeries, Topics.PitStop,
        "Position.z", "CarData.z",
    ];

    public async Task<List<StreamEntry>> DownloadAsync(
        string sessionPath,
        DownloadProgress? progress = null,
        bool includeTelemetry = true,
        CancellationToken ct = default)
    {
        var feeds = await client.ListFeedsAsync(sessionPath, ct).ConfigureAwait(false);

        var topics = Wanted
            .Where(t => feeds.ContainsKey(t))
            .Where(t => includeTelemetry || (t != "Position.z" && t != "CarData.z"))
            .ToList();

        var all = new List<StreamEntry>();
        for (var i = 0; i < topics.Count; i++)
        {
            var topic = topics[i];
            progress?.Invoke($"downloading {topic}", i, topics.Count);

            try
            {
                all.AddRange(await client
                    .DownloadTopicAsync(sessionPath, topic, feeds[topic], ct)
                    .ConfigureAwait(false));
            }
            catch (HttpRequestException)
            {
                // A missing or broken topic file must not abort the session.
                // Sessions from 2018–2019 are missing several of these.
                progress?.Invoke($"skipped {topic}", i, topics.Count);
            }
        }

        progress?.Invoke("ordering", topics.Count, topics.Count);

        // A stable sort matters: several topics legitimately share an offset,
        // and their relative order is the order the feed produced them.
        return [.. all.OrderBy(e => e.OffsetMs)];
    }

    /// <summary>
    /// Writes the merged stream as line-delimited JSON:
    /// {"o": offsetMs, "t": "Topic", "p": { ... }}
    /// </summary>
    public static async Task WriteJsonlAsync(IEnumerable<StreamEntry> entries, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = File.Create(path);
        await using var writer = new StreamWriter(file);

        foreach (var entry in entries)
        {
            var line = new JsonObject
            {
                ["o"] = entry.OffsetMs,
                ["t"] = entry.Topic,
                ["p"] = entry.Payload.DeepClone(),
            };
            await writer.WriteLineAsync(line.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reads a stream back from disk.</summary>
    public static IEnumerable<StreamEntry> ReadJsonl(string path)
    {
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            if (JsonNode.Parse(line) is not JsonObject o) continue;
            if (o["p"] is not JsonObject payload) continue;

            yield return new StreamEntry(
                (long?)o["o"] ?? 0,
                (string?)o["t"] ?? "",
                payload);
        }
    }
}
