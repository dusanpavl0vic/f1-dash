using System.Text.Json.Nodes;

namespace F1Dash.Core.Merge;

/// <summary>
/// The delta merge. This is the single most important piece of code in the
/// project: the F1 feed sends no periodic snapshots, only partial patches, so
/// every value the dashboard shows is the product of this function.
///
/// It operates on <see cref="JsonNode"/> rather than typed models on purpose. A
/// delta patches an arbitrarily nested subtree — one sector time inside one
/// driver inside TimingData.Lines — and a strongly typed merge would need
/// hand-written per-field code across roughly thirty models, which docs/00
/// rule 3 exists to forbid. Typed records remain the read-side projection.
///
/// Rules, verbatim from docs/06:
///   1. Recursive merge, never replace. Keys absent from the patch keep their value.
///   2. Numeric-string keys are sparse array indices. Unmentioned indices survive.
///   3. `_kf` is stripped; `_deleted` removes the listed keys.
///   4. A JSON array arriving where an index-keyed object is held merges by index.
///   5. Scalars replace.
///
/// The TypeScript port in web/src/features/live/lib/merge.ts mirrors this rule
/// for rule and is tested against the same fixtures. Divergence between the two
/// is the most insidious failure available to a delta-based system — both sides
/// stay internally consistent, nothing throws, and the tower is simply wrong.
/// </summary>
public static class DeltaMerge
{
    private const string KeyframeMarker = "_kf";
    private const string DeletedMarker = "_deleted";

    /// <summary>
    /// Merges <paramref name="patch"/> into <paramref name="target"/>, mutating
    /// the target in place.
    /// </summary>
    public static void Merge(JsonObject target, JsonObject patch)
    {
        // Snapshot the patch's entries: assigning into the target can detach
        // nodes, and mutating a collection while enumerating it throws.
        foreach (var (key, value) in patch.ToArray())
        {
            switch (key)
            {
                case KeyframeMarker:
                    // Rule 3 — a keyframe marker is protocol, not data.
                    continue;

                case DeletedMarker:
                    ApplyDeletions(target, value);
                    continue;
            }

            switch (value)
            {
                // Rule 1 — both sides are objects, so recurse.
                case JsonObject patchObject when target[key] is JsonObject targetObject:
                    Merge(targetObject, patchObject);
                    break;

                // Rule 4 — an array arrived where we hold an index-keyed object.
                case JsonArray patchArray when target[key] is JsonObject indexed:
                    MergeArrayByIndex(indexed, patchArray);
                    break;

                // Rules 1 and 5 — a new key, a type change, or a scalar.
                // DeepClone is required: a JsonNode may have only one parent,
                // and the patch keeps ownership of its own children.
                default:
                    target[key] = value?.DeepClone();
                    break;
            }
        }
    }

    /// <summary>
    /// Merges a genuine JSON array into an object keyed by stringified index, so
    /// downstream code only ever sees one shape. RaceControlMessages.Messages
    /// arrives both ways depending on the moment in the session.
    /// </summary>
    private static void MergeArrayByIndex(JsonObject target, JsonArray patch)
    {
        for (var i = 0; i < patch.Count; i++)
        {
            var index = i.ToString();
            var item = patch[i];

            if (item is JsonObject itemObject && target[index] is JsonObject existing)
            {
                Merge(existing, itemObject);
            }
            else
            {
                target[index] = item?.DeepClone();
            }
        }
    }

    /// <summary>
    /// Rule 3 — `_deleted` carries the keys to remove. The feed sends it as an
    /// array of strings, but a single string is accepted rather than dropped:
    /// silently ignoring a deletion leaves a ghost driver on the timing tower.
    /// </summary>
    private static void ApplyDeletions(JsonObject target, JsonNode? deleted)
    {
        switch (deleted)
        {
            case JsonArray keys:
                foreach (var key in keys)
                {
                    if (key?.GetValue<string>() is { } name)
                    {
                        target.Remove(name);
                    }
                }
                break;

            case JsonValue value when value.TryGetValue<string>(out var single):
                target.Remove(single);
                break;
        }
    }

    /// <summary>
    /// Normalises a topic payload before it reaches the accumulator: converts a
    /// top-level array into an index-keyed object so rule 2 applies uniformly.
    /// </summary>
    public static JsonObject Normalise(JsonNode? payload)
    {
        switch (payload)
        {
            case JsonObject o:
                return o;

            case JsonArray a:
            {
                var result = new JsonObject();
                for (var i = 0; i < a.Count; i++)
                {
                    result[i.ToString()] = a[i]?.DeepClone();
                }
                return result;
            }

            default:
                return new JsonObject();
        }
    }
}
