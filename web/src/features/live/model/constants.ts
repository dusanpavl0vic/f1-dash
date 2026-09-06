import type { PaceClass, TyreCompound, TrackState } from "./types";

/**
 * Team colours arrive from the feed at runtime (DriverList.TeamColour, hex
 * without a leading '#') and change between and during seasons. These are a
 * development fallback only — never a source of truth.
 *
 * The `awkward` palette exists because the design brief requires the UI to
 * survive an arbitrary palette: two near-identical blues, a near-white, a
 * near-black and one missing colour. Toggle it to prove the layout holds.
 */
export const TEAM_COLORS_CURRENT: Record<string, string> = {
  RBR: "#3671C6", FER: "#E8002D", MER: "#27F4D2", MCL: "#FF8000", AST: "#229971",
  ALP: "#0093CC", WIL: "#64C4FF", RB: "#6692FF", SAU: "#52E252", HAA: "#B6BABD",
};

export const TEAM_COLORS_AWKWARD: Record<string, string | null> = {
  RBR: "#2f5fd0", FER: "#3462d6", MER: "#f4f4f0", MCL: "#0e1014", AST: "#3d6ad8",
  ALP: null, WIL: "#8a8f95", RB: "#2f5fd0", SAU: "#f0efe8", HAA: "#101216",
};

/** Used when the feed has not yet delivered a colour for a driver. */
export const TEAM_COLOR_FALLBACK = "var(--neutral)";

export const TEAM_NAMES: Record<string, string> = {
  RBR: "Red Bull Racing", FER: "Ferrari", MER: "Mercedes", MCL: "McLaren",
  AST: "Aston Martin", ALP: "Alpine", WIL: "Williams", RB: "RB",
  SAU: "Kick Sauber", HAA: "Haas",
};

/** Tyre compound colours are FIA brand colours. They are never adjusted. */
export const TYRE: Record<TyreCompound, { bg: string; fg: string; name: string }> = {
  S: { bg: "var(--tyre-soft)",   fg: "var(--tyre-soft-fg)",   name: "SOFT" },
  M: { bg: "var(--tyre-medium)", fg: "var(--tyre-medium-fg)", name: "MEDIUM" },
  H: { bg: "var(--tyre-hard)",   fg: "var(--tyre-hard-fg)",   name: "HARD" },
  I: { bg: "var(--tyre-inter)",  fg: "var(--tyre-inter-fg)",  name: "INTER" },
  W: { bg: "var(--tyre-wet)",    fg: "var(--tyre-wet-fg)",    name: "WET" },
};

/** Pace colour is sport vocabulary, not a style choice. See docs/05. */
export const PACE_COLOR: Record<PaceClass, string> = {
  overall:  "var(--purple)",
  personal: "var(--green)",
  slower:   "var(--yellow)",
  pit:      "var(--blue)",
  none:     "var(--surface-4)",
};

/**
 * Session state drives the header chip, the toast and the marshal-sector
 * overlay. Every entry carries a text label — track status must never be
 * conveyed by colour alone.
 */
export const TRACK_STATE: Record<
  TrackState,
  { label: string; fg: string; bg: string; bd: string; marshal: string }
> = {
  "none":        { label: "Green flag",   fg: "var(--green)",      bg: "var(--green-bg)",   bd: "var(--green-bd)",   marshal: "var(--wash-marshal-none)" },
  "yellow":      { label: "Yellow flag",  fg: "var(--yellow)",     bg: "var(--yellow-bg)",  bd: "var(--yellow-bd)",  marshal: "var(--wash-marshal-yellow)" },
  "safety-car":  { label: "Safety car",   fg: "var(--yellow)",     bg: "var(--yellow-bg)",  bd: "var(--yellow-bd)",  marshal: "var(--wash-marshal-yellow)" },
  "vsc":         { label: "VSC deployed", fg: "var(--yellow)",     bg: "var(--yellow-bg)",  bd: "var(--yellow-bd)",  marshal: "var(--wash-marshal-yellow)" },
  "red-flag":    { label: "Red flag",     fg: "var(--red-bright)", bg: "var(--red-bg)",     bd: "var(--red-bd)",     marshal: "var(--wash-marshal-red)" },
  "chequered":   { label: "Chequered",    fg: "var(--text)",       bg: "var(--neutral-bg)", bd: "var(--neutral-bd)", marshal: "var(--wash-marshal-none)" },
};

export const DRIVER_STATUS_LABEL = {
  racing:  { label: "RACING",  fg: "var(--text-fainter)" },
  lapped:  { label: "LAPPED",  fg: "var(--blue-lighter)" },
  pit:     { label: "IN PIT",  fg: "var(--blue-lighter)" },
  outlap:  { label: "OUT LAP", fg: "var(--yellow)" },
  retired: { label: "RETIRED", fg: "var(--red-bright)" },
} as const;

/** Tyre age thresholds that drive the age colour in the tower. */
export const TYRE_AGE_WARN = 14;
export const TYRE_AGE_CRITICAL = 20;
