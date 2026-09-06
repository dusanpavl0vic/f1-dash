# Design Brief — F1 Live Timing & Telemetry Dashboard

> **This document is self-contained.** It is written to be handed to a designer who knows nothing
> about Formula 1. Every domain term is defined at first use. Nothing here requires reading the
> other files in `docs/`.
>
> Audience: Claude Design. Output: a complete visual design system + screen designs.

---

## 1. What we are designing

A **web dashboard that shows live Formula 1 race data while the race is happening**. It is the
same information a TV director sees: who is in which position, how far apart the cars are, how
fast each one is going, where each car is on the circuit right now, and what the race officials
are announcing.

It is a **dense, real-time instrument panel**, not a content site. A user has it open on a second
screen or a tablet next to a TV, glancing at it every few seconds during a two-hour race.

It also replays past races with the same interface, and shows the season calendar and championship
standings.

**Reference points** (do not copy, but understand the genre): the official F1 TV timing sidebar,
`f1-dash.com` (now shut down), Nitrous (desktop app), and — outside F1 — a Bloomberg terminal or a
flight-tracking dashboard. Numbers dominate. Chrome is minimal. Nothing decorative competes with
the data.

---

## 2. Design principles

These are ranked. When two conflict, the higher one wins.

1. **Glanceability over completeness.** The user looks away from the TV for one second. In that
   second they must find their driver's position and gap. If they have to hunt, the design failed.
2. **Change must be visible without being noisy.** A new fastest sector should announce itself.
   Twenty rows updating four times a second must not shimmer. Solve this with brief, decaying
   highlights on *meaningful* changes only — never a persistent animation.
3. **Colour carries meaning, and only meaning.** In this domain purple/green/yellow/red are not
   brand choices — they are the sport's vocabulary (defined in §5.3). Do not spend them on
   decoration. Everything non-semantic is greyscale plus one neutral accent.
4. **Density is a feature.** Twenty drivers × ten columns must fit on a 1440px screen with no
   scrolling. Generous whitespace is the wrong instinct here. Aim for the information density of a
   spreadsheet with the legibility of a well-set table.
5. **Degrade honestly.** There is no F1 session happening 340 days a year, connections drop, and
   data arrives late. Every empty, broken, and delayed state must be designed — see §7. These are
   not edge cases; out of season they are the *only* state.

**Tone:** technical, precise, quiet. Think instrumentation, not entertainment. No gradients on
data surfaces, no glassmorphism, no drop shadows used for decoration, no illustrations, no
rounded-everything. Sharp or slightly-rounded corners (2–4px), hairline borders, flat fills.

---

## 3. Who uses it, and how

| | |
|---|---|
| **Primary user** | An F1 fan watching the race on TV, with this open on a laptop or tablet beside them. Knows the sport. Wants detail the broadcast does not show. |
| **Secondary user** | Someone analysing a past race — scrubbing a replay, comparing two drivers' telemetry. Same screens, slower pace, more interaction. |
| **Viewing conditions** | Frequently a dark room, evening, screen at arm's length or further. **Dark theme is the primary design; light theme must exist and be equally correct**, for daytime European races and for users who prefer it. |
| **Session length** | 1–3 hours of continuous viewing. Eye strain matters. Nothing may flash, pulse, or animate continuously. |
| **Attention pattern** | 1–3 second glances, dozens of times per session. Not sustained reading. |

---

## 4. Domain glossary

Read this once; the rest of the document uses these terms freely.

