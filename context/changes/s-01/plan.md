# S-01 — Generate route from start + length, show on map (north star)

## Overview

Deliver the north-star flow end-to-end: a rider enters a start location and a ride
**distance**, taps Generate, and sees a **loop** route drawn on a map that departs from
their start. This fills the missing middle between the two finished bookends — the F-01
input shell and the F-02 stitching backend — by adding: a `POST /route/generate` endpoint
backed by a **simple geometric loop generator** (emits sparse ordered waypoints → the
existing OpenRouteService stitcher), the client wiring (distance input, forward-geocoding,
a generate mutation), and a **map results screen** on an EAS **dev build** (react-native-maps
does not run in Expo Go).

## Current State Analysis

- **Input shell exists but is unwired** ([src/app/index.tsx](src/app/index.tsx)): origin
  (auto-detected via `expo-location`), destination (*"leave blank for a loop"*), a 5-level
  curviness selector. The Plan button only `console.log`s ([index.tsx:205-208](src/app/index.tsx)).
  **No ride-length input.** Origin is a free-text/address string, never geocoded to lat/lng.
- **Stitching backend is complete** (F-02): `POST /route/stitch` turns a caller-supplied
  waypoint list into `StitchedRoute{Geometry,Distance,Duration}` ([api/Program.cs:67-90](api/Program.cs));
  `OpenRouteServiceStitcher` (pattern A, sparse waypoints) and a `fake` passthrough both
  implement `IRouteStitcher`. The seam already reserves the generator:
  [RouteModels.cs:11](api/Routing/RouteModels.cs).
- **The generation core is entirely absent**: no `/route/generate`, no waypoint-generation
  algorithm, no client generation module, no map. `react-native-maps` **is not in
  `package.json`** ([package.json:5-32](package.json)); no `eas.json`.
- **Client convention to reuse** (F-01): `request<T>` with a 30s abort timeout
  ([src/api/client.ts:19-54](src/api/client.ts)), `ApiError.kind`
  (`network|timeout|http|parse`, [errors.ts](src/api/errors.ts)), react-query
  (`useHealthQuery` is *"the template S-01 copies for generation"*,
  [use-health-query.ts:7](src/hooks/use-health-query.ts)), `DEFAULT_TIMEOUT_MS = 30_000`
  already sized to NFR-01 ([config.ts:16](src/api/config.ts)).
- **Grounded library facts** (Context7, Expo SDK 56): react-native-maps installs via the
  `"react-native-maps"` config plugin (SDK 53+), needs a custom dev build, and needs an
  **Android Google Maps API key** (iOS uses Apple Maps, keyless); `Polyline` takes
  `{latitude, longitude}`; `fitToCoordinates` must run from `onLayout` on Android.
  `expo-location` is **native-only (no web)** — forward geocoding must split native
  `geocodeAsync` vs web Nominatim, mirroring the existing reverse-geocode code.

## Desired End State

On an EAS dev build, a rider on the Plan screen sets a start (auto-detected or typed) and a
distance in km, taps **Plan Route**, and within the 30s budget is taken to a results screen
showing a **loop** polyline drawn on a map, departing from (and returning to) the start, with
distance/duration stats. The stitched route length lands within **±20%** of the requested
distance. Verified by: `dotnet test` (generator invariants) + `dotnet build` + `npm run lint`
+ `npx tsc --noEmit` all green, and a manual device run of the full flow.

### Key Discoveries:

- Reuse the whole F-02 stitcher — generation sits *above* `IRouteStitcher`, so the risk is
  the algorithm + the map/EAS step, not the plumbing ([research.md](context/changes/s-01/research.md)).
- Sparse waypoints keep the existing ORS backend; a dense track would reopen the F-02
  provider decision (research §Architecture Insights).
- `expo-location` has no web target — the existing screen already proves the native/web
  geocode split ([index.tsx:71-91](src/app/index.tsx)); forward geocoding follows suit.
- Generation is a POST with side-effecting cost (a billed ORS call) — model it as a
  react-query `useMutation`, not a query.
- **Navigation is flat (found during Phase 1).** The root `src/app/_layout.tsx` renders
  `NativeTabs` directly (tabs `index`, `explore`); there is **no root `<Stack>`**, and `result`
  is neither a tab nor on a stack, so `router.push('/result')` is a silent no-op. Expo SDK 56
  requires a root `<Stack>` wrapping a `(tabs)` group to push a non-tab route (docs: *nesting
  navigators / Stack inside native tabs*). This is now folded into Phase 1.

