# Route Stitching Adapter (F-02) Implementation Plan

## Overview

Build a server-side, swappable adapter that turns an ordered list of waypoints into a road-following route (decoded coordinate array + distance + duration) by calling a commodity external directions API, with the provider key held server-side. This is roadmap foundation **F-02** — it unblocks the north-star slice S-01 (`generate-route-preview`) by providing the "stitching" half of the hybrid architecture (our own curviness algorithm, built later, produces the waypoints; this adapter stitches them).

The adapter is exposed through a dedicated `POST /route/stitch` endpoint so F-02 is independently shippable and testable with hand-supplied waypoints before the curviness algorithm exists. A provider-agnostic `IRouteStitcher` seam keeps the external provider swappable; two implementations ship — an in-process fake (straight-line passthrough, no network) and a real OpenRouteService directions provider — selected by configuration.

## Current State Analysis

- The backend `api/` (RideForgeApi, **net10.0**) is a bare minimal-API scaffold: a single 27-line `Program.cs` with wide-open CORS, `PORT` binding, and exactly one endpoint `GET /health` (`api/Program.cs:25`). No `Controllers/`, `Services/`, `Models/` folders.
- **Zero NuGet dependencies** (`api/RideForgeApi.csproj:1-9`); `RideForgeApi.deps.json` confirms the project itself is the only library. `System.Net.Http[.Json]` are already globally imported via implicit usings, and `IHttpClientFactory`/`AddHttpClient` come from the Web SDK — so a typed `HttpClient` needs **no package add**.
- **No outbound HTTP anywhere** — F-02 introduces the backend's first external call, first DI service registration beyond CORS, and first domain model layer.
- **No config/secret precedent** — `IConfiguration` is never read; the only env-var pattern is `Environment.GetEnvironmentVariable("PORT")` (`api/Program.cs:18`). Railway supplies secrets as env vars. `appsettings.json` has no custom sections.
- The mobile client's wire contract is already fixed and merged by **F-01 (mobile-backend-link)**: a `request<T>()` transport (`src/api/client.ts:19-54`), a normalized `ApiError` union with kinds `network|timeout|http|parse` (`src/api/errors.ts:2-19`), and a 30s default timeout sized to NFR-01 (`src/api/config.ts:16`).
- **Load-bearing constraint:** the client throws `ApiError('http', …, status)` on any non-2xx **before reading the response body** (`src/api/client.ts:45-47`) — a structured error JSON body is invisible to today's client. Failure must be signaled by HTTP **status code**.
- ASP.NET Core minimal API serializes System.Text.Json **camelCase** by default (the existing `/health` emits `{"status":…,"service":…}`), so F-02's DTO fields arrive camelCased and the future mobile TS type must match.

### Key Discoveries

