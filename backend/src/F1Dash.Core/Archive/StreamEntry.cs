using System.Text.Json.Nodes;

namespace F1Dash.Core.Archive;

/// <summary>
/// One archived topic update, positioned relative to the start of the session.
/// This is the unit the replay engine and the simulator both consume.
/// </summary>
/// <param name="OffsetMs">Milliseconds since session start.</param>
/// <param name="Topic">Normalised topic name — ".z" already stripped.</param>
/// <param name="Payload">Decompressed, array-normalised payload.</param>
public readonly record struct StreamEntry(long OffsetMs, string Topic, JsonObject Payload);