| Term | Meaning |
|---|---|
| **Session** | One on-track event. Types: Practice 1/2/3 (FP1/FP2/FP3), Qualifying (Q), Sprint Qualifying (SQ), Sprint (S), Race (R). |
| **Driver / car number** | Each driver has a permanent number (1, 44, 81…). Displayed everywhere. |
| **TLA** | Three-letter abbreviation for a driver — `VER`, `HAM`, `LEC`. The primary identifier in the UI; shorter than a name, unambiguous to a fan. |
| **Team colour** | Each of the 10 teams has a colour. **It arrives from the live data feed at runtime and changes between seasons — it cannot be hardcoded in the design.** See §8.1. |
| **Gap** | Time behind the race leader, e.g. `+12.482`. Can also be `1 L` meaning "one full lap behind" (a *lapped* driver). |
| **Interval** | Time behind the car immediately ahead. Usually the more useful number. The UI toggles between gap and interval. |
| **Sector** | Each circuit is split into 3 timed sectors (S1, S2, S3). Each sector time gets a colour — see §5.3. |
| **Mini-sector / segment** | Each sector is further split into 5–10 short segments (so 15–25 per lap). Rendered as a row of tiny coloured bars showing where a driver is gaining or losing. |
| **Tyre compound** | SOFT / MEDIUM / HARD / INTERMEDIATE / WET. Each has a fixed, non-negotiable brand colour (§5.4). |
| **Tyre age** | Number of laps on the current set. Shown next to the compound. |
| **Stint** | A run on one set of tyres, between pit stops. |
| **Pit / box** | Where cars stop to change tyres. A driver is `IN PIT`, then on an `OUT LAP`. |
| **DRS** | An overtaking aid. Binary indicator: active or not. |
| **Race Control** | The officials. They publish messages: flags, penalties, investigations. A scrolling feed in the UI. |
| **Flags** | Green = clear. Yellow = danger, slow down. Double yellow = greater danger. Red = session stopped. Chequered = session finished. Blue = let a faster car past. |
| **Safety Car (SC)** | A car that leads the field slowly after an incident. Race effectively neutralised. |
| **Virtual Safety Car (VSC)** | Same purpose, no physical car — drivers must slow to a delta. |
| **Marshal sector** | The circuit is divided into ~20 marshalling zones. A yellow flag applies to a specific zone, so the track map colours only that stretch of track. |
| **Broadcast delay** | The data feed runs **ahead of every TV broadcast** by 3–40 seconds. The user sets a delay so the dashboard matches their TV. This is a headline feature, not a setting nobody finds. |
| **Replay** | Playing a past session back through the identical interface, with play/pause/seek/speed. |
| **Telemetry** | Continuous per-car channels: speed, throttle, brake, gear, engine RPM, DRS. Drawn as line charts. |

---

## 5. Design tokens

Deliver a token table with **both themes for every token**. The names below are the contract —
implementation will use these exact names as CSS custom properties.

### 5.1 Surfaces and text

| Token | Dark (given) | Light (design this) | Use |
|---|---|---|---|
| `--bg` | `#0a0a0a` | | Page background |
| `--surface` | `#141414` | | Panels, cards, table backgrounds |
| `--surface-raised` | design this | | Modals, dropdowns, popovers |
| `--surface-hover` | design this | | Row hover, button hover |
| `--border` | `#262626` | | Hairlines between rows, panel edges |
| `--border-strong` | design this | | Focus rings, active tabs, table header rule |
| `--text` | `#fafafa` | | Primary numbers and labels |
| `--text-dim` | `#a1a1a1` | | Secondary values, units, column headers |
| `--text-faint` | design this | | Disabled, placeholder, "no data" |
| `--accent` | design this | | The single non-semantic interactive colour: links, selected tab, focus. Pick something that does **not** collide with the semantic five below. Blue is taken by DRS; suggest a cyan/teal or a neutral slate. |

### 5.2 The critical constraint on semantic colour

Every semantic colour needs **three** values, not one, because it appears in three roles:

| Role | Example | Requirement |
|---|---|---|
| `--x-fill` | Background of a mini-sector bar, a tyre badge, a banner | Any luminance; it is a surface |
| `--x-on-fill` | Text sitting on that fill | ≥ 4.5:1 against `--x-fill` |
| `--x-text` | The colour of a *sector time number* or an icon on the page background | ≥ 4.5:1 against **both** `--bg` and `--surface` |

This matters most in light theme. `#ffd12e` (yellow) is perfectly readable as a fill on a dark
page but is invisible as text on white — `--yellow-text` in light theme must be a much darker
amber while `--yellow-fill` stays the recognisable bright yellow. **Do not simply invert the dark
palette.** Solve each role separately and state the contrast ratio you achieved for each.

### 5.3 Semantic colours — fixed meaning, do not repurpose

