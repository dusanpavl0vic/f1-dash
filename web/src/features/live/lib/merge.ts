/**
 * The delta merge — a port of backend/src/F1Dash.Core/Merge/DeltaMerge.cs.
 *
 * Both implementations run against the SAME fixtures in shared-fixtures/merge/,
 * and CI fails if they disagree. That parity is the only practical guard
 * against the most insidious failure available to a delta-based system: if the
 * two merges diverge, the browser shows something different from the server
 * while both stay internally consistent. Nothing throws, nothing logs, and the
 * timing tower is simply wrong.
 *
 * Rules, identical to docs/06 and to the C#:
 *   1. Recursive merge, never replace. Keys absent from the patch keep their value.
 *   2. Numeric-string keys are sparse array indices. Unmentioned indices survive.
 *   3. `_kf` is stripped; `_deleted` removes the listed keys.
 *   4. A JSON array arriving where an index-keyed object is held merges by index.
 *   5. Scalars replace.
 */

export type JsonValue = string | number | boolean | null | JsonValue[] | { [k: string]: JsonValue };
export type JsonObject = { [k: string]: JsonValue };

const KEYFRAME_MARKER = "_kf";
const DELETED_MARKER = "_deleted";

function isPlainObject(value: JsonValue | undefined): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** Mutates `target` in place, exactly as the C# does. */
export function merge(target: JsonObject, patch: JsonObject): JsonObject {
  for (const key of Object.keys(patch)) {
    const value = patch[key] as JsonValue;

    // Rule 3 — a keyframe marker is protocol, not data.
    if (key === KEYFRAME_MARKER) continue;

    if (key === DELETED_MARKER) {
      applyDeletions(target, value);
      continue;
    }

    const existing = target[key];

    // Rule 1 — both sides are objects, so recurse.
    if (isPlainObject(value) && isPlainObject(existing)) {
      merge(existing, value);
      continue;
    }

    // Rule 4 — an array arrived where we hold an index-keyed object.
    if (Array.isArray(value) && isPlainObject(existing)) {
      mergeArrayByIndex(existing, value);
      continue;
    }

    // Rules 1 and 5 — a new key, a type change, or a scalar.
    // Cloned for the same reason the C# calls DeepClone: the patch keeps
    // ownership of its own subtree, and a shared reference means a later
    // mutation of the state silently rewrites the delta that produced it.
    target[key] = clone(value);
  }

  return target;
}

function mergeArrayByIndex(target: JsonObject, patch: JsonValue[]): void {
  for (let i = 0; i < patch.length; i++) {
    const index = String(i);
    const item = patch[i] as JsonValue;
    const existing = target[index];

    if (isPlainObject(item) && isPlainObject(existing)) {
      merge(existing, item);
    } else {
      target[index] = clone(item);
    }
  }
}

/**
 * Rule 3 — `_deleted` carries the keys to remove. The feed sends an array of
 * strings, but a bare string is accepted rather than dropped: silently ignoring
 * a deletion leaves a ghost driver on the timing tower for the rest of the
 * session.
 */
function applyDeletions(target: JsonObject, deleted: JsonValue): void {
  if (Array.isArray(deleted)) {
    for (const key of deleted) {
      if (typeof key === "string") delete target[key];
    }
  } else if (typeof deleted === "string") {
    delete target[deleted];
  }
}

function clone<T extends JsonValue>(value: T): T {
  if (value === null || typeof value !== "object") return value;
  return structuredClone(value);
}

/**
 * Normalises a topic payload before it reaches the accumulator: a top-level
 * array becomes an index-keyed object so rule 2 applies uniformly downstream.
 */
export function normalise(payload: JsonValue | undefined): JsonObject {
  if (isPlainObject(payload)) return payload;

  if (Array.isArray(payload)) {
    const result: JsonObject = {};
    for (let i = 0; i < payload.length; i++) result[String(i)] = clone(payload[i] as JsonValue);
    return result;
  }

  return {};
}
