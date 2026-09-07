/**
 * Development fixture — the dataset from the v3 design, ported to typed models.
 *
 * This exists so every component is developable and reviewable before the
 * backend exists. It is replaced wholesale by the WebSocket snapshot in
 * IMPL-36; nothing outside this folder may import it after that point.
 *
 * Source: design/handoff/.../F1 Live Dashboard v3.dc.html — the ROWS, CAR_POS,
 * messages, penalties, stints and timeline arrays in its script block.
 */
import { TEAM_COLORS_CURRENT, TEAM_COLORS_AWKWARD, TEAM_COLOR_FALLBACK, TEAM_NAMES } from "../model/constants";
import type {
  CarPosition, Driver, PaceClass, PaceLine, Penalty, RaceControlMessage,
  SectorTime, SessionSnapshot, Stint, TimelineBand, TimingRow, TrackState, TyreCompound,
} from "../model/types";

const NAMES: Record<string, [string, string]> = {
  VER: ["Max", "Verstappen"], NOR: ["Lando", "Norris"], LEC: ["Charles", "Leclerc"],
  PIA: ["Oscar", "Piastri"], SAI: ["Carlos", "Sainz"], RUS: ["George", "Russell"],
  HAM: ["Lewis", "Hamilton"], PER: ["Sergio", "Perez"], ALO: ["Fernando", "Alonso"],
  GAS: ["Pierre", "Gasly"], HUL: ["Nico", "Hulkenberg"], TSU: ["Yuki", "Tsunoda"],
  STR: ["Lance", "Stroll"], OCO: ["Esteban", "Ocon"], ALB: ["Alex", "Albon"],
  MAG: ["Kevin", "Magnussen"], RIC: ["Daniel", "Ricciardo"], BOT: ["Valtteri", "Bottas"],
  ZHO: ["Guanyu", "Zhou"], SAR: ["Logan", "Sargeant"],
};

/** tla, num, team, pos, gap, interval, s1, s2, s3, last, best, tyre, age, stops, status */
type Raw = [
  string, number, string, number, string, string,
  [string, PaceClass], [string, PaceClass], [string, PaceClass],
  string, string, TyreCompound, number, number, "" | "PIT" | "OUT" | "STOP",
];

const P = "overall" as const, G = "personal" as const, Y = "slower" as const, N = "none" as const;