| Token | Dark reference | Meaning — this is sport vocabulary, not a style choice |
|---|---|---|
| `--purple` | `#b16cea` | **Overall fastest.** The fastest sector or lap by *anyone* in the session. The most prestigious colour on screen. |
| `--green` | `#43b02a` | **Personal best.** A driver's own fastest, but not the overall fastest. Also: track is clear. |
| `--yellow` | `#ffd12e` | **Caution.** Yellow flag, slower than personal best, a sector under warning. |
| `--red` | `#da291c` | **Stopped / danger.** Red flag, retired driver, session stopped. |
| `--blue` | design this | **Lapped / in pit.** A car being lapped, or a mini-sector traversed in the pit lane. Must be distinguishable from `--drs`. |
| `--drs` | `#2b7fff` | DRS active indicator, and DRS zones drawn on the track map. |
| `--grey-neutral` | design this | Mini-sector not yet driven this lap. The resting state. |

### 5.4 Tyre compounds — brand colours, fixed, do not adjust

| Compound | Colour | Note |
|---|---|---|
| SOFT | `#da291c` | |
| MEDIUM | `#ffd12e` | |
| HARD | `#f0f0ec` | **Near-white.** On light theme this needs a visible outline or it disappears. Design that outline. |
| INTERMEDIATE | `#43b02a` | |
| WET | `#0067ad` | |

These collide with the semantic palette on purpose — they are the real F1 colours and fans read
them instantly. Disambiguate by **shape and placement**, not by changing the colours: tyre
compounds always appear as a circular badge with a letter (S/M/H/I/W) inside, never as a bar or a
number colour.

### 5.5 Track map colours

| Token | Use |
|---|---|
| `--track` (dark `#2a2a2a`) | The track surface stroke. In light theme this must be a mid-grey that team-colour dots read clearly against — not a light grey. |
| `--track-edge` | Optional outline for definition |
| `--marshal-default` | Marshal sector overlay at rest — near-invisible |
| `--marshal-yellow` / `--marshal-red` | Flagged marshal sector overlay, drawn *under* the track surface at ~55% opacity |

---

## 6. Typography and numeric formatting

**The single most important typography decision in this project: all changing numbers must be
tabular (monospaced digits).** Gaps, lap times, sector times, speed, temperature. If digits
change width, every row jitters four times a second and the whole design falls apart.

Specify:
- **UI / label typeface** — a neutral grotesque. System stack is acceptable and preferred for
  performance.
- **Numeric typeface** — either the same family with `font-variant-numeric: tabular-nums`, or a
  separate mono family. State which, explicitly.
- **A type scale** with no more than 6 steps. This is a data UI; it does not need 12 sizes.
- **Minimum readable size**: nothing below 11px, and 11px only for units and column headers.

Formatting rules to design around (these are fixed by the data):

| Value | Format | Note |
|---|---|---|
| Lap / sector time | `1:23.456` or `23.456` | Never trailing-zero-stripped |
| Gap | `+12.482` | Always signed |
| Lapped driver | `1 L` / `2 L` | Not a number — must not break a numeric column layout |
| Leader's gap | *empty*, or `LEADER` | Design what fills the space |
| Interval, first car | *empty* | Same |
| Session clock | `1:24:31` counting down | Ticks every second — the only continuously-updating element |
| Temperature | `27.4°C` | |
| Speed | `318 km/h` | Unit may be dimmed or omitted in dense contexts |
| Position | `1`–`20` | Fixed 2-character width |

---

## 7. States that must be designed

**This section is half the work.** In this application the non-happy states are not rare — out of
season, "no live session" is the *only* state a visitor ever sees.

Design each of these as an explicit artboard or component variant:

### 7.1 Application-level

| State | What the user sees | Design note |
|---|---|---|
| **No live session** | The dashboard route with no data | This is the default for ~340 days a year. It must be a designed destination — next session countdown, link to replays — not an empty grey box. |
| **Loading initial snapshot** | First 1–3 seconds after connecting | Skeleton rows, not a spinner. The user should see the *shape* of the tower arriving. |
| **Connected** | Normal | A small, calm, persistent indicator. Green dot or similar. |
| **Reconnecting** | Connection dropped, retrying | Must be unmissable but not alarming. Data on screen is now stale — indicate that, e.g. dim the data area. |
| **Disconnected / failed** | Retries exhausted | Clear message, explicit retry action. |
| **Live but delayed** | User has set a broadcast delay | See 7.3 — the most important indicator in the app. |

