# 11 — Frontend (Next.js)

Target: `packages/dashboard/`. Next.js 15 App Router, TypeScript, Tailwind v4.

## Routes

```
/                       landing — next session countdown, live banner if a session is on
/dashboard              the live dashboard
/dashboard/track-map    map-focused view
/dashboard/standings    championship standings
/schedule               season calendar
/replay                 session picker
/replay/[year]/[round]/[type]   the replay dashboard
/settings               preferences
```

## Component tree

```
components/
├── layout/
│   ├── Header.tsx              connection status, delay badge, session name, clock
│   ├── Sidebar.tsx
│   └── SessionClock.tsx        ExtrapolatedClock, ticks locally between updates
├── timing/
│   ├── TimingTower.tsx         the list; virtualised only if >30 rows
│   ├── DriverRow.tsx           memoised; subscribes to ONE driver's slice
│   ├── MiniSectors.tsx         segment bar
│   ├── TyreBadge.tsx           compound + age
│   ├── GapDisplay.tsx          leader/interval toggle
│   └── DriverStatus.tsx        PIT / OUT / STOP
├── track/
│   ├── TrackMap.tsx            SVG container
│   ├── CarDot.tsx              imperative rAF positioning via refs
│   ├── MarshalSectors.tsx
│   └── TrackMapLegend.tsx
├── telemetry/
│   ├── TelemetryPanel.tsx
│   ├── SpeedTrace.tsx          uPlot
│   ├── ChannelChart.tsx        throttle/brake/gear/rpm
│   └── DriverCompare.tsx       two-driver overlay + delta
├── info/
│   ├── RaceControl.tsx         message feed, newest first
│   ├── Weather.tsx
│   ├── TrackStatusBanner.tsx   full-width SC/VSC/RED banner
│   └── LapCounter.tsx
├── replay/
│   ├── ReplayControls.tsx      play/pause, scrub, speed
│   ├── ScrubBar.tsx            with event markers
│   └── LapJump.tsx
└── settings/
    ├── DelaySlider.tsx
    ├── FavouriteDrivers.tsx
    └── PanelToggles.tsx
```

## Stores (Zustand)

```
stores/
├── dataStore.ts        F1State + merge application  (see 07)
├── connectionStore.ts  ws status, reconnect attempts, skew estimate
├── delayStore.ts       delay value + buffer         (see 08)
├── replayStore.ts      playing, position, speed, duration
└── settingsStore.ts    persisted to localStorage
```

### The one rule that matters

**Components subscribe to the narrowest possible slice.** A `DriverRow` for car 44 must re-render only when car 44's timing changes.

```typescript
const timing = useDataStore(
  useShallow(s => s.state?.timing_data?.Lines?.[driverNumber])
);
```

Not `useDataStore(s => s.state)`. Ever.

The track map goes further and bypasses React entirely for position updates: subscribe imperatively, write to DOM refs in a rAF loop. See `10-track-map.md`.

## WebSocket client

`lib/ws.ts`:

```typescript
class LiveClient {
  connect(url: string): void
  disconnect(): void
  onMessage(cb: (m: ServerMessage) => void): void
  send(m: ClientMessage): void
}
```

- Exponential backoff on disconnect: 1s → 30s, with jitter.
- Reconnect sends `?since=<lastStreamId>` to try resumption.
- Heartbeat: send `{"type":"ping"}` every 20 s; if no `pong` within 10 s, force-reconnect. This catches dead-but-open sockets, which are common on mobile networks and behind corporate proxies.
- Tab visibility: on `visibilitychange` to hidden, keep the socket but stop the rAF render loop. On visible, resync.

## Rendering budget

Hard targets, verified in DevTools with a recorded race at 1x:

| Metric | Target |
|---|---|
| Long tasks (>50 ms) | 0 |
| React commits per second, steady state | < 15 |
| Track map frame rate | 60 fps |
| Memory growth over 2 h | < 100 MB |

Techniques required to hit these:
1. rAF-batched delta application (never per-message `setState`).
2. `React.memo` on `DriverRow`, `MiniSectors`, `CarDot`.
3. Refs + direct DOM writes for the map.
4. uPlot (not Recharts/Chart.js) for telemetry traces.
5. No `key={index}` anywhere in the timing tower — key by driver number, or reordering animations will tear.

## Timing tower behaviour details

These are the details that separate "looks like f1-dash" from "looks amateur":

- **Position changes animate.** Rows move with a FLIP transition, not an instant reorder. `framer-motion`'s `Reorder` or a manual FLIP.
- **Sector times flash.** A new personal best flashes green for ~1.5 s, an overall fastest flashes purple, then settles to the resting colour.
- **Gap column toggles** between gap-to-leader and interval-to-car-ahead on click, persisted.
- **Purple/green/yellow logic** comes from `PersonalFastest` / `OverallFastest` booleans on the timing values, not from comparing numbers yourself.
- **`GapToLeader` can be `"1 L"`** (lapped) or empty. Render lapped drivers distinctly.
- **DRS indicator** from `CarData.drs >= 10`.
- **Favourite drivers** get a highlighted row and a white ring on the map dot.

## Theming

CSS variables, dark by default:

```css
:root {
  --bg: #0a0a0a; --surface: #141414; --border: #262626;
  --text: #fafafa; --text-dim: #a1a1a1;
  --purple: #b16cea; --green: #43b02a; --yellow: #ffd12e; --red: #da291c;
  --track: #2a2a2a; --drs: #2b7fff;
}
```

Team colours are injected at runtime from `DriverList.TeamColour` — never hardcoded.

## Accessibility

- Track status must not be conveyed by colour alone — the SC/VSC/red banner carries text.
- Timing tower is a `<table>` with proper headers, not a stack of divs.
- Respect `prefers-reduced-motion`: disable row reorder animation and position interpolation.

## Acceptance criteria

- [ ] Dashboard renders a full recorded race with all panels populated.
- [ ] Rendering budget table above is met.
- [ ] Disconnecting the network shows a clear reconnecting state and recovers without a reload.
- [ ] All panels work at 375 px width.
- [ ] Replay controls drive the server engine and the UI reflects seek/pause/speed within 200 ms.
