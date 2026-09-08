---
date: 2026-08-24T19:40:30+02:00
researcher: Claude (10x-research)
git_commit: 0c9eaaf9dfae532c5340b8f7ac9effd81db4af9b
branch: dev
repository: 10xDevs (RideForge)
topic: "S-01 north-star (generate-route-preview): current state, gap to outcome, build architecture, risks"
tags: [research, codebase, s-01, north-star, route-generation, map-preview, frontend, backend]
status: complete
last_updated: 2026-08-24
last_updated_by: Claude (10x-research)
---

# Research: S-01 — Generate route from start + length, show on map (north star)

**Date**: 2026-08-24T19:40:30+02:00
**Researcher**: Claude (10x-research)
**Git Commit**: 0c9eaaf9dfae532c5340b8f7ac9effd81db4af9b
**Branch**: dev
**Repository**: 10xDevs (RideForge)

## Research Question

Roadmap slice **S-01 `generate-route-preview`** — the north star
([roadmap.md:98-111](context/foundation/roadmap.md)):

> Outcome: jeździec wpisuje lokalizację startu i długość przejazdu, klika Generuj i widzi
> trasę narysowaną na mapie, wychodzącą z podanego startu.
> PRD refs: US-01, FR-001, FR-002, FR-005, FR-006, NFR-01.

What exists in the codebase toward S-01, what's missing, how the pieces fit, and what
must be decided before it can be planned.

## Summary

**S-01 is now unblocked** — its prerequisites F-01 (mobile↔backend link) and F-02
(stitching adapter) are both `done`. But the north star's hard middle is entirely absent.
The codebase gives S-01 two solid bookends and no core:

- **Bookend 1 — input shell (from F-01, unwired).** `src/app/index.tsx` is already a "Plan
  your ride" screen: start location (auto-detected via `expo-location`), a destination
  field (*"leave blank for a loop"*), and a 5-level curviness selector. But the **Plan Route
  button only `console.log`s** ([index.tsx:205-208](src/app/index.tsx)) — nothing calls the
  backend. It also has **no ride-length input** (FR-002), and the origin is a **free-text
  address string, never geocoded to coordinates**.
- **Bookend 2 — stitching backend (F-02, complete).** `POST /route/stitch` turns a
  *caller-supplied* waypoint list into geometry+distance+duration
  ([Program.cs:67-90](api/Program.cs)). Solid, provider-agnostic, ready to consume.
- **Missing core — everything that makes it "generate".** (1) the **waypoint-generation
  algorithm** and a **`/route/generate`** endpoint do not exist; (2) the **map preview** does
  not exist — `react-native-maps` **is not even in `package.json`**; (3) the **end-to-end
  wiring** (a generation client + mutation hook, forward-geocoding, the length input) is
  absent.

The single riskiest gap is the generation algorithm — the roadmap already calls S-01 *"the
heaviest and most uncertain slice (novel algorithm + 30 s limit)"*
([roadmap.md:110](context/foundation/roadmap.md)). The map layer is the biggest **non-code**
prerequisite: `react-native-maps` doesn't run in Expo Go, so S-01 forces an **EAS dev build**
(memory `northstar-tech-decisions`; roadmap.md:106-107).

A scope inversion to reconcile: the current form already exposes **curviness (S-02 / FR-003)**
but is **missing ride length (S-01 / FR-002)** — the UI got ahead of the roadmap on the wrong
axis. And the *"leave blank for a loop"* field answers the open question from the Risk #1
research: **the product supports both loops and point-to-point rides**, so loop-closure is a
conditional property, not a universal one.

## Detailed Findings

### A. Input shell — exists but unwired (delivered by F-01)