### 7.2 Session-state banners

A full-width banner across the top of the dashboard. These override each other in this priority:

| Banner | Trigger | Design |
|---|---|---|
| **RED FLAG** | Session stopped | Highest urgency. Full-width, red. **Must carry text, not just colour** — accessibility requirement. |
| **SAFETY CAR** | SC deployed | Yellow, full-width, text `SAFETY CAR`. |
| **VIRTUAL SAFETY CAR** | VSC deployed | Yellow, text `VSC`. Visually distinct from SC — a fan must tell them apart instantly. |
| **VSC ENDING** | VSC about to end | Transitional, brief. |
| **YELLOW FLAG** | Yellow somewhere on track | Lower prominence than SC. |
| **CHEQUERED FLAG** | Session finished | Neutral/monochrome, celebratory but restrained. |
| **(none)** | Track clear | Banner area collapses. Design the collapsed state so the layout does not jump — reserve the space or animate the collapse. |

### 7.3 Broadcast delay indicator — design this carefully

The single biggest support problem in the predecessor app was: *a user sets a 30-second delay,
forgets, comes back next week, and thinks the app is broken because it shows old data.*

Requirements:
- A **persistent badge in the header** whenever delay > 0, showing the value: `DELAY 30s`.
- It must be impossible to overlook, but must not be alarming during normal use.
- Design an **active/attention variant** for when the delay is large (> 60s).
- Design a **"catching up"** state: when the user *reduces* the delay, the app fast-forwards
  through buffered data for up to a few seconds. Show progress, not a freeze.
- Design the **delay control** itself: a 0–120 second slider with quick presets (0 / 5 / 15 / 30 / 60).
  It appears both in Settings and in a quick-access popover from the header badge.

### 7.4 Per-driver row states

| State | Label shown | Design |
|---|---|---|
| Normal | — | |
| In pit | `PIT` | Row visibly de-emphasised |
| Out lap (just left pit) | `OUT` | |
| Stopped on track | `STOP` | |
| Retired | `OUT` / struck through | Row moves to bottom, heavily dimmed, removed from track map |
| Lapped | gap shows `1 L` | Distinct treatment — a fan must see at a glance who is lapped |
| Favourite driver | — | User-pinned. A highlighted row + a ring on the map dot. Design the highlight so it reads without competing with the semantic colours. |
| Position gained/lost | — | A brief directional flash on the position number, then settle |

### 7.5 Replay-specific

| State | Design |
|---|---|
| **Session not yet processed** | The first time anyone opens a given past session, the server must process it — 30 to 120 seconds. Design a **progress UI with named stages** (downloading → parsing → building), not a spinner. A spinner for 90 seconds reads as broken. |
| **Buffering / seeking** | Brief, after a scrub |
| **Paused** | All values frozen. Make it obvious the data is intentionally static, not stalled. |
| **Playing at non-1x speed** | Speed indicator: `2×`, `0.25×` |
| **End of session reached** | |

### 7.6 Data-level empties

- Driver has no lap time yet (start of session) — every timing column empty
- Qualifying before anyone has run — the tower exists but is entirely blank
- Telemetry chart with no driver selected
- Race control feed with zero messages
- Standings before round 1 of a season

---

## 8. Hard constraints the design must respect

### 8.1 Team colours are runtime data

Ten team colours arrive from the live feed as raw hex values and change between seasons and
occasionally mid-season. **The design cannot specify them and must not depend on any particular
one.**

Design implications:
- Every place a team colour appears must work with an *arbitrary* saturated hex — including a very
  dark one (near-black) and a very light one (near-white).
- Provide a rule for text placed on a team colour, or avoid placing text on it entirely.
- Provide a fallback grey for a driver whose colour has not arrived yet.
- Show at least one mockup with a deliberately awkward palette (two near-identical blues, one
  near-white) to prove the design survives it.

