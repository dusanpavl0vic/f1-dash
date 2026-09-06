# 10 — Track Map

The single most-visible component. Get the coordinate handling right once, on the server, and the client becomes trivial.

## The core insight

**Track outline and live car positions must share a coordinate system.**

| Source | Coordinate system | Usable? |
|---|---|---|
| `f1-circuits` GeoJSON, OSM | WGS84 lat/lon | ❌ needs projection + manual fitting |
| Hand-drawn circuit SVGs | arbitrary | ❌ never aligns |
| Driver's fastest lap telemetry (FastF1 `pos_data`) | F1 native | ⚠️ works, but wobbly and driver-dependent |
| **MultiViewer circuits API** | **F1 native — same as `Position.z`** | ✅ **use this** |

MultiViewer's `x[]`/`y[]` arrays are in the same native F1 units (~1/10 m) as the live `Position.z` stream. Car dots land on the outline with zero fitting.

> Do **not** draw the track from a driver's telemetry lap. Different drivers take different lines, so the outline would visibly change depending on whose lap you sampled, and it would jitter through corners.

## Server-side transform (in `api`, endpoint `/api/track/{key}/{year}`)

Do all of this once, server-side, and cache forever.

### Step 1 — Rotate

```python
import numpy as np

def rotate(xy: np.ndarray, degrees: float) -> np.ndarray:
    a = np.radians(degrees)
    m = np.array([[np.cos(a), np.sin(a)], [-np.sin(a), np.cos(a)]])
    return xy @ m
```

Apply `circuit["rotation"]` so the map matches the orientation used in official broadcasts.

### Step 2 — Flip Y

F1 coordinates have **Y increasing upward**. SVG has **Y increasing downward**. Without a flip the circuit renders mirrored.

Negate Y after rotating: `xy[:, 1] *= -1`.

### Step 3 — Build the path and viewBox

```python
def build_path(xy: np.ndarray) -> str:
    pts = " L ".join(f"{x:.0f},{y:.0f}" for x, y in xy)
    return f"M {pts} Z"

pad = 0.04
minx, miny = xy.min(axis=0); maxx, maxy = xy.max(axis=0)
w, h = maxx - minx, maxy - miny
view_box = {"x": minx - w*pad, "y": miny - h*pad,
            "width": w * (1 + 2*pad), "height": h * (1 + 2*pad)}
```

### Step 4 — Apply the identical transform to everything

Corners, marshal sectors, DRS zones, start/finish, **and every live car position** go through the *same* rotate + flip. Export the transform as a small function the client can also call:

```typescript
// returned in the /api/track response
{ "transform": { "rotation": 227, "flipY": true } }
```

```typescript
export function toScreen(x: number, y: number, t: Transform): [number, number] {
  const a = (t.rotation * Math.PI) / 180;
  const rx =  x * Math.cos(a) + y * Math.sin(a);
  const ry = -x * Math.sin(a) + y * Math.cos(a);
  return [rx, t.flipY ? -ry : ry];
}
```

**Both the outline (server) and the car dots (client) must use identical maths.** A test fixture with a known input/expected-output pair should be shared between them.

## Rendering

```tsx
<svg viewBox={`${vb.x} ${vb.y} ${vb.width} ${vb.height}`} className="w-full h-full">
  {/* marshal sectors — coloured by flag state, drawn under the track */}
  {sectors.map(s => (
    <path key={s.number} d={s.path} fill="none"
          stroke={sectorColour(s.number, trackStatus, raceControl)}
          strokeWidth={340} strokeLinecap="round" opacity={0.55} />
  ))}

  {/* track surface */}
  <path d={track.path} fill="none" stroke="var(--track)" strokeWidth={260}
        strokeLinejoin="round" strokeLinecap="round" />

  {/* DRS zones */}
  {drsZones.map((z, i) => (
    <path key={i} d={z.path} fill="none" stroke="var(--drs)" strokeWidth={80} />
  ))}

  {/* start/finish */}
  <line {...startFinish} stroke="white" strokeWidth={90} />

  {/* cars */}
  {drivers.map(d => (
    <CarDot key={d.number} driver={d} pos={positions[d.number]} transform={t} />
  ))}
</svg>
```

Stroke widths are in **track units** (~1/10 m), so 260 ≈ 26 m — roughly a real track width. They scale automatically with the viewBox; do not use pixel values.

## Car dots

```tsx
<g transform={`translate(${sx}, ${sy})`}>
  <circle r={220} fill={`#${driver.TeamColour}`}
          stroke={isFavourite ? "#fff" : "none"} strokeWidth={60} />
  <text x={300} y={90} fontSize={420} fill="#fff"
        className="select-none pointer-events-none">{driver.Tla}</text>
</g>
```

Handling:
- **Hidden drivers.** `PositionEntry.Status !== "OnTrack"` → render at 30% opacity or hide entirely (user preference). Cars in the pits report positions that wander outside the outline.
- **Retired drivers.** `TimingDataDriver.Retired` → remove from the map.
- **Missing driver.** A number in `Position` with no `DriverList` entry → skip and log once, do not crash.

## Smoothing

`Position.z` arrives at ~4 Hz. Rendering dots at 4 Hz looks like stop-motion.

Interpolate on the client:

```typescript
// keep last two known positions per driver, lerp between them on rAF
const alpha = Math.min(1, (now - prev.t) / (next.t - prev.t));
const x = prev.x + (next.x - prev.x) * alpha;
const y = prev.y + (next.y - prev.y) * alpha;
```

- Use **linear** interpolation, not spline. Splines overshoot on hairpins and cars visibly cut across the grass.
- If the gap between updates exceeds 3 s, stop interpolating and snap — the driver has probably pitted or the feed stalled.
- Do the lerp inside a `requestAnimationFrame` loop that reads from a ref, **not** through React state. Setting React state 60×/s for 20 drivers will melt the main thread.

## Marshal sector colouring

`RaceControlMessages` with `Scope: "Sector"` carry a `Sector` number and a `Flag`. Map:

| Flag | Colour |
|---|---|
| `YELLOW` | `#ffd12e` |
| `DOUBLE YELLOW` | `#ffd12e` (add a pulse animation) |
| `RED` | `#da291c` |
| `CLEAR` / `GREEN` | back to default |

Global `TrackStatus` of `4` (SC) or `6` (VSC) overrides all sectors to yellow.

Sector paths: MultiViewer gives each marshal sector a `trackPosition` and `length`. Slice the main outline point array between consecutive marshal-sector start points to build each sector's own path. Precompute this server-side and return it in `marshalSectors[].path`.

## Responsive layout

- Mobile: track map full width, timing tower below, telemetry hidden behind a tab.
- Desktop: 3-column — tower | map | telemetry+weather.
- The SVG's `preserveAspectRatio="xMidYMid meet"` handles all scaling. No JS resize handlers.

## Acceptance criteria

- [ ] Bahrain, Monaco, Suzuka, and Las Vegas all render recognisably and match their official broadcast orientation.
- [ ] Live car dots stay on the track surface throughout a full recorded race lap — no dot leaves the stroke width.
- [ ] Motion looks smooth (interpolated), not steppy.
- [ ] 20 cars animating at 60 fps with no React re-render per frame (verify with the React DevTools profiler: zero commits during steady-state animation).
- [ ] A yellow flag in sector 7 visibly colours only that sector.