- **Plan screen** ([src/app/index.tsx](src/app/index.tsx)) has:
  - Start location, auto-detected: `expo-location` permission → `getCurrentPositionAsync` →
    reverse-geocode (native `reverseGeocodeAsync`; web via Nominatim) into a display string
    ([index.tsx:57-104](src/app/index.tsx)).
  - Destination field with loop semantics: *"Destination — leave blank for a loop"*
    ([index.tsx:154-163](src/app/index.tsx)); the button computes `isLoop = destination is empty`
    ([index.tsx:206](src/app/index.tsx)).
  - Curviness selector, 5 discrete levels (Highways→Extreme), not a slider
    ([index.tsx:31-37,171-197](src/app/index.tsx)).
  - **Dead-end submit**: `onPress` only `console.log`s the inputs
    ([index.tsx:205-208](src/app/index.tsx)) — no API call, no navigation, no result surface.
- **Provenance**: this screen shipped with **F-01** (commit `5423ad1`
  "feat(mobile-backend-link): TanStack Query provider + connectivity proof"), not a separate
  slice. `BackendStatus` confirms the intent: it's a dev-only pill and its doc-comment says
  *"S-01 owns any user-facing surface"* ([backend-status.tsx:11](src/components/backend-status.tsx)).
  So the shell is deliberately stubbed, waiting for S-01 — **not** undocumented drift. (The
  roadmap baseline line "Brak ekranów RideForge" is simply stale, [roadmap.md:59](context/foundation/roadmap.md).)
- **API client convention S-01 must copy** (F-01):
  - `request<T>` owns transport + a 30 s `AbortController` timeout + normalized errors
    ([client.ts:19-54](src/api/client.ts)).
  - `ApiError` with machine-branchable `kind` (`network|timeout|http|parse`)
    ([errors.ts:1-19](src/api/errors.ts)) — the FR-005 loading/error contract.
  - `useHealthQuery` is explicitly *"the template S-01 copies for generation"*
    ([use-health-query.ts:7](src/hooks/use-health-query.ts)); `queryClient` retries once,
    `staleTime` 30 s ([query-client.ts](src/api/query-client.ts)).
  - `DEFAULT_TIMEOUT_MS = 30_000` is already sized to NFR-01
    ([config.ts:16](src/api/config.ts)); `API_BASE_URL` falls back to the live Railway URL.
- **Permissions ready**: `expo-location` is a dependency; `app.json` declares iOS
  `NSLocationWhenInUseUsageDescription` + Android `ACCESS_*_LOCATION`
  ([app.json:10-25](app.json)).

### B. Stitching backend — complete and ready to consume (F-02)

- `POST /route/stitch` validates input, calls `IRouteStitcher.StitchAsync`, maps failures to
  400/422/502/504 ([Program.cs:67-90](api/Program.cs)). Default provider `fake` (passthrough,
  no network); `openrouteservice` is fully implemented
  ([OpenRouteServiceStitcher.cs](api/Routing/OpenRouteServiceStitcher.cs)).
- Provider-agnostic contract: `Coord(Lat,Lng)`, `RouteRequest(Waypoints)`,
  `StitchedRoute(Geometry,Distance,Duration)` ([RouteModels.cs](api/Routing/RouteModels.cs)).
  `RouteModels.cs:11` already reserves the seam: *"The (future) curviness algorithm produces
  this; the endpoint hands it in directly for now."*
- ORS timeout ceiling 10 s, kept under the 30 s NFR
  ([RouteStitchingOptions.cs:25](api/Routing/RouteStitchingOptions.cs), [Program.cs:27-30](api/Program.cs)).

### C. Generation core — entirely absent (the north star's hard part)

- **No `/route/generate` endpoint.** The API exposes only `GET /health` and
  `POST /route/stitch` ([Program.cs:62,67](api/Program.cs)). Nothing accepts `{start, length}`.