Team colour is used as: a 3–4px vertical bar at the left edge of each timing row, the fill of the
car dot on the track map, and the line colour in telemetry charts.

### 8.2 The track map is real-world geometry

The circuit outline is drawn from real coordinate data in track units (roughly 1/10 metre), not
from an illustration. Consequences:

- **Do not draw circuit shapes.** Every circuit is a different shape and there are 24 of them,
  generated at runtime. Design the *frame, overlays and legend* around an outline you treat as
  given. Use one real circuit (Bahrain or Suzuka) as a placeholder in mockups and note that it varies.
- **Stroke widths must be given in track units, not pixels.** The track surface is ~260 units
  ≈ 26 metres wide. Car dots ~220 units radius. These scale with the viewport automatically. A
  pixel value for these is unusable.
- Aspect ratio varies wildly by circuit — from nearly square (Bahrain) to long and thin (Monza,
  Las Vegas). The map container must handle both without the layout breaking.
- Elements layered on the map: marshal-sector overlays (underneath), the track surface, DRS zone
  markers, the start/finish line, and 20 car dots with TLA labels.

### 8.3 Update frequency

- Position data arrives ~4 times per second, smoothed to 60fps on screen.
- Timing data arrives in bursts, up to ~10 updates per second.
- **No element may animate continuously.** Highlights are brief (~1.5s) and decay. The session
  clock ticking once per second is the only permanent motion.
- Row reordering (a position change) should animate as a move, not a jump-cut — but must be
  disabled under `prefers-reduced-motion`.

### 8.4 Accessibility

- **Track status must never be conveyed by colour alone.** Every banner carries text.
- Sector-time colour (purple/green/yellow) is the one place colour carries meaning without a text
  equivalent — provide a secondary cue (a small marker, a border weight, or an optional
  pattern/icon mode) and design a colour-blind-safe variant of the purple/green pair specifically.
  Deuteranopia makes `#43b02a` and `#ffd12e` hard to separate, and protanopia affects the red.
- The timing tower is a real `<table>` with headers — design it as tabular data with a header row,
  not as a stack of cards.
- Minimum 4.5:1 contrast for all text; 3:1 for meaningful non-text (borders that separate rows,
  the track outline, chart lines).
- Visible focus states for every interactive element, in both themes.

---

## 9. Screens

Eight routes. Priority order for design effort: **1, 2 and 7 are the product**; the rest are
supporting.

### 9.1 `/dashboard` — the live dashboard ★ primary screen

The screen that matters. Everything else is secondary to getting this right.

**Desktop layout (≥1440px)** — three columns beneath a header and an optional status banner:

```
┌─────────────────────────────────────────────────────────────────────┐
│ HEADER  logo · session name · session clock · lap 34/57 ·           │
│         DELAY 30s badge · connection dot · settings                 │
├─────────────────────────────────────────────────────────────────────┤
│ BANNER  (only when SC / VSC / RED / CHEQUERED — collapses when clear)│
├──────────────────────────┬──────────────────┬───────────────────────┤
│ TIMING TOWER             │ TRACK MAP        │ RACE CONTROL          │
│ 20 rows, the densest     │ square-ish,      │ newest first, scrolls │
│ element on screen        │ dominant visual  ├───────────────────────┤
│                          │                  │ WEATHER               │
│                          │                  ├───────────────────────┤
│                          │                  │ TELEMETRY             │
│                          │                  │ selected driver       │
└──────────────────────────┴──────────────────┴───────────────────────┘
```

Column proportions are yours to propose, but the timing tower needs roughly 40–45% and must not
scroll vertically at 1440×900 with 20 drivers.

**Panels must be individually toggleable** (a user who only wants the tower and map turns the
rest off). Design the layout so removing a panel reflows sensibly rather than leaving a hole.

#### 9.1.1 Timing tower — the core component

One row per driver, 20 rows, sorted by position. Every column below is real data that must appear.