- Swappable-provider seam is an explicit architecture requirement (`context/changes/route-stitching-adapter/research-stitching.md:78`).
- Pattern A (directions-with-waypoints) accepts a sparse ordered list and returns geometry + `summary.distance/duration`; ORS `POST /v2/directions/{profile}` is the reference (`research-stitching.md:26-33`). Pattern B (map-matching) is deferred — it fragments on sparse input (`research-stitching.md:44-46`).
- NFR-01 (`context/foundation/prd.md:100`): route generation completes within 30 seconds end-to-end. The external round-trip is only one slice of that budget; the adapter must cap its provider call well below 30s and thread cancellation (`context/foundation/infrastructure.md:99` rates the latency risk M/H).
- The client's render target is fixed: `react-native-maps <Polyline coordinates={[{latitude, longitude}]}>` (`context/foundation/roadmap.md:106-107`) — a decoded coordinate array is consumed directly.
- Endpoint-fn + barrel + hook pattern for the eventual mobile side: `src/api/health.ts:1-15`, `src/api/index.ts:6-7`, `src/hooks/use-health-query.ts:9-14` (S-01's concern, not this change).

## Desired End State

The backend exposes `POST /route/stitch` accepting `{ "waypoints": [{ "lat": <num>, "lng": <num> }, …] }` (≥2 points) and returning, on success (`200`):

```
{ "geometry": [{ "lat": <num>, "lng": <num> }, …], "distanceMeters": <num>, "durationSeconds": <num> }
```

Behind the endpoint, an `IRouteStitcher` is resolved from config: `fake` (default for local/CI — straight-line passthrough, no network) or `openrouteservice` (real directions call, key from env). Failures map to distinct HTTP statuses (400/422/502/504). The whole path is runnable end-to-end on the fake with no key or network, the real provider is proven against a live ORS call, and the fragile decode + status-mapping logic is covered by hermetic xUnit tests.

Verified by: `curl` against the fake provider returns a straight-line route; against ORS returns a road-following route; `dotnet test` passes; `dotnet build` and `npm run lint` are clean.

## What We're NOT Doing

- **No curviness algorithm** — F-02 only stitches waypoints handed to it. The algorithm (which produces the waypoints) is a separate, later concern.
- **No mobile-side code** — no `generateRoute()` fn, `useGenerateRoute()` hook, map screen, or `<Polyline>` rendering. That's S-01. The wire DTO is designed to fit F-01's client, but no TS is written here.
- **No pattern B (map-matching)** — deferred until the algorithm's output shape is known to be a dense track.
- **No second real provider** (GraphHopper/Mapbox/self-hosted OSRM) — the interface makes it a later drop-in.
- **No change to the F-01 client** (`src/api/*`) — the distinct-status error contract is chosen precisely to avoid touching merged F-01 code.
- **No CORS/auth rework** — the existing wide-open MVP CORS stays; re-scoping is a later, separate concern.
- **No live-provider integration tests in CI** — the real ORS path is verified manually; automated tests stay hermetic.
- **No fan-out / multi-call segmentation, caching, or retry of the stitch call** — single directions call for the whole list; revisit only if worst-case latency testing (an S-01 concern) demands it.

## Implementation Approach

Ship in three phases that each leave the tree green and runnable. Phase 1 builds the entire vertical (domain types, interface, DI, endpoint, validation, error contract) against a **fake** provider — so the HTTP path is provable immediately with zero external dependency. Phase 2 adds the **real** OpenRouteService provider behind the same interface, plus the config/secret and timeout/cancellation machinery, selectable by config so Phase 1's fake remains the default for local/CI. Phase 3 adds a **minimal xUnit** project locking the two fragile pieces — GeoJSON→coordinate decode and provider-failure→status mapping — via a fake `HttpMessageHandler`, with no network.

All provider vocabulary (ORS request shape, GeoJSON, coordinate axis order) is confined inside the ORS provider implementation; the endpoint, DTOs, and interface never see it.

## Critical Implementation Details

- **NFR-01 latency budget & cancellation** — the mobile client already spends the full 30s (`src/api/config.ts:16`), so the adapter cannot. The ORS provider's `HttpClient` must carry a tight per-call timeout (~10s) and the endpoint's `CancellationToken` must thread into the outbound call, so a client abort, the adapter's own timeout, and provider cancellation form one linked chain. A provider timeout/cancellation surfaces as `504`.
- **GeoJSON axis order** — ORS GeoJSON coordinates are `[longitude, latitude]`; the decode into `Coord { Lat, Lng }` must swap axis order. This is the single most error-prone line in the change and is a primary test target in Phase 3.
- **camelCase wire contract** — response records must serialize to camelCase (`geometry`, `distanceMeters`, `durationSeconds`) under the default System.Text.Json settings so the future mobile TS type matches; do not introduce a naming policy that breaks this.

## Phase 1: Adapter spine — domain, interface, fake provider, endpoint

### Overview

Establish the provider-agnostic core and expose it end-to-end against a fake provider. After this phase, `POST /route/stitch` returns a real (straight-line) response with no network, no key, and the full error contract in place.

### Changes Required

#### 1. Domain types

**File**: `api/Routing/RouteModels.cs` (new)

**Intent**: Define the stable, provider-agnostic contract the interface speaks — the input the adapter accepts and the output it returns — plus the request/response DTOs for the endpoint. Keep provider vocabulary out entirely.

**Contract**: Records `Coord(double Lat, double Lng)`; `RouteRequest(IReadOnlyList<Coord> Waypoints)`; `StitchedRoute(IReadOnlyList<Coord> Geometry, double DistanceMeters, double DurationSeconds)`. Endpoint request DTO `StitchRequestDto(IReadOnlyList<Coord> Waypoints)` and response DTO `StitchResponseDto(IReadOnlyList<Coord> Geometry, double DistanceMeters, double DurationSeconds)` — field names chosen so default camelCase serialization yields `waypoints`/`geometry`/`distanceMeters`/`durationSeconds`. (The DTO and domain shapes may coincide; keep them as distinct types so the wire contract and the internal contract can evolve independently.)

#### 2. Adapter interface

**File**: `api/Routing/IRouteStitcher.cs` (new)

**Intent**: The single swappable seam. One method the algorithm (later) and the endpoint (now) depend on; providers implement it.

**Contract**: `Task<StitchedRoute> StitchAsync(RouteRequest request, CancellationToken ct)`. A dedicated exception type `RouteStitchException(StitchFailure kind, string message)` with `enum StitchFailure { NoRoute, ProviderError, Timeout }` so the endpoint can map failures to statuses without knowing provider internals. (Input validation — too few waypoints — is the endpoint's job and maps to 400 before the stitcher is called.)

#### 3. Fake provider

**File**: `api/Routing/FakeRouteStitcher.cs` (new)

**Intent**: An in-process implementation that returns the input waypoints as the route geometry (straight-line passthrough), so the whole vertical runs without a network. Distance is the summed haversine length; duration a naive constant-speed estimate.

**Contract**: `IRouteStitcher` impl. `Geometry` = the input waypoints unchanged. `DistanceMeters` = haversine sum over consecutive waypoints. `DurationSeconds` = `DistanceMeters / assumedSpeedMps` (a fixed assumed speed, documented in a comment). Throws `RouteStitchException(NoRoute, …)` if given <2 waypoints as a defensive guard (endpoint validates first).

#### 4. DI registration + provider selection

**File**: `api/Program.cs` (modify, between the `AddCors` block at `:11` and `builder.Build()` at `:13`)

**Intent**: Register the stitcher based on a `RouteStitching:Provider` config value, defaulting to `fake`. Only the fake branch exists in Phase 1; Phase 2 adds the `openrouteservice` branch.

**Contract**: Read `builder.Configuration["RouteStitching:Provider"]` (default `"fake"`); register `IRouteStitcher` → `FakeRouteStitcher` as a singleton for now. Registration lives in `Program.cs` alongside `AddCors`.

#### 5. Endpoint + validation + error contract

**File**: `api/Program.cs` (modify, new `MapPost` adjacent to the `/health` `MapGet` at `:25`)

**Intent**: Expose `POST /route/stitch`, validate input, call the stitcher, and map outcomes to the distinct-status error contract that the body-blind F-01 client can read.

**Contract**: `app.MapPost("/route/stitch", async (StitchRequestDto dto, IRouteStitcher stitcher, CancellationToken ct) => …)`. Status mapping: `400` when `dto.Waypoints` is null or has <2 entries, or any coord is out of lat/lon range; `200` with `StitchResponseDto` on success; `422` for `RouteStitchException(NoRoute)`; `502` for `RouteStitchException(ProviderError)`; `504` for `RouteStitchException(Timeout)`. Use `Results.Ok(...)` / `Results.StatusCode(...)` / `Results.Problem(statusCode: …)`. No structured error body is relied upon (client ignores it); a `ProblemDetails` body is acceptable but the status carries the meaning.

### Success Criteria

#### Automated Verification

- Backend builds: `dotnet build api/RideForgeApi.csproj`
- Mobile lint unaffected: `npm run lint`

#### Manual Verification

- `POST /route/stitch` with a valid 3-waypoint body (fake provider active by default) returns `200` with `geometry` echoing the input, a positive `distanceMeters`, and a positive `durationSeconds`, all camelCased.
- A 1-waypoint body returns `400`.
- A malformed/empty body returns `400` (not `500`).
- `GET /health` still returns `200` (no regression).

**Implementation Note**: After this phase and passing automated verification, pause for human confirmation of the manual tests before Phase 2.

---

## Phase 2: Real OpenRouteService provider

### Overview

Add a real directions provider behind `IRouteStitcher`, selectable via config, with the key held server-side, a tight timeout, cancellation, geometry decode, and provider-failure→status mapping. The fake stays the default; setting `RouteStitching:Provider=openrouteservice` switches to the live path.

### Changes Required

#### 1. Provider options

**File**: `api/Routing/RouteStitchingOptions.cs` (new)

**Intent**: Typed config for provider selection and the ORS key/base URL/timeout, bound from configuration so the key arrives as a Railway env var (mirroring how `PORT` flows) and nothing is hardcoded.

**Contract**: Options class with `Provider` (string, default `"fake"`), `ApiKey` (string?), `BaseUrl` (string, default the ORS base), `Profile` (string, default `"driving-car"`), `TimeoutSeconds` (int, default `10`). Bound from the `RouteStitching` section via `builder.Configuration.GetSection("RouteStitching")`. On Railway the key is provided as env var `RouteStitching__ApiKey` (ASP.NET Core's `__` env→section convention).

#### 2. OpenRouteService provider

**File**: `api/Routing/OpenRouteServiceStitcher.cs` (new)

**Intent**: Call ORS directions for the whole waypoint list in a single request, decode the returned GeoJSON geometry into `Coord[]`, read distance/duration from the summary, and translate transport/HTTP/no-route outcomes into `RouteStitchException`.

**Contract**: `IRouteStitcher` impl taking an injected typed `HttpClient` and `IOptions<RouteStitchingOptions>`. Request: `POST {BaseUrl}/v2/directions/{Profile}/geojson` with body `{ "coordinates": [[lng, lat], …] }` (axis order **[lng, lat]**) and the API key in the `Authorization` header. Response decode: `features[0].geometry.coordinates` is `[[lng, lat], …]` → map each to `Coord(lat, lng)` (**swap axis order**); `features[0].properties.summary.distance`/`.duration` → `DistanceMeters`/`DurationSeconds`. Failure mapping: empty/missing route → `RouteStitchException(NoRoute)`; `OperationCanceledException`/`TaskCanceledException` from the timeout or client cancel → `RouteStitchException(Timeout)`; any other non-success status or transport/parse error → `RouteStitchException(ProviderError)`. The GeoJSON `[lng,lat]`→`Coord(lat,lng)` swap is the load-bearing detail (see Critical Implementation Details).

#### 3. Typed HttpClient + provider-selection wiring

**File**: `api/Program.cs` (modify the registration added in Phase 1)

**Intent**: Register a typed `HttpClient` for the ORS provider with the per-call timeout, bind the options, and extend the provider-selection switch so `openrouteservice` resolves to `OpenRouteServiceStitcher` while `fake` stays the default.

**Contract**: `builder.Services.Configure<RouteStitchingOptions>(builder.Configuration.GetSection("RouteStitching"))`. `builder.Services.AddHttpClient<OpenRouteServiceStitcher>(c => c.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds))`. Resolve `IRouteStitcher` by reading `RouteStitching:Provider`: `openrouteservice` → the typed-client-backed `OpenRouteServiceStitcher`; anything else → `FakeRouteStitcher`. Keep the endpoint from Phase 1 unchanged — it depends only on `IRouteStitcher`.

#### 4. Config surface

**File**: `api/appsettings.json` and `api/appsettings.Development.json` (modify)

**Intent**: Add a `RouteStitching` section documenting the knobs, with `Provider` defaulting to `fake` so local runs never require a key. The real key is never committed — it comes from an env var.

**Contract**: `"RouteStitching": { "Provider": "fake", "BaseUrl": "https://api.openrouteservice.org", "Profile": "driving-car", "TimeoutSeconds": 10 }`. No `ApiKey` in the file. Document (in the plan/README, not code) that Railway/local sets `RouteStitching__ApiKey` and `RouteStitching__Provider=openrouteservice` to enable the live path.

### Success Criteria

#### Automated Verification

- Backend builds: `dotnet build api/RideForgeApi.csproj`
- Default config still resolves the fake provider (no key needed to build/run).

#### Manual Verification

- With `RouteStitching__Provider=openrouteservice` and a valid `RouteStitching__ApiKey`, `POST /route/stitch` with 3 real-world waypoints returns `200` with a road-following `geometry` (more points than the input), plausible `distanceMeters`/`durationSeconds`.
- Coordinates render in the right place (lat/lng not swapped) — spot-check one returned point against the input region.
- An unroutable request (e.g. a waypoint in the ocean) returns `422`, not `500`.
- An invalid/empty key returns `502`.
- A deliberately tiny `TimeoutSeconds` (e.g. 1) against a slow/large request returns `504`.
- Switching `Provider` back to `fake` restores the no-network straight-line behavior.

**Implementation Note**: After this phase and passing automated verification, pause for human confirmation of the manual tests before Phase 3.

---

## Phase 3: Minimal xUnit on the fragile bits

### Overview

Stand up the first backend test project and lock the two pieces most likely to regress silently: the ORS GeoJSON→`Coord[]` decode (axis order, summary extraction) and the provider-failure→status mapping. All tests are hermetic — a fake `HttpMessageHandler` supplies canned ORS responses; no network, no key.

### Changes Required

#### 1. Test project

**File**: `api/RideForgeApi.Tests/RideForgeApi.Tests.csproj` (new)

**Intent**: An xUnit test project referencing the API project, runnable via `dotnet test`.

**Contract**: xUnit + `Microsoft.NET.Test.Sdk` test project targeting `net10.0`, `ProjectReference` to `api/RideForgeApi.csproj`. Ensure `dotnet test` from the repo/`api` root discovers it (add to a solution file if one is introduced, or rely on directory discovery).

#### 2. ORS decode + failure-mapping tests

**File**: `api/RideForgeApi.Tests/OpenRouteServiceStitcherTests.cs` (new)

**Intent**: Verify the provider decodes a canned ORS GeoJSON body correctly and maps each failure class to the right `RouteStitchException` kind, using a fake `HttpMessageHandler`.

**Contract**: A `DelegatingHandler`/`HttpMessageHandler` stub returning canned responses, injected into the provider's `HttpClient`. Cases: (a) valid GeoJSON → geometry decoded with **lat/lng un-swapped** (assert a known `[lng,lat]` input yields the expected `Coord(lat,lng)`), correct `DistanceMeters`/`DurationSeconds`; (b) empty `features` → `NoRoute`; (c) non-success status (e.g. 403) → `ProviderError`; (d) a canceled/timed-out send → `Timeout`.

#### 3. Fake provider + validation tests

**File**: `api/RideForgeApi.Tests/FakeRouteStitcherTests.cs` (new)

**Intent**: Lock the fake provider's contract (geometry echo, positive distance/duration, monotonic distance with more spread) and the endpoint's input-validation boundary (<2 waypoints, out-of-range coords).

**Contract**: Unit tests over `FakeRouteStitcher` (geometry equals input; distance/duration positive; distance grows when waypoints are farther apart). Validation may be tested at the handler level if practical, else via a thin extracted validation helper.

### Success Criteria

#### Automated Verification

- Tests pass: `dotnet test`
- Backend still builds: `dotnet build api/RideForgeApi.csproj`
- Mobile lint unaffected: `npm run lint`

#### Manual Verification

- `dotnet test` output shows the decode axis-order case and all three failure-mapping cases passing.
- No network calls occur during the test run (tests complete offline).

**Implementation Note**: After this phase and passing automated verification, pause for human confirmation before considering F-02 complete.

---

## Testing Strategy

### Unit Tests

- ORS GeoJSON decode: axis-order swap (`[lng,lat]`→`Coord(lat,lng)`), summary distance/duration extraction.
- Provider failure mapping: no-route→`NoRoute`, non-success→`ProviderError`, cancellation→`Timeout`.
- Fake provider: geometry echo, positive/monotonic distance, duration derivation.
- Input validation: <2 waypoints and out-of-range coordinates rejected.

### Integration Tests

- None automated (live-provider integration is intentionally out of scope for CI). The real round-trip is verified manually in Phase 2.

### Manual Testing Steps

1. Fake path (default): `curl -X POST /route/stitch` with 3 waypoints → `200`, geometry echoes input.
2. Real path: set `RouteStitching__Provider=openrouteservice` + `RouteStitching__ApiKey`, repeat with real coordinates → `200`, road-following geometry, correct on-map position.
3. Error path: unroutable waypoint → `422`; bad key → `502`; tiny timeout → `504`; 1 waypoint → `400`.
4. Regression: `GET /health` still `200`.

## Performance Considerations

Single directions call per request — no fan-out — keeps latency to one round-trip. The provider `HttpClient` timeout (~10s) reserves headroom inside the 30s NFR-01 budget for the future algorithm compute, serialization, Railway network, and client render. Cancellation threads end-to-end so a client abort stops orphaned provider work. Worst-case latency (long touristic ride, many waypoints) is an S-01-era test concern, flagged in `context/foundation/infrastructure.md:99`; if it blows the budget, revisit call-count/segmentation then.

## Migration Notes

No data or schema. The only operational change: to enable the live provider, set env vars `RouteStitching__Provider=openrouteservice` and `RouteStitching__ApiKey=<key>` on Railway. Absent those, the service runs the fake provider — safe by default, no external dependency.

## References

- Related research (internal): `context/changes/route-stitching-adapter/research.md`
- Related research (external/providers): `context/changes/route-stitching-adapter/research-stitching.md`
- F-01 wire contract to fit: `src/api/client.ts:45-47`, `src/api/errors.ts:2-19`, `src/api/config.ts:16`
- Existing endpoint pattern: `api/Program.cs:25`
- NFR-01: `context/foundation/prd.md:100`; latency risk: `context/foundation/infrastructure.md:99`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Adapter spine — domain, interface, fake provider, endpoint

#### Automated

- [x] 1.1 Backend builds: `dotnet build api/RideForgeApi.csproj`
- [x] 1.2 Mobile lint unaffected: `npm run lint`

#### Manual

- [x] 1.3 Valid 3-waypoint POST (fake) returns 200 with echoed geometry + positive distance/duration, camelCased
- [x] 1.4 1-waypoint body returns 400
- [x] 1.5 Malformed/empty body returns 400 (not 500)
- [x] 1.6 `GET /health` still returns 200 (no regression)

### Phase 2: Real OpenRouteService provider

#### Automated

- [ ] 2.1 Backend builds: `dotnet build api/RideForgeApi.csproj`
- [ ] 2.2 Default config resolves the fake provider (no key needed to build/run)

#### Manual

- [ ] 2.3 Live ORS path returns 200 with road-following geometry + plausible distance/duration
- [ ] 2.4 Returned coordinates are positioned correctly (lat/lng not swapped)
- [ ] 2.5 Unroutable request returns 422 (not 500)
- [ ] 2.6 Invalid/empty key returns 502
- [ ] 2.7 Tiny TimeoutSeconds against a slow request returns 504
- [ ] 2.8 Switching Provider back to `fake` restores no-network behavior

### Phase 3: Minimal xUnit on the fragile bits

#### Automated

- [ ] 3.1 Tests pass: `dotnet test`
- [ ] 3.2 Backend still builds: `dotnet build api/RideForgeApi.csproj`
- [ ] 3.3 Mobile lint unaffected: `npm run lint`

#### Manual

- [ ] 3.4 Test output shows decode axis-order case + all three failure-mapping cases passing
- [ ] 3.5 No network calls during the test run (completes offline)
