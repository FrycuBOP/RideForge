# S-01 — Generate route from start + length, show on map — Plan Brief

> Full plan: `context/changes/s-01/plan.md`
> Research: `context/changes/s-01/research.md`

## What & Why

The north star: prove RideForge *generates* a ride rather than editing one. A rider enters a
start and a ride **distance**, taps Generate, and sees a **loop** drawn on a map that departs
from their start — the first end-to-end pass of RideForge's own algorithm plus external
stitching, and the earliest point the product thesis meets real feedback.

## Starting Point

Two finished bookends, no middle. The Plan screen ([src/app/index.tsx](src/app/index.tsx))
already has origin auto-detect + a curviness selector but the button only `console.log`s and
there's no distance input. The stitching backend (F-02) turns caller-supplied waypoints into a
road route ([api/Program.cs:67-90](api/Program.cs)). Missing: the generation algorithm +
`/route/generate`, the client wiring, and the map (`react-native-maps` isn't even installed).

## Desired End State

On an EAS dev build, start + distance → Generate → a results screen draws a loop departing from
and returning to the start, with distance/duration stats, inside 30s, landing within ±20% of the
requested distance.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Algorithm output shape | Sparse ordered waypoints | Keeps the built ORS stitcher (pattern A); no provider reopen | Plan |
| First-cut algorithm | Simple geometric loop | Proves the pipe + ±20% with minimal risk; honest S-02 baseline | Plan |
| Curviness in S-01 | Deferred; selector inactive | Matches roadmap sequencing; keeps S-01 shippable | Plan |
| Ride length | Distance (km) primary | Direct algorithm target; no speed model needed | Plan |
| Geocoding | Client-side (native `geocodeAsync` / web Nominatim) | Reuses existing split; backend stays coordinate-only | Plan |
| Route mode | Loop-first | Matches north-star + UI default; one path to get right | Plan |
| Map / EAS | Dev build + react-native-maps as Phase 1 | Only way to preview on device; tooling gate first | Plan |
| Result surface | Separate typed `/result` screen | Clean separation, room for S-04 GPX | Plan |

## Scope

**In scope:** `/route/generate` + geometric loop generator (sparse waypoints → ORS); distance
input; client-side forward geocoding; generate mutation + loading/error UI; EAS dev build +
`react-native-maps`; results screen with map + stats; generator unit tests.

**Out of scope:** curviness shaping (S-02), pace (S-03), GPX (S-04), POI (S-08), radius (S-09),
auth/save (S-05–07); point-to-point routing; duration-primary input; backend geocoding;
map-matching / provider swap; a measure-and-adjust re-generation loop; the full Risk #1/#4 test
harness.

## Architecture / Approach

Generation sits *above* the existing `IRouteStitcher`: `POST /route/generate` takes
`{start, distanceKm}`, the new `RouteGenerator` emits a closed loop of sparse waypoints (radius
from `distanceKm / detourFactor`), the existing stitcher turns them into a road route, and the
same response DTO flows back. The client geocodes the origin, calls the endpoint via a
react-query mutation, stashes the result in a small in-memory store (not the URL — the polyline
is too big), and navigates to a `react-native-maps` results screen.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Map & dev-build foundation | react-native-maps + EAS dev build; bare MapView renders | EAS setup friction; Android Google Maps API key |
| 2. Route-generation backend | `/route/generate` + geometric loop generator + xUnit invariants | Hitting ±20% single-shot against real ORS road inflation |
| 3. Client wiring | Distance input, geocoding, generate mutation, inactive curviness | expo-location has no web → native/web geocode split |
| 4. Results screen + end-to-end | Map + Polyline + stats; full north-star flow on device | `fitToCoordinates` Android timing; passing geometry without URL params |

**Prerequisites:** F-01 + F-02 done (both are); a Google Cloud "Maps SDK for Android" key; an
EAS account for the dev build.
**Estimated effort:** ~4 sessions, one per phase (Phase 1 gated by EAS build turnaround).

## Open Risks & Assumptions

- **±20% single-shot** against real-road inflation may need `detourFactor` tuning per region;
  if it can't hold single-shot, a corrective loop is deferred (would threaten the 30s NFR).
- **Android map key** must be provisioned before Phase 1's map renders on Android.
- **Web build**: `react-native-maps`/`expo-location` are native-only; if web is still built,
  the results screen and geocoder need web guards/fallbacks.
- Nominatim (web geocoding) usage policy — keep to single low-rate lookups.

## Success Criteria (Summary)

- A rider generates a loop from start + distance and sees it drawn on a map departing from the
  start, on a real dev build.
- Stitched length within ±20% of requested km; whole flow under 30s.
- Generator unit tests (loop closure, ±20% vs fake, non-degenerate) pass with `dotnet test`.
