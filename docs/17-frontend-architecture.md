# 17 — Frontend Architecture

> Applies `REACT-APP-TEMPLATE.md` to this domain. That template is authoritative for **how code is
> organised**; this document is authoritative for **what goes where in this specific app** and for
> the one place we deliberately extend it.
>
> Supersedes `11-frontend.md` on stack and structure. `11-frontend.md` remains authoritative for
> **component behaviour** — the timing tower details, theming, accessibility and the rendering
> budget.

---

## 1. Stack

Per the template, with the deviations noted.

| Layer | Choice | Note |
|---|---|---|
| Build | Vite 6 + TypeScript strict | |
| UI | React 19, React Compiler on | |
| Router | React Router, object routes | |
| Server state | RTK Query | schedule, standings, track geometry, historical telemetry |
| Client state | Redux Toolkit | settings, UI state, replay transport state |
| **Live stream state** | **external store + `useSyncExternalStore`** | **the deliberate extension — see §3** |
| Styling | Tailwind 4 + `cva` + `cn()` | tokens as CSS custom properties, light and dark |
| Charts | uPlot | telemetry traces |
| Track map | raw SVG | no library |
| Test | Vitest + Testing Library + MSW; Playwright e2e | |

**Not Next.js.** This is a wholly client-side realtime application. Server rendering buys nothing
when the first meaningful paint depends on a WebSocket snapshot, and an App Router adds a layer
that fights the socket lifecycle and the animation-frame loop. The landing and schedule pages are
the only SSR candidates and they do not justify the cost.

---

## 2. Feature layout

```
src/
├── main.tsx
├── App.tsx
├── providers/            StoreProvider, ThemeProvider, ErrorBoundary, LiveConnectionProvider
├── routes/               router.tsx
├── store/                store.ts, rootReducer.ts, hooks.ts
├── pages/                composition only, no logic
│
├── features/
│   ├── live/             THE CORE — connection, state, delay buffer
│   │   ├── lib/          merge.ts · delayBuffer.ts · clockSkew.ts · wsClient.ts
│   │   ├── store/        f1State.ts (external store) · connectionSlice.ts
│   │   ├── hooks/        useDriverTiming · useTrackStatus · useConnection · usePositions
│   │   └── types.ts      re-exports from src/types/generated.ts
│   │
│   ├── timing/           TimingTower · DriverRow · MiniSectors · TyreBadge · GapCell · DriverStatus
│   ├── track-map/        TrackMap · CarDot · MarshalSectors · DrsZones · Legend
│   │                     lib/interpolate.ts · lib/toScreen.ts
│   ├── telemetry/        TelemetryPanel · ChannelChart · DriverCompare  api/telemetryApi.ts
│   ├── race-control/     MessageFeed · MessageEntry · StatusBanner
│   ├── weather/          WeatherPanel
│   ├── replay/           ReplayControls · ScrubBar · LapJump  store/replaySlice.ts
│   ├── schedule/         api/scheduleApi.ts · RoundCard · SessionList
│   ├── standings/        api/standingsApi.ts
│   └── settings/         DelaySlider · FavouriteDrivers · PanelToggles · ThemeToggle
│
├── components/           shared: ui/ atoms/ molecules/ organisms/ layouts/
├── hooks/                useMediaQuery · useRafLoop · useLocalStorage
├── lib/                  format.ts · time.ts · colour.ts   (zero-dep, no src/ imports)
├── styles/               globals.css · tokens.css
└── types/generated.ts    GENERATED from the C# records — never edited by hand
```

`features/live` is the only feature other features may depend on, and only through its barrel. It
owns the connection, the state and the delay buffer; everything else reads from it via hooks. This
is the template's "feature does not import feature" rule with one sanctioned exception, made
explicit rather than smuggled in.

---

## 3. The one deliberate extension to the template

The template (§9) puts state in three categories: server state in RTK Query, client state in
Redux, and local state in the component. **This app has a fourth: high-frequency stream state.**

