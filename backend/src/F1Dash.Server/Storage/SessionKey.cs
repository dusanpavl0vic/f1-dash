using F1Dash.Core.Analysis;
using F1Dash.Server.Ingest;

namespace F1Dash.Server.Storage;

/// <summary>
/// The natural key for a session, shared by all three stores.
///
/// Postgres, Influx and Mongo do not participate in one transaction, so they
/// need a key each can compute independently from the same source. This is it:
/// derived from the archive path, never from a database-assigned id.
/// </summary>
public readonly record struct SessionKey(int Year, string MeetingSlug, string SessionSlug)
{
    public override string ToString() => $"{Year}/{MeetingSlug}/{SessionSlug}";

    public static SessionKey From(AnalysisMeta meta) => new(
        meta.Year ?? 0,
        SessionManager.Slug(meta.Meeting),
        SessionManager.Slug(meta.SessionName));

    public static SessionKey From(int year, string meeting, string session) =>
        new(year, SessionManager.Slug(meeting), SessionManager.Slug(session));
}
