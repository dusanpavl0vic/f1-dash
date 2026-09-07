using F1Dash.Core;
using F1Dash.Core.Analysis;
using F1Dash.Core.Archive;
using F1Dash.Core.Merge;
using F1Dash.Core.Signalr;
using F1Dash.Server.Ingest;

namespace F1Dash.Server.Catalog;

/// <summary>
/// Builds a finished session's analysis by reading its stream at full speed.
///
/// A finished session does not need replaying: the stream is on disk and the
/// pacing buys nothing. Reading the whole 2024 Monza race through the
/// accumulator takes about 1.5 seconds, against minutes even at 50x through the
/// paced ingest loop.
///
/// Live sessions are the opposite case and are recorded as they stream, because
/// there is no second chance.
/// </summary>
public sealed class AnalysisPrecomputer(ArchiveClient archive, string archiveRoot, ILogger<AnalysisPrecomputer> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public sealed record Result(bool Ok, string? Error, int Drivers, int TotalLaps, bool FromCache, string? Path);

    public string SessionDirectory(int year, string meeting, string session) =>
        Path.Combine(archiveRoot, year.ToString(), SessionManager.Slug(meeting), SessionManager.Slug(session));

    /// <summary>Loads a stored analysis without computing anything.</summary>
    public Task<SessionAnalysis?> LoadAsync(int year, string meeting, string session, CancellationToken ct) =>
        new AnalysisStore(SessionDirectory(year, meeting, session)).LoadAsync(ct);

    public async Task<Result> PrecomputeAsync(
        int year, string meeting, string session, bool force, CancellationToken ct)
    {
        var directory = SessionDirectory(year, meeting, session);
        var store = new AnalysisStore(directory);

        if (!force && store.Exists)
        {
            var cached = await store.LoadAsync(ct).ConfigureAwait(false);
            if (cached is not null)
            {
                return new Result(true, null, cached.Drivers.Count, cached.Meta.TotalLaps, true, store.Directory);
            }
        }

        // One at a time: two concurrent precomputes of the same session would
        // race on the same output files, and of different sessions would just
        // contend for the disk.
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var streamPath = Path.Combine(directory, "stream.jsonl");

            if (!File.Exists(streamPath))
            {
                var downloaded = await DownloadAsync(year, meeting, session, directory, ct).ConfigureAwait(false);
                if (!downloaded) return new Result(false, $"No archived session matched {year} {meeting} {session}.", 0, 0, false, null);
            }

            var started = DateTime.UtcNow;
            var accumulator = new StateAccumulator();
            var builder = new AnalysisBuilder();

            // Telemetry is captured here too, for 2026 onward, so a finished
            // 2026 session has traces without ever being replayed.
            using var telemetry = new TelemetryRecorder(store.TelemetryDirectory, year);

            foreach (var entry in SessionDownloader.ReadJsonl(streamPath))
            {
                ct.ThrowIfCancellationRequested();

                accumulator.Apply(new TopicUpdate(entry.Topic, entry.Payload, null, IsSnapshot: false));
                builder.Observe(accumulator, entry.Topic);
                telemetry.Observe(accumulator, entry.Topic);
            }

            var info = accumulator[Topics.SessionInfo];
            var analysis = builder.Build(accumulator, new AnalysisMeta(
                Year: year,
                Meeting: (string?)info?["Meeting"]?["Name"] ?? meeting,
                SessionName: (string?)info?["Name"] ?? session,
                SessionType: (string?)info?["Type"] ?? "",
                Circuit: (string?)info?["Meeting"]?["Circuit"]?["ShortName"] ?? "",
                CircuitKey: (int?)info?["Meeting"]?["Circuit"]?["Key"],
                StartDate: (string?)info?["StartDate"],
                TotalLaps: 0,
                HasTelemetry: telemetry.Enabled,
                RecordedAtUtc: DateTime.UtcNow.ToString("O")));

            await store.SaveAsync(analysis, ct).ConfigureAwait(false);

            logger.LogInformation(
                "Precomputed {Year} {Meeting} {Session}: {Drivers} drivers, {Laps} laps, {Telemetry} telemetry laps in {Seconds:F1}s",
                year, meeting, session, analysis.Drivers.Count, analysis.Meta.TotalLaps,
                telemetry.LapsWritten, (DateTime.UtcNow - started).TotalSeconds);

            return new Result(true, null, analysis.Drivers.Count, analysis.Meta.TotalLaps, false, store.Directory);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<bool> DownloadAsync(
        int year, string meeting, string session, string directory, CancellationToken ct)
    {
        var sessions = await archive.ListSessionsAsync(year, ct).ConfigureAwait(false);

        var match = sessions.FirstOrDefault(s =>
            SessionManager.Slug(s.MeetingName) == SessionManager.Slug(meeting)
            && SessionManager.Slug(s.SessionName) == SessionManager.Slug(session));

        if (match is null) return false;

        var entries = await new SessionDownloader(archive)
            .DownloadAsync(match.Path, null, includeTelemetry: true, ct).ConfigureAwait(false);

        await SessionDownloader.WriteJsonlAsync(entries, Path.Combine(directory, "stream.jsonl"), ct)
            .ConfigureAwait(false);

        return true;
    }
}