| Column | Content | Width behaviour |
|---|---|---|
| Position | `1`–`20` | Fixed |
| Team colour bar | 3–4px vertical rule | Fixed |
| Driver | TLA (`VER`) + car number. Optionally a headshot at larger sizes. | Fixed |
| DRS | Active / inactive indicator | Fixed, narrow |
| Gap / Interval | `+12.482` or `1 L`. **Header is clickable to toggle which is shown** — design both the toggle affordance and the two states. | Fixed, tabular |
| Mini-sectors | A row of 15–25 tiny bars, each coloured grey/yellow/green/purple/blue. The most information-dense element in the app. | Flexible — must work at 15 and at 25 segments |
| S1 / S2 / S3 | Three sector times, each individually coloured purple/green/yellow | Fixed, tabular |
| Last lap | `1:23.456`, coloured | Fixed, tabular |
| Best lap | `1:22.981`, coloured | Fixed, tabular |
| Tyre | Compound badge (letter in a coloured circle) + age in laps, e.g. `M 14` | Fixed |
| Pit stops | Count | Fixed, narrow |
| Status | `PIT` / `OUT` / `STOP` when applicable | Fixed |

Design decisions needed:
- **Row height.** 20 rows must fit without scrolling at 900px viewport height minus header,
  banner and column header. That is roughly 34–38px per row. Prove it.
- **Which columns drop at narrower widths**, in order. Propose the sequence.
- **The "new fastest sector" flash**: a sector time turning purple should flash and settle within
  ~1.5s. Design the flash — a background wash, a border pulse, or a brightness ramp.
- **Row reorder**: when a driver overtakes, rows swap. Design the transition.
- **Expanded row**: clicking a driver reveals detail inline (full lap history, stint history,
  live telemetry). Design collapsed and expanded.

#### 9.1.2 Track map

- Circuit outline with 20 moving dots, each a team-coloured circle with the driver's TLA beside it.
- Marshal sectors coloured when flagged.
- DRS zones marked along the track.
- Start/finish line.
- A legend.
- **Design the label collision problem**: 20 TLAs on a start-line grid are all within a few
  hundred track units of each other. Propose a treatment (label only favourites, label on hover,
  fade labels when clustered, or a leader-line approach).
- Favourite drivers get a white ring on their dot.
- Cars in the pit report positions that wander off the circuit outline — design a treatment
  (reduced opacity, or a dedicated pit-lane area).

#### 9.1.3 Race control feed

Chronological list, newest at top. Each entry: timestamp, lap number, category icon, message text.
Categories: Flag, DRS enabled/disabled, Safety Car, Other, Car event (penalties, investigations).
Messages are ALL CAPS from the source and can be long — design for two- and three-line wrapping.

#### 9.1.4 Weather panel

Air temperature, track temperature, humidity, air pressure, wind speed, wind direction, rainfall
(binary yes/no). Seven values, compact. Wind direction is a compass bearing in degrees — an arrow
is more legible than a number. Track temperature is the most-watched value; give it prominence.

#### 9.1.5 Telemetry panel

For the selected driver: current speed, throttle %, brake, gear, RPM, DRS. Design both a **live
gauge/bar readout** (updating 4×/sec — must not jitter) and a **rolling trace chart** of the last
~30 seconds.

### 9.2 `/dashboard/track-map` — map-focused view ★

The same track map at full screen, with a condensed timing tower alongside (position, TLA, gap
only). For users who want the spatial view dominant.

### 9.3 `/replay/[year]/[round]/[type]` — replay dashboard ★

Identical to `/dashboard`, plus a **transport bar** pinned to the bottom:

- Play / pause
- A **scrub bar** showing the whole session, with:
  - Lap markers (a tick per lap, ~50–70 of them for a race)
  - **Event markers** at safety car deployments, red flags, and race control incidents — these are
    the things a user actually seeks to. Design distinct marker types.
  - Current position, buffered/loaded range, hover preview showing the time and lap under cursor
- Elapsed / total time, and current lap number
- Speed control: 0.25× / 0.5× / 1× / 2× / 4× / 8×
- Jump-to-lap input

The scrub bar is the hardest single component in this screen. It must be usable at 375px width.

### 9.4 `/replay` — session picker