## What We're NOT Doing

- **Curviness shaping (S-02 / FR-003).** The selector stays visible but **inactive** for
  S-01; the generator is curviness-agnostic. Risk #1's "does the slider change the route"
  thesis is explicitly out of scope here.
- **Pace fast/touristic (S-03), GPX download (S-04), POI (S-08), radius (S-09), auth/save
  (S-05–S-07).**
- **Point-to-point routing.** S-01 is loop-only (destination field inert); A→B deferred.
- **Duration-primary input.** Distance (km) is the target; duration is a derived display.
- **Backend geocoding / provider swap / map-matching.** Geocoding is client-side; ORS stays.
- **A measure-and-adjust re-generation loop.** Single-shot generation only (see Performance).
- **The full Risk #1/#4 test harness** (test-plan Phase 2, change `testing-route-shaping-quality`)
  — S-01 ships generator unit tests, not the independent-oracle metric suite.

## Implementation Approach

Front-load the **EAS dev build + map** as the tooling gate (Phase 1) so "can we preview on
device" is answered before anything depends on it. Build the **backend generator** (Phase 2)
in isolation, testable with `dotnet test` against the `fake` stitcher. Then **wire the client**
(Phase 3: distance input, geocoding, mutation) and finally the **results map screen** (Phase 4)
that closes the loop end-to-end. Each phase is independently verifiable; only Phase 4 needs
all prior phases.

## Critical Implementation Details

- **Do not pass the polyline through expo-router params.** A generated loop is dozens–hundreds
  of points; URL-serializing it is fragile and size-bounded. Hand the result to the results
  screen via a shared in-memory store (a small React context or a module-level singleton /
  the react-query cache), and navigate with no route data in the URL.
- **`fitToCoordinates` timing.** On Android it throws if called during mount — call it from the
  `MapView` `onLayout` handler (Context7/react-native-maps).
- **Road-detour factor.** The `fake` stitcher returns straight lines (deterministic), so the
  generator's geometry is exactly measurable in tests; ORS inflates length via real roads
  (~1.2–1.4×). The generator sizes the loop from `distanceKm / detourFactor` with a tunable
  constant, single-shot. xUnit asserts the **geometric** sizing against the fake; the **±20%
  against real roads** is a manual device check (Phase 4), and the detour constant is tuned there.
- **30s budget (NFR-01).** One generation = one ORS round-trip (10s provider ceiling already
  set, [RouteStitchingOptions.cs:25](api/Routing/RouteStitchingOptions.cs)); the client keeps
  the default 30s timeout. No multi-call adjust loop in S-01.
- **Meters→lat/lng offset** (generator): `dLat = (dNorthMeters / R)·(180/π)`,
  `dLng = (dEastMeters / (R·cos(latRad)))·(180/π)`, `R = 6_371_000`. This is the one
  non-obvious conversion; everything else is straightforward geometry.

## Phase 1: Map & dev-build foundation

### Overview