Roughly ten deltas per second arrive against a deeply nested object. A Redux dispatch per delta
would re-render the tree and drop frames on mid-range hardware. So:

| State | Home | Why |
|---|---|---|
| Schedule, standings, track geometry, historical telemetry | **RTK Query** | Genuine server state — cached, deduplicated, invalidated. The template's rule applies unchanged. |
| Settings, panel visibility, favourites, theme, replay transport | **Redux Toolkit** | Ordinary client state, changes rarely. Unchanged. |
| **Live F1 state (`F1State`)** | **module-level object outside React, exposed via `useSyncExternalStore`** | ~10 updates/s on a deep object. Redux is the wrong tool at this frequency, not because Redux is slow but because a dispatch-per-delta forces a top-down re-render. |
| Car positions for map animation | **DOM refs written from an animation-frame loop** | 60 fps × 20 cars. This never touches React at all. |

Everything else in the template — the folder tree, the decision table, import direction, component
folders, naming, the lint enforcement — applies unchanged. This is one documented exception, not a
licence to improvise.

### 3.1 How the live store works

```
WebSocket message
   → merge.ts applies the delta into the module-level state object (in place)
   → the touched paths are recorded in a dirty set
   → ONE requestAnimationFrame flush notifies subscribers of dirty paths only
   → components subscribed to those paths re-render
```

- Components subscribe to the **narrowest possible slice**. `DriverRow` for car 44 subscribes to
  that driver's timing only, and re-renders only when car 44's data changes.
- Never subscribe to the whole state object. There is a lint rule for this.
- The delay buffer sits **between** the socket and the merge: frames are queued and released by
  timestamp, so the merge only ever sees frames that are due.

### 3.2 `merge.ts` is a port, not an independent implementation

`merge.ts` mirrors `DeltaMerge.cs` rule for rule, and both run against the **same** JSON fixtures in
`shared-fixtures/merge/`. CI fails on divergence.

This matters more than it looks. If the two merges disagree, the browser shows something different
from the server while both remain internally consistent, so nothing crashes and nothing logs — the
tower is simply wrong. The shared-fixture test is the only practical guard against that.

---

## 4. Rendering budget

Hard targets, verified against a recorded race at 1×:

| Metric | Target | Technique |
|---|---|---|
| Long tasks (> 50 ms) | 0 | animation-frame-batched delta application |
| React commits / s, steady state | < 15 | narrow-slice subscriptions, `React.memo` on `DriverRow`, `MiniSectors`, `CarDot` |
| Track map frame rate | 60 fps | refs and direct DOM writes, zero React commits during animation |
| Memory growth over 2 h | < 100 MB | bounded buffers; no unbounded position history |

Additional rules that are not optional:
- **No `key={index}` anywhere in the timing tower.** Key by driver number, or reorder animations tear.
- uPlot for traces — not Recharts or Chart.js.
- Position interpolation is linear, never spline. Splines overshoot on hairpins and cars visibly cut
  across the grass.

---

## 5. Types are generated

`src/types/generated.ts` is emitted from the C# records in `F1Dash.Shared/Models`. It is committed,
and CI regenerates and diffs it — a stale file fails the build. Never hand-edit it, and never
declare a parallel interface for a model that already exists there.

---

## 6. Theming

Tokens are CSS custom properties in `styles/tokens.css`, with a complete light and dark pair for
every value, per the design brief §5. Team colours are injected at runtime from
`DriverList.TeamColour` (hex without a leading `#`) and are **never** hardcoded — only a fallback
grey is.

---

## 7. Acceptance criteria

- [ ] The dashboard renders a full recorded race with all panels populated.
- [ ] The rendering budget table above is met, verified in the profiler.
- [ ] Disconnecting the network shows a clear reconnecting state and recovers without a reload.
- [ ] All panels work at 375 px width.
- [ ] Replay controls drive the server engine and the UI reflects seek, pause and speed within 200 ms.
- [ ] `merge.ts` and `DeltaMerge.cs` agree on every shared fixture.
- [ ] Lint enforces the import direction and the no-whole-state-subscription rule.