const ROWS: Raw[] = [
  ["VER",  1, "RBR",  1, "",        "",        ["29.114", P], ["38.902", G], ["27.507", Y], "1:35.523", "1:34.918", "H", 12, 1, ""],
  ["NOR",  4, "MCL",  2, "+2.318",  "+2.318",  ["29.240", G], ["38.744", P], ["27.612", G], "1:35.596", "1:35.011", "H", 10, 1, ""],
  ["LEC", 16, "FER",  3, "+5.902",  "+3.584",  ["29.331", Y], ["38.901", G], ["27.418", P], "1:35.650", "1:35.104", "M",  8, 1, ""],
  ["PIA", 81, "MCL",  4, "+8.115",  "+2.213",  ["29.402", G], ["39.014", Y], ["27.744", G], "1:36.160", "1:35.298", "H", 14, 1, ""],
  ["SAI", 55, "FER",  5, "+11.483", "+3.368",  ["29.518", Y], ["39.102", Y], ["27.809", Y], "1:36.429", "1:35.401", "H", 21, 1, ""],
  ["RUS", 63, "MER",  6, "+15.207", "+3.724",  ["29.480", G], ["39.244", Y], ["27.902", G], "1:36.626", "1:35.512", "M",  6, 2, ""],
  ["HAM", 44, "MER",  7, "+18.636", "+3.429",  ["29.601", Y], ["39.188", G], ["27.988", Y], "1:36.777", "1:35.640", "M",  5, 2, ""],
  ["PER", 11, "RBR",  8, "+22.014", "+3.378",  ["29.744", Y], ["39.310", Y], ["28.104", Y], "1:37.158", "1:35.902", "H", 18, 1, ""],
  ["ALO", 14, "AST",  9, "+26.881", "+4.867",  ["29.902", Y], ["39.402", Y], ["28.211", G], "1:37.515", "1:36.088", "H", 20, 1, ""],
  ["GAS", 10, "ALP", 10, "+31.402", "+4.521",  ["30.014", Y], ["39.518", Y], ["28.302", Y], "1:37.834", "1:36.244", "M",  4, 2, ""],
  ["HUL", 27, "HAA", 11, "+36.744", "+5.342",  ["30.118", Y], ["39.602", Y], ["28.418", Y], "1:38.138", "1:36.501", "H", 16, 1, ""],
  ["TSU", 22, "RB",  12, "+41.203", "+4.459",  ["",       N], ["",       N], ["",       N], "—",        "1:36.712", "M",  1, 2, "PIT"],
  ["STR", 18, "AST", 13, "+45.618", "+4.415",  ["30.244", Y], ["39.744", Y], ["28.502", Y], "1:38.490", "1:36.804", "H", 19, 1, ""],
  ["OCO", 31, "ALP", 14, "+52.104", "+6.486",  ["30.318", Y], ["39.811", Y], ["28.614", Y], "1:38.743", "1:37.002", "H", 22, 1, ""],
  ["ALB", 23, "WIL", 15, "+58.902", "+6.798",  ["30.402", Y], ["39.902", Y], ["28.702", Y], "1:39.006", "1:37.214", "M",  3, 2, "OUT"],
  ["MAG", 20, "HAA", 16, "1 L",     "1 L",     ["30.518", Y], ["40.014", Y], ["28.811", Y], "1:39.343", "1:37.418", "H", 24, 1, ""],
  ["RIC",  3, "RB",  17, "1 L",     "+3.204",  ["30.602", Y], ["40.118", Y], ["28.902", Y], "1:39.622", "1:37.601", "H", 25, 1, ""],
  ["BOT", 77, "SAU", 18, "1 L",     "+5.118",  ["30.744", Y], ["40.244", Y], ["29.014", Y], "1:40.002", "1:37.844", "M",  9, 2, ""],
  ["ZHO", 24, "SAU", 19, "2 L",     "1 L",     ["30.902", Y], ["40.402", Y], ["29.118", Y], "1:40.422", "1:38.104", "H", 27, 1, ""],
  ["SAR",  2, "WIL", 20, "",        "",        ["",       N], ["",       N], ["",       N], "—",        "1:38.402", "H", 14, 1, "STOP"],
];

/** Positions on the 480×300 circuit viewBox used by TrackMap. */
const CAR_POS: [number, number, string][] = [
  [96, 178, "VER"], [96, 212, "NOR"], [96, 240, "LEC"], [104, 264, "PIA"],
  [130, 272, "SAI"], [186, 272, "RUS"], [242, 272, "HAM"], [300, 272, "PER"],
  [346, 272, "ALO"], [384, 214, "GAS"], [384, 168, "HUL"], [342, 122, "STR"],
  [278, 176, "OCO"], [228, 176, "ALB"], [188, 76, "MAG"], [136, 76, "RIC"],
  [110, 196, "TSU"], [268, 120, "BOT"], [384, 126, "ZHO"],
];

/**
 * Mini-sector segments. The real feed delivers these per driver; here they are
 * generated deterministically so the fixture is stable across reloads — a
 * flickering fixture makes visual review impossible.
 */
function segmentsFor(index: number, status: Raw[14]): PaceClass[] {
  const out: PaceClass[] = [];
  const driven = status === "STOP" ? 0 : 20 - (index % 7);
  for (let s = 0; s < 20; s++) {
    if (status === "PIT" || status === "OUT") { out.push(s < driven ? "pit" : "none"); continue; }
    if (s >= driven) { out.push("none"); continue; }
    const seed = (index * 7 + s * 13) % 11;
    if (index < 3 && seed > 8) out.push("overall");
    else if (seed > 6) out.push("personal");
    else if (seed > 2) out.push("slower");
    else out.push("personal");
  }
  return out;
}

export type PaletteMode = "current" | "awkward";

function teamColor(team: string, palette: PaletteMode): string {
  const table = palette === "awkward" ? TEAM_COLORS_AWKWARD : TEAM_COLORS_CURRENT;
  return table[team] ?? TEAM_COLOR_FALLBACK;
}