- **No waypoint-generation algorithm.** Confirmed last turn (Risk #1 research): `api/Routing/`
  is exclusively the stitcher; a search for `curviness|shaping|generate` in `api/` returns only
  two "future" doc-comments. Even a *minimal, curviness-agnostic* S-01 needs a generator that
  emits waypoints forming a route of ~requested length departing from the start — that code
  does not exist. This is the roadmap's "most uncertain slice"
  ([roadmap.md:110](context/foundation/roadmap.md)) and the product's riskiest thesis
  ([roadmap.md:20](context/foundation/roadmap.md)).
- **No generation client on the frontend.** `src/api/` has only `config/errors/client/health/
  index/query-client`; no `route.ts`, no `useGenerateRouteMutation`. A repo search for
  `route/generate|generateRoute|useGenerate` in `src/` finds nothing relevant.

### D. Map preview — not installed, and gated on an EAS dev build

- **`react-native-maps` is not a dependency** ([package.json:5-32](package.json)) and is imported
  nowhere in `src/`; the only mentions live in docs (`roadmap.md`, the F-02 archive). `expo-maps`
  is also absent. So FR-006 has zero code *and* zero dependency today.
- **Decision already recorded** (not yet acted on): map = `react-native-maps` `<Polyline>` +
  `fitToCoordinates`; `expo-maps` rejected (alpha, iOS 18+, no Expo Go)
  ([roadmap.md:106](context/foundation/roadmap.md); memory `northstar-tech-decisions`).
- **Tooling consequence**: `react-native-maps` has native code → **does not run in Expo Go** →
  S-01 requires an **EAS dev build** ([roadmap.md:107](context/foundation/roadmap.md)). No EAS
  config (`eas.json`) is present. This is the largest non-code prerequisite of the slice.
- **Geometry hand-off**: the backend returns `Coord{lat,lng}`; `react-native-maps` `Polyline`
  wants `{latitude, longitude}` — a field-name mapping the client must apply
  ([research-stitching.md:87-88](context/archive/2026-08-16-route-stitching-adapter/research-stitching.md)).

### E. Scope / form reconciliation (things that are *misaligned*, not just missing)

- **Missing FR-002 length input.** S-01's outcome is "start **+ długość**", but the form has no
  duration/distance field — it has curviness and destination instead. Length must be added,
  and it's what the algorithm targets (±20%, US-01).
- **Origin is a string, not coordinates.** The form captures/derives a human address
  ([index.tsx:39-47,92](src/app/index.tsx)); the backend needs `Coord{lat,lng}`. **Forward**
  geocoding of the typed origin is absent (only **reverse** geocoding exists, for the auto-detect
  display). Someone must turn the address into lat/lng — client-side or via a backend endpoint.
- **Curviness is ahead of schedule.** The UI already exposes FR-003 (S-02), which the roadmap
  sequences *after* S-01. S-01 must decide whether to ignore curviness for its first cut
  (generate a ~length route, curviness-agnostic) or thread the level through immediately.
- **Loops are a real, first-class mode.** *"leave blank for a loop"* means S-01 must handle both
  a **loop** (return to start) and a **point-to-point** ride. This resolves the open loop
  question from the Risk #1 research — loop-closure is conditional on an empty destination.

### F. The US-01 / NFR-01 guarantees S-01 must hit

- **±20% of requested length** (US-01 acceptance; roadmap S-01 unknown
  [roadmap.md:109](context/foundation/roadmap.md)) — the generator must aim the stitched
  route's length/duration at the request, asserted against the *request* not the algorithm's
  own number (Risk #4).
- **Departs from start** (PRD guardrail, [prd.md:42](context/foundation/prd.md)) — first route
  point ≈ requested start.
- **30 s budget** (NFR-01) — client timeout is 30 s; ORS per-call ceiling 10 s; the algorithm's
  own compute + the stitch round-trip must fit inside 30 s (Risk #5).

## Code References

- `src/app/index.tsx:49-219` — Plan screen: origin auto-detect, destination/loop, curviness; button `console.log` only (205-208); **no length input**
- `src/components/backend-status.tsx:11` — "S-01 owns any user-facing surface" (shell is intentionally stubbed)
- `src/api/client.ts:19-54` — `request<T>` transport + 30 s timeout + error normalization (S-01 builds on this)
- `src/api/errors.ts:1-19` — `ApiError` kinds (network/timeout/http/parse) = FR-005 error contract
- `src/hooks/use-health-query.ts:7` — "the template S-01 copies for generation"
- `src/api/config.ts:16` — `DEFAULT_TIMEOUT_MS = 30_000` sized to NFR-01
- `api/Program.cs:62,67-90` — only `/health` + `/route/stitch`; **no `/route/generate`**
- `api/Routing/RouteModels.cs:11` — seam reserved for "the (future) curviness algorithm"
- `package.json:5-32` — deps: `expo-location` present; **`react-native-maps` absent**; no test runner
- `app.json:10-25` — location permissions configured; no map config plugin, no EAS/dev-build config

## Architecture Insights

- **S-01 = fill the middle between two finished bookends.** The build shape the code implies: a
  new `POST /route/generate` takes `{start, length[, curviness]}` → the (new) algorithm emits an
  ordered waypoint list → hand that straight to the **existing** `IRouteStitcher` → return
  `StitchedRoute`. F-01 (client + error convention) and F-02 (stitcher) are reused wholesale;
  **~all the risk is in the algorithm and the map/EAS step**, not the plumbing.
- **ORS being the only implemented stitcher pre-commits "pattern A" (sparse waypoints).** The
  F-02 research says the algorithm's output *shape* decides the provider: sparse ordered
  waypoints → Directions/ORS (built); a dense track → map-matching (OSRM/GraphHopper, not built)
  ([research-stitching.md:53-63](context/archive/2026-08-16-route-stitching-adapter/research-stitching.md)).
  So designing the algorithm to emit **sparse waypoints** keeps the existing backend; a dense
  track would reopen the provider decision.
- **The generation call is a mutation, not a query.** Unlike `useHealthQuery`, generation is a
  user-triggered POST with side-effecting cost (a billed ORS call, Risk #7) — model it as a
  react-query `useMutation`, reuse `ApiError.kind` for the FR-005 loading/error UI, and the 30 s
  timeout is already in place.
- **The map is a workflow gate, not just a component.** Adding `react-native-maps` means an EAS
  dev build and a config-plugin/native rebuild — plan it as a setup task *before* the preview UI,
  or S-01 has no way to render its own output on device.

## Historical Context (from prior changes)

- `context/archive/2026-08-11-mobile-backend-link/` (F-01) — delivered the typed client, the
  react-query provider, `BackendStatus`, and the Plan-screen shell that S-01 now wires up.
- `context/archive/2026-08-16-route-stitching-adapter/` (F-02) — the stitching boundary S-01
  consumes; provider choice deliberately deferred until the algorithm's output shape is known.
- `context/archive/2026-08-16-route-stitching-adapter/research-stitching.md` — the A-vs-B provider
  criterion (sparse waypoints vs dense track) and the `Coord`→`{latitude,longitude}` client note.

## Related Research

- `context/changes/testing-route-shaping-quality/research.md` (test-plan Risk #1) — S-01 (this
  slice) and S-02 are its named blockers; that doc's open "must rides be loops?" question is
  **answered here**: the UI offers both (blank destination = loop), so loop-closure is conditional.

## Open Questions

Decisions the user owns before `/10x-plan` on S-01:

1. **Curviness in S-01, or defer to S-02?** The UI already exposes the 5-level selector, but the
   roadmap sequences curviness as S-02. Does S-01's first algorithm ignore it (generate a
   ~length route, curviness-agnostic) or thread the level through from day one?
2. **Ride length: duration (h) or distance (km) as the S-01 primary?** (FR-002 offers both.)
   Distance is the more direct algorithm target; duration additionally needs a speed model (the
   `fake` uses 50 km/h; ORS returns a real duration).
3. **Forward-geocoding of the typed origin** — client-side (`expo-location`/Nominatim, already
   used for reverse) or a new backend geocode endpoint? The backend needs `Coord{lat,lng}`; the
   form currently holds a string.
4. **Algorithm output shape — sparse waypoints vs dense track?** This is the deferred F-02
   blocking question, now due: sparse keeps the existing ORS/pattern-A backend; dense reopens the
   provider choice toward map-matching. It gates both the algorithm design and the provider.
5. **Loop vs point-to-point for the first cut.** The UI offers both; the north-star framing is
   loop-oriented. Does S-01 support both immediately, or ship one mode first?
6. **Map/EAS now.** Install `react-native-maps` and stand up an EAS dev build as part of S-01
   (there's no other way to preview the route on device). Confirm the dev-build workflow before
   the preview UI is built.