Install and configure `react-native-maps`, stand up an EAS **development** build, and prove a
bare `MapView` renders on a device/emulator. No route data yet — this unblocks the preview.
Also restructures navigation to a root `<Stack>` over a `(tabs)` group so `/result` is
reachable (the flat `NativeTabs` layout can't push a non-tab route), plus the connectivity/web
fixes surfaced while getting the dev build to run.

### Changes Required:

#### 1. Map dependency + config plugin

**File**: `package.json`, `app.json`

**Intent**: Add `react-native-maps` and register its Expo config plugin so the native map
module is linked into the dev build; supply the Android Google Maps API key.

**Contract**: `npx expo install react-native-maps` adds the dep. `app.json` `expo.plugins`
gains `["react-native-maps", { "androidGoogleMapsApiKey": "<key>" }]` (iOS uses Apple Maps,
no key). The key is a Google Cloud "Maps SDK for Android" key; keep it out of source if the
repo goes public (env-injected via `app.config.js`) — for the MVP dev build it may sit in
`app.json`.

#### 2. EAS development build profile

**File**: `eas.json` (new)

**Intent**: Define a `development` build profile (dev client, internal distribution) so
`react-native-maps` native code can run outside Expo Go.

**Contract**: New `eas.json` with a `build.development` profile (`developmentClient: true`,
`distribution: "internal"`) for android + ios. Build via
`eas build --profile development --platform android` (and/or ios), install on device/emulator.

#### 3. Smoke MapView

**File**: `src/app/result.tsx` (new, temporary bare version) or a scratch screen

**Intent**: Render a minimal `MapView` to confirm the dev build shows a map on each platform.

**Contract**: A `MapView` filling the screen, default region. (Superseded by Phase 4's full
results screen — this is just the Phase 1 proof.) Reached via the temporary Plan-Route
navigation in change #5 below.

#### 4. Navigation restructure — root Stack over a `(tabs)` group

**File**: `src/app/_layout.tsx`, `src/app/(tabs)/_layout.tsx` (new), `src/app/(tabs)/index.tsx` (moved), `src/app/(tabs)/explore.tsx` (moved)

**Intent**: Make `/result` reachable. The flat `NativeTabs` root can't push a non-tab route, so
adopt the standard Expo Router structure — a root `<Stack>` wrapping a `(tabs)` route group.
Required by the Phase 1 smoke (to view the map) and by Phase 4 (results navigation).

**Contract**:
- Move `src/app/index.tsx` → `src/app/(tabs)/index.tsx` and `src/app/explore.tsx` →
  `src/app/(tabs)/explore.tsx`. A `(…)` group is not part of the URL, so `/` and `/explore` are
  unchanged; `@/*`-aliased imports are unaffected by the move.
- New `src/app/(tabs)/_layout.tsx` renders the tabs (`<AppTabs />` from
  `src/components/app-tabs.tsx`; the `.web` variant still applies). `NativeTabs.Trigger` names
  `index`/`explore` now resolve within the group.
- Root `src/app/_layout.tsx` keeps the providers (`QueryClientProvider`, `ThemeProvider`,
  `AnimatedSplashOverlay`) and renders a `<Stack>` with
  `<Stack.Screen name="(tabs)" options={{ headerShown: false }} />` and a `result` screen.
  `result.tsx` + `result.web.tsx` stay at `src/app/` as siblings of `(tabs)`, now pushable via
  `router.push('/result')`.

#### 5. Connectivity + web fixes surfaced during implementation (already applied)

**File**: `src/api/config.ts`, `.easignore` (new), `src/app/result.web.tsx` (new), `src/app/index.tsx`

**Intent**: Record fixes made while getting the dev build to run so the plan matches reality.

**Contract**:
- `src/api/config.ts` — corrected the production fallback domain to
  `https://rideforge-production.up.railway.app` (the `-api-` domain 404s). F-01 bug.
- `.easignore` (new) — excludes `.agents/`, `.claude/`, `api/`, `context/` from the EAS archive
  (fixes a Windows symlink `EPERM` during the upload; also slims the build).
- `src/app/result.web.tsx` (new) — web fallback; `react-native-maps` has no web support and
  otherwise crashes the web bundle (`codegenNativeComponent is not a function`).
- `src/app/index.tsx` — the Plan Route button temporarily calls `router.push('/result')` so the
  map is reachable now; Phase 3 replaces it with geocode → generate → navigate.

### Success Criteria:

#### Automated Verification:

- Dependency installed: `react-native-maps` present in `package.json`
- `eas.json` exists with a `development` profile
- Lint passes: `npm run lint`
- Typecheck passes: `npx tsc --noEmit`
- After the restructure, the router resolves the `(tabs)` group and `/result` as a pushable route (typecheck + lint green)

#### Manual Verification:

- `eas build --profile development` produces an installable dev build
- A bare `MapView` renders on Android (Google Maps, key working) and iOS (Apple Maps)
- App still launches and the existing Plan/Explore tabs work in the dev build
- Tapping **Plan Route** opens the `/result` map screen; back returns to the tabs

**Implementation Note**: Pause for human confirmation that the dev build installs and the map
renders before proceeding.

---

## Phase 2: Route-generation backend (endpoint + algorithm)

### Overview

Add the own-algorithm generator that turns `{start, distanceKm}` into sparse loop waypoints,
stitches them via the existing `IRouteStitcher`, and exposes it at `POST /route/generate`.

### Changes Required:

#### 1. Loop waypoint generator

**File**: `api/Routing/RouteGenerator.cs` (new)

**Intent**: Produce an ordered, closed waypoint list (a loop starting and ending at the start)
whose stitched length aims at the requested distance, curviness-agnostic.

**Contract**: `RouteGenerator.GenerateLoop(Coord start, double distanceKm)` →
`IReadOnlyList<Coord>` beginning and ending at `start`. Places N control points (e.g. 6) on a
rough circle/polygon around `start`, radius derived from `distanceKm / detourFactor`, using the
meters→lat/lng offset in Critical Implementation Details. Pure/static so it is unit-testable
without network. A `detourFactor` constant (start ~1.3) is documented as tunable.

#### 2. Generate endpoint

**File**: `api/Program.cs`, `api/Routing/RouteModels.cs`

**Intent**: Accept the rider's request, generate waypoints, hand them to the stitcher, return
the same response DTO the stitch endpoint uses.

**Contract**: New `record GenerateRequestDto(Coord? Start, double? DistanceKm)` in
`RouteModels.cs` (camelCase `start`, `distanceKm`). New `app.MapPost("/route/generate", …)`:
validate (start present + in range via a `RouteValidation` addition; `distanceKm` > 0 and under
a sane ceiling), call `RouteGenerator.GenerateLoop`, then `stitcher.StitchAsync`, return
`StitchResponseDto`. Reuse the existing `RouteStitchException` → 400/422/502/504 mapping.

#### 3. Input validation for generation

**File**: `api/Routing/RouteValidation.cs`

**Intent**: Reject malformed generate requests with a 400 the same way stitch does.

**Contract**: Add `Validate(GenerateRequestDto?)` → `string?` (null when valid): start non-null
and lat/lng in range; `distanceKm` present, > 0, ≤ ceiling (e.g. 500). Mirrors the existing
`Validate(StitchRequestDto?)`.

#### 4. Generator tests

**File**: `api/RideForgeApi.Tests/RouteGeneratorTests.cs` (new)

**Intent**: Pin the generator's oracle-derived invariants against the deterministic `fake`
stitcher (straight-line legs).

**Contract**: xUnit tests asserting: (a) first point ≈ start and last point ≈ start
(**loop closure**, departs-from-start guardrail); (b) stitched length via `fake` is within
**±20%** of `distanceKm`; (c) at least 3 distinct waypoints (not a degenerate out-and-back);
(d) invalid inputs (0/negative distance, out-of-range start) are rejected by `RouteValidation`.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- Tests pass: `dotnet test` (new `RouteGeneratorTests` green)
- Endpoint responds 200 to a valid `POST /route/generate` (fake provider) with non-empty geometry

#### Manual Verification:

- With `RouteStitching__Provider=openrouteservice`, a real generate call returns a plausible
  loop that follows roads and departs from the start
- Length lands within ±20% on real roads for a couple of regions (tune `detourFactor` here)
- Round-trip completes well under 30s

**Implementation Note**: Pause for human confirmation of the real-ORS behaviour and detour tuning.

---

## Phase 3: Client wiring — distance input, geocoding, generate mutation

### Overview

Make the Plan form drive a real generation call: add a distance input, deactivate the curviness
selector, geocode the origin to coordinates, and wire the button through a react-query mutation
to navigation.

### Changes Required:

#### 1. Distance input + inactive curviness

**File**: `src/app/(tabs)/index.tsx` (moved there in Phase 1 change #4)

**Intent**: Add the missing ride-length control (km) and make the curviness selector clearly
inactive so it doesn't imply a guarantee S-01 doesn't deliver (FR-003).

**Contract**: A numeric "Distance (km)" input added to the form state; `canPlan` also requires
a valid positive distance. The curviness card gets a disabled visual state + a small "coming
soon" affordance; its `onPress` is a no-op for S-01. No new nav yet in this file beyond the
button handler (below).

#### 2. Forward-geocoding utility

**File**: `src/lib/geocode.ts` (new) + `src/lib/geocode.web.ts` (new)

**Intent**: Turn the typed/auto-detected origin address into `{lat, lng}`, mirroring the
existing native/web reverse-geocode split.

**Contract**: `geocodeAddress(query: string): Promise<{ lat: number; lng: number } | null>`.
Native: `Location.geocodeAsync(query)` → first result. Web (`geocode.web.ts`): Nominatim
`/search?format=json&q=…&limit=1` → first result's `lat`/`lon`. Return `null` on no match so
the caller can surface a friendly error. (Respect Nominatim usage policy — single result,
no hammering.)

#### 3. Generation API client + mutation hook

**File**: `src/api/route.ts` (new), `src/api/index.ts`, `src/hooks/use-generate-route-mutation.ts` (new)

**Intent**: Add the typed generate call and a mutation hook consumers branch on, copying the
`useHealthQuery` template.

**Contract**: `route.ts` exports `type GenerateRequest = { start: {lat:number;lng:number}; distanceKm: number }`,
`type GeneratedRoute = { geometry: {lat:number;lng:number}[]; distanceMeters: number; durationSeconds: number }`,
and `generateRoute(req): Promise<GeneratedRoute>` calling `request<GeneratedRoute>('/route/generate',
{ method:'POST', body:req })` (default 30s timeout). Re-export from `index.ts`.
`use-generate-route-mutation.ts` exports `useGenerateRouteMutation()` = `useMutation<GeneratedRoute,
ApiError, GenerateRequest>`; on success it stashes the route in the shared result store (Phase 4)
and navigates to `/result`.

#### 4. Wire the Plan button

**File**: `src/app/(tabs)/index.tsx` (moved there in Phase 1 change #4)

**Intent**: Replace the `console.log` with the real flow.

**Contract**: `onPress` → geocode origin (guard `null` → error state) → `mutate({ start,
distanceKm })`. Button shows a pending state from the mutation; failures render a message keyed
on `ApiError.kind` (FR-005). Destination/curviness are ignored for S-01.

### Success Criteria:

#### Automated Verification:

- Typecheck passes: `npx tsc --noEmit`
- Lint passes: `npm run lint`
- New modules exported from `src/api/index.ts`

#### Manual Verification:

- Tapping Plan Route geocodes the origin, calls the backend, shows a loading state, and on
  success navigates to the results screen
- A bad origin (no geocode match) and a backend error each show a clear message
- Curviness selector visibly reads as inactive/"coming soon"

**Implementation Note**: Pause for human confirmation of the wired flow (against the dev build).

---

## Phase 4: Results screen + end-to-end map preview

### Overview

Render the generated loop on a map with stats, closing the north-star flow.

### Changes Required:

#### 1. Shared result store

**File**: `src/lib/route-result-store.ts` (new)

**Intent**: Hold the last generated route in memory so the results screen reads it without
serializing geometry into the URL.

**Contract**: A tiny module-level store (or React context) exposing `setLastRoute(GeneratedRoute)`
/ `getLastRoute()` (or a hook). Written by the mutation's `onSuccess`, read by `result.tsx`.

#### 2. Results screen

**File**: `src/app/result.tsx` (replaces the Phase 1 smoke version)

**Intent**: Draw the loop and show ride stats; the destination for post-generation.

**Contract**: A typed expo-router route. Reads the route from the store; if absent (deep link /
reload), shows an empty state routing back to Plan. Renders `MapView` + `Polyline` mapping
`geometry` `{lat,lng}` → `{latitude, longitude}`; a start `Marker`; calls `fitToCoordinates`
from the `MapView` `onLayout` (Android-safe). Shows distance (km) and a derived duration.
`react-native-maps` imports must not break the web bundle — guard or provide a `.web` fallback
if web is still built.

### Success Criteria:

#### Automated Verification:

- Typecheck passes: `npx tsc --noEmit`
- Lint passes: `npm run lint`
- `src/app/result.tsx` resolves as a typed route (no `typedRoutes` errors)

#### Manual Verification:

- Full flow on the dev build: start + distance → Generate → results screen draws a **loop**
  that **departs from and returns to** the start, framed by `fitToCoordinates`
- Displayed/stitched distance is within **±20%** of the requested km on real roads
- End-to-end completes within **30s**
- Reloading/deep-linking `/result` with no stored route shows the empty state, not a crash

**Implementation Note**: Final phase — confirm the whole north-star flow on device.

---

## Testing Strategy

### Unit Tests:

- `RouteGeneratorTests` (backend, xUnit against the `fake` stitcher): loop closure /
  departs-from-start, ±20% length band, ≥3 distinct waypoints, invalid-input rejection.

### Integration Tests:

- Manual `POST /route/generate` against the real ORS provider (device/curl): road-following
  loop, ±20% on real roads, sub-30s. (Automated ORS integration is deferred — Risk #5 territory,
  test-plan Phase 3.)

### Manual Testing Steps:

1. On the dev build, accept location → origin auto-fills; enter e.g. 40 km; tap Plan Route.
2. Confirm a loop is drawn departing from the start, framed to fit, with stats shown.
3. Check the stitched distance is within ±20% of 40 km; repeat for a second region and tune
   `detourFactor` if needed.
4. Force errors: airplane mode (network/timeout message), a nonsense origin (no-geocode message).
5. Reload `/result` directly → empty state, no crash.

## Performance Considerations

Single ORS round-trip per generation (10s provider ceiling; 30s client budget). No
measure-and-adjust loop in S-01 — a second corrective ORS call would risk the 30s NFR and
double the billed request (Risk #7). If ±20% proves hard single-shot, revisit in a later change
rather than stacking calls here.

## Migration Notes

Adding `react-native-maps` requires a **new dev build** — Expo Go can no longer run the app
after Phase 1. Document this so the solo dev doesn't try Expo Go. No data/schema migration.

## References

- Research: [context/changes/s-01/research.md](context/changes/s-01/research.md)
- Risk #1 research (loop question answered here): [context/changes/testing-route-shaping-quality/research.md](context/changes/testing-route-shaping-quality/research.md)
- Stitcher to reuse: [api/Routing/OpenRouteServiceStitcher.cs](api/Routing/OpenRouteServiceStitcher.cs), [api/Program.cs:67-90](api/Program.cs)
- Client template: [src/hooks/use-health-query.ts](src/hooks/use-health-query.ts), [src/api/client.ts](src/api/client.ts)
- Provider decision context: [context/archive/2026-08-16-route-stitching-adapter/research-stitching.md](context/archive/2026-08-16-route-stitching-adapter/research-stitching.md)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Map & dev-build foundation

#### Automated

- [x] 1.1 Dependency installed: `react-native-maps` present in `package.json` — 6191d66
- [x] 1.2 `eas.json` exists with a `development` profile — 6191d66
- [x] 1.3 Lint passes: `npm run lint` — 6191d66
- [x] 1.4 Typecheck passes: `npx tsc --noEmit` — 6191d66
- [x] 1.8 Typecheck + lint pass after the `(tabs)`/root-Stack restructure; `/result` is a pushable route — 6191d66

#### Manual

- [x] 1.5 `eas build --profile development` produces an installable dev build — 6191d66
- [x] 1.6 A bare `MapView` renders on Android (Google Maps key working) and iOS (Apple Maps) — 6191d66
- [x] 1.7 App still launches; existing Plan/Explore tabs work in the dev build — 6191d66
- [x] 1.9 Tapping Plan Route opens `/result` (map); back returns to the tabs — 6191d66

### Phase 2: Route-generation backend (endpoint + algorithm)

#### Automated

- [x] 2.1 Build passes: `dotnet build` — a70d87a
- [x] 2.2 Tests pass: `dotnet test` (new `RouteGeneratorTests` green) — a70d87a
- [x] 2.3 `POST /route/generate` returns 200 with non-empty geometry (fake provider) — a70d87a

#### Manual

- [x] 2.4 Real-ORS generate returns a road-following loop departing from start — a70d87a
- [x] 2.5 Length within ±20% on real roads for ≥2 regions (`detourFactor` tuned) — a70d87a
- [x] 2.6 Round-trip completes under 30s — a70d87a

### Phase 3: Client wiring — distance input, geocoding, generate mutation

#### Automated

- [x] 3.1 Typecheck passes: `npx tsc --noEmit`
- [x] 3.2 Lint passes: `npm run lint`
- [x] 3.3 New generate modules exported from `src/api/index.ts`

#### Manual

- [x] 3.4 Plan Route geocodes origin, calls backend, shows loading, navigates on success
- [x] 3.5 Bad origin and backend error each show a clear `ApiError.kind`-keyed message
- [x] 3.6 Curviness selector reads as inactive/"coming soon"

### Phase 4: Results screen + end-to-end map preview

#### Automated

- [ ] 4.1 Typecheck passes: `npx tsc --noEmit`
- [ ] 4.2 Lint passes: `npm run lint`
- [ ] 4.3 `src/app/result.tsx` resolves as a typed route

#### Manual

- [ ] 4.4 Full flow draws a loop departing from + returning to start, framed by `fitToCoordinates`
- [ ] 4.5 Stitched distance within ±20% of requested km on real roads
- [ ] 4.6 End-to-end completes within 30s
- [ ] 4.7 Reloading `/result` with no stored route shows the empty state, not a crash