function sector([value, pace]: [string, PaceClass]): SectorTime {
  return { value, pace };
}

export function buildMockSession(
  trackState: TrackState = "yellow",
  palette: PaletteMode = "current",
): SessionSnapshot {
  const drivers: Record<string, Driver> = {};
  for (const r of ROWS) {
    const [first, last] = NAMES[r[0]] ?? [r[0], ""];
    drivers[r[0]] = {
      tla: r[0], number: r[1], teamCode: r[2],
      color: teamColor(r[2], palette),
      teamName: TEAM_NAMES[r[2]] ?? r[2],
      firstName: first, lastName: last,
    };
  }

  const timing: TimingRow[] = ROWS.map((r, i) => {
    const status = r[14];
    const lapped = r[4].includes("L");
    return {
      tla: r[0],
      position: r[3],
      gap: r[4],
      interval: r[5],
      sectors: [sector(r[6]), sector(r[7]), sector(r[8])],
      segments: segmentsFor(i, status),
      lastLap: r[9],
      bestLap: r[10],
      bestIsOverall: i === 0,
      tyre: r[11],
      tyreAge: r[12],
      stops: r[13],
      status:
        status === "STOP" ? "retired"
        : status === "PIT" ? "pit"
        : status === "OUT" ? "outlap"
        : lapped ? "lapped"
        : "racing",
      // The feed exposes DRS via CarData channel 45 (>= 10 is active). The
      // fixture approximates it: the leading group has it available.
      drsActive: i < 4 && status === "",
      overtakes: 0,
    };
  });

  const positions: CarPosition[] = CAR_POS.map(([x, y, tla]) => ({ tla, x, y }));

  const messages: RaceControlMessage[] = [
    { category: "Yellow flag",   lap: 34, time: "14:48:02", highlighted: true,  text: "YELLOW FLAG IN TRACK SECTOR 9 — DEBRIS ON THE RACING LINE, MARSHALS WORKING" },
    { category: "Penalty",       lap: 34, time: "14:47:55", highlighted: true,  text: "CAR 11 (PER) — 5 SECOND TIME PENALTY FOR CAUSING A COLLISION WITH CAR 31 (OCO) AT TURN 4" },
    { category: "Lap deleted",   lap: 34, time: "14:47:41", highlighted: false, text: "CAR 22 (TSU) LAP TIME 1:36.712 DELETED — TRACK LIMITS AT TURN 4, LAP 33" },
    { category: "Investigation", lap: 33, time: "14:46:12", highlighted: false, text: "CAR 18 (STR) — UNSAFE RELEASE IN THE PIT LANE UNDER INVESTIGATION AFTER THE SESSION" },
    { category: "Car event",     lap: 32, time: "14:45:19", highlighted: false, text: "CAR 2 (SAR) STOPPED ON TRACK AT TURN 11 — RECOVERY VEHICLE ON STANDBY" },
    { category: "Penalty",       lap: 31, time: "14:44:02", highlighted: false, text: "CAR 23 (ALB) — 10 SECOND TIME PENALTY FOR SPEEDING IN THE PIT LANE, SERVED AT LAP 31 STOP" },
    { category: "DRS",           lap: 28, time: "14:41:07", highlighted: false, text: "DRS ENABLED IN ALL THREE ZONES" },
    { category: "Blue flag",     lap: 26, time: "14:38:44", highlighted: false, text: "CAR 24 (ZHO) BEING LAPPED — BLUE FLAG SHOWN AT TURN 1" },
    { category: "Other",         lap: 24, time: "14:36:20", highlighted: false, text: "TRACK TEMPERATURE 40.2 C — RISING" },
    { category: "VSC",           lap: 21, time: "14:33:51", highlighted: false, text: "VIRTUAL SAFETY CAR ENDING — RACING RESUMES AT THE START/FINISH LINE" },
    { category: "Safety car",    lap: 19, time: "14:32:14", highlighted: false, text: "SAFETY CAR IN THIS LAP — RESTART EXPECTED ON LAP 21" },
    { category: "Green flag",    lap:  6, time: "14:18:02", highlighted: false, text: "TRACK CLEAR — ALL FLAGGED ZONES RELEASED" },
  ];

  const penalties: Penalty[] = [
    { tla: "PER", number: 11, teamName: TEAM_NAMES["RBR"]!, lap: 34, penalty: "+5.0s",               reason: "Causing a collision with car 31 (OCO) · turn 4",   state: "PENDING" },
    { tla: "STR", number: 18, teamName: TEAM_NAMES["AST"]!, lap: 33, penalty: "UNDER INVESTIGATION", reason: "Unsafe release in the pit lane",                   state: "NOTED" },
    { tla: "TSU", number: 22, teamName: TEAM_NAMES["RB"]!,  lap: 33, penalty: "LAP DELETED",         reason: "Track limits · turn 4 · 1:36.712",                 state: "APPLIED" },
    { tla: "ALB", number: 23, teamName: TEAM_NAMES["WIL"]!, lap: 31, penalty: "+10.0s",              reason: "Speeding in the pit lane · 62.4 km/h",             state: "SERVED" },
    { tla: "MAG", number: 20, teamName: TEAM_NAMES["HAA"]!, lap: 22, penalty: "+5.0s",               reason: "Leaving the track and gaining an advantage",       state: "SERVED" },
    { tla: "OCO", number: 31, teamName: TEAM_NAMES["ALP"]!, lap: 14, penalty: "REPRIMAND",           reason: "Failing to follow race director's instructions",   state: "APPLIED" },
  ];

  const timeline: TimelineBand[] = [
    { fromLap: 1,  toLap: 5,  label: "Green",      state: "none" },
    { fromLap: 6,  toLap: 18, label: "Green",      state: "none" },
    { fromLap: 19, toLap: 21, label: "Safety car", state: "safety-car" },
    { fromLap: 22, toLap: 33, label: "Green",      state: "none" },
    { fromLap: 34, toLap: 34, label: "",           state: trackState },
  ];

  const stints: Stint[] = [
    { tla: "VER", stops: 1, bars: [{ compound: "M", laps: 22 }, { compound: "H", laps: 12 }] },
    { tla: "NOR", stops: 1, bars: [{ compound: "M", laps: 24 }, { compound: "H", laps: 10 }] },
    { tla: "LEC", stops: 1, bars: [{ compound: "S", laps: 26 }, { compound: "M", laps: 8 }] },
    { tla: "HAM", stops: 2, bars: [{ compound: "M", laps: 12 }, { compound: "H", laps: 17 }, { compound: "M", laps: 5 }] },
    { tla: "SAI", stops: 1, bars: [{ compound: "S", laps: 13 }, { compound: "H", laps: 21 }] },
  ];

  /** Deterministic lap-time curves so the pace chart is stable between renders. */
  const paceLaps = (seed: number, base: number): number[] =>
    Array.from({ length: 11 }, (_, i) => base + Math.sin(i / 1.7 + seed) * 0.9 + Math.sin(i / 3.3 + seed) * 0.6);

  const pace: PaceLine[] = [
    { tla: "VER", color: teamColor("RBR", palette), laps: paceLaps(0.3, 95.5) },
    { tla: "NOR", color: teamColor("MCL", palette), laps: paceLaps(1.9, 95.9) },
    { tla: "LEC", color: teamColor("FER", palette), laps: paceLaps(3.4, 96.3) },
  ];

  return {
    overtakeAid: "drs",
    session: {
      meetingName: "Bahrain Grand Prix",
      circuitName: "Bahrain International Circuit",
      type: "RACE",
      currentLap: 34,
      totalLaps: 57,
      circuitKey: 63,   // Bahrain, per the design's mock session
      year: 2024,
      round: 1,
      startDate: "2024-03-02T15:00:00",
    },
    weather: { trackTemp: 40.2, airTemp: 23.4, windSpeed: 7.0, windDirection: 183, humidity: 32, pressure: 1012, rainfall: false },
    trackState,
    drivers,
    timing,
    positions,
    messages,
    penalties,
    timeline,
    stints,
    pace,
    topLapSpeed: 318,
    currentLapTime: "1:35.523",
    currentLapDelta: "-1.075",
    bestLapTime: "1:34.918",
  };
}