Browse by year → round → session. Each session card shows: circuit, date, session type, winner (if
finished), and whether it is ready to replay or needs processing first. Design the year selector
for 2018 → current.

### 9.5 `/schedule` — season calendar

The season's rounds. Each round has 3–5 sessions with local start times. Design three round states:
**completed** (show the winner and a link to replay), **live now** (prominent), and **upcoming**
(countdown for the next one). Include a country flag or circuit thumbnail treatment.

### 9.6 `/dashboard/standings` — championship standings

Two tables: drivers and constructors. Position, name/team, points, wins. Team colours applied.
A simple, correct table — this screen needs the least invention.

### 9.7 `/` — landing ★

The first thing a visitor sees, and out of season the *only* thing. Must handle both:
- **Session live now** → large, unmissable "watch live" entry point
- **No session** → countdown to the next one, plus routes into replay and schedule

Also carries the project identity, a one-line explanation, and the required legal notice (§11).

### 9.8 `/settings`

- Broadcast delay slider + presets (§7.3)
- Theme: dark / light / system
- Favourite drivers picker (all 20, multi-select)
- Panel visibility toggles
- Gap vs interval default
- Units (°C/°F, km/h/mph)
- Show/hide drivers who are off track
- Reduced motion override

Preferences are per-browser; there are no user accounts. Design accordingly — no profile, no
avatar, no sign-in anywhere in the app.

---

## 10. Responsive behaviour

Design at these widths: **375, 768, 1024, 1440, 1920**.

| Width | Layout |
|---|---|
| 375 (mobile) | Single column. Track map at top (fixed height), condensed timing tower below (position, TLA, gap, tyre only). Everything else behind a bottom tab bar: Timing / Map / Telemetry / Control. Header collapses to session name + delay badge + connection dot. |
| 768 (tablet portrait) | Two columns: tower + map. Race control and weather in a collapsible drawer. More timing columns return. |
| 1024 | Two columns with a narrow third for race control and weather. |
| 1440 | Full three-column layout, all columns, no vertical scroll on the tower. |
| 1920 | Same as 1440 with wider columns and larger map — do **not** add a fourth column or increase font sizes proportionally. Extra width goes to the map and to restoring any dropped timing columns. |

State explicitly, in a table, **which timing-tower columns are visible at which width**, in drop order.

---

## 11. Out of scope — do not design these

- Video player, stream embed, or anything resembling F1 TV
- Sign-in, sign-up, user profiles, avatars, account settings
- Payment, subscription, pricing, or upgrade prompts
- Social feeds, comments, chat, sharing
- Betting or predictions
- Native mobile app shells
- Onboarding carousels or feature tours
- Marketing sections, testimonials, feature grids

**Required in the footer of every page** (legal, non-negotiable, verbatim):

> This project is unofficial and is not associated in any way with the Formula 1 companies. F1,
> FORMULA ONE, FORMULA 1, FIA FORMULA ONE WORLD CHAMPIONSHIP, GRAND PRIX and related marks are
> trade marks of Formula One Licensing B.V.

Design a footer treatment for this that is legible but unobtrusive. It must not use F1 branding,
the F1 logo, or the F1 wordmark anywhere in the design.

---

## 12. What to deliver

1. **Token sheet** — every token from §5, both themes, with achieved contrast ratios noted for
   each text/background pair.
2. **Type scale** — families, sizes, weights, line heights, and the explicit tabular-numeral rule.
3. **Component sheet** — each of these with all its states:
   timing row · mini-sector bar · tyre badge · sector time cell · gap cell · driver status chip ·
   DRS indicator · car dot · status banner (all 6 variants) · connection indicator · delay badge ·
   race control entry · weather stat · scrub bar · transport controls · session card · tab bar.
4. **Screens** — the eight routes of §9 at 375 / 768 / 1440, in **both themes** for at least
   `/dashboard` and `/`.
5. **State artboards** — everything listed in §7 that is not already covered by a component state.
6. **The awkward-palette proof** from §8.1.
7. **A one-page rationale**: the three or four decisions you would defend, and what you traded away.

Design in this order, and stop for feedback after step 4's `/dashboard` at 1440 dark — that single
artboard determines everything else.
