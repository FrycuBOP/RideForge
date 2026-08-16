# Route Stitching Adapter (F-02) — Plan Brief

> Full plan: `context/changes/route-stitching-adapter/plan.md`
> Research (internal): `context/changes/route-stitching-adapter/research.md`
> Research (external/providers): `context/changes/route-stitching-adapter/research-stitching.md`

## What & Why

Build the server-side "stitching" half of RideForge's hybrid routing: a swappable adapter that turns an ordered list of waypoints into a road-following route (coordinate array + distance + duration) via a commodity external directions API, key held server-side. This is roadmap foundation **F-02** — it unblocks the north-star slice S-01 by providing the piece our (later) curviness algorithm feeds waypoints into.

## Starting Point

The backend `api/` is a bare net10.0 minimal-API scaffold: one `GET /health` endpoint, zero NuGet deps, no outbound HTTP, no DI beyond CORS, no domain models (`api/Program.cs:25`). F-02 introduces the backend's first external call, first service registration, and first domain layer. The mobile client's transport contract is already merged (F-01) and — critically — throws on any non-2xx **before reading the body** (`src/api/client.ts:45-47`), so failures must be signaled by HTTP status code.

## Desired End State

`POST /route/stitch` accepts `{ waypoints: [{lat,lng}, …] }` and returns `{ geometry: [{lat,lng}, …], distanceMeters, durationSeconds }`. Behind it, an `IRouteStitcher` resolves from config to either a no-network **fake** (straight-line passthrough, the default) or a real **OpenRouteService** directions provider (key from env). The whole path runs end-to-end on the fake with no key, the real provider is proven against a live call, and the fragile decode + error-mapping logic is covered by hermetic tests.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| A-vs-B pattern | Pattern A (directions), ORS first provider | Forgiving on sparse *or* moderately-dense waypoints; keeps the unbuilt algorithm's output shape flexible | Plan (blocker deferred in Research) |
| Provider realness | Real ORS + in-process fake, config-selected | Proves the real round-trip yet keeps the vertical runnable with no key/network | Plan |
| F-02 surface | Dedicated `POST /route/stitch` endpoint | Independently shippable/testable with hand-supplied waypoints before the algorithm exists | Plan |
| Error contract | Distinct HTTP status codes (400/422/502/504) | Works with the body-blind F-01 client without touching merged F-01 code | Research + Plan |
| Geometry wire format | Decoded coordinate array `{lat,lng}[]` | `react-native-maps <Polyline>` consumes it directly; provider encoding stays inside the adapter | Research + Plan |
| Testing | Minimal xUnit on decode + status mapping (hermetic) | Locks the exactly-fragile logic; no network, no flaky live tests in CI | Plan |
| Config/secret | Bound `RouteStitching` options; key via Railway env `RouteStitching__ApiKey` | Idiomatic .NET, mirrors how `PORT` already flows; nothing committed | Plan (self-decided) |
| Latency policy | Single call, ~10s per-call timeout, cancellation threaded | Reserves headroom in the 30s NFR-01 budget; stops orphaned provider work | Research + Plan |

## Scope

**In scope:** `IRouteStitcher` seam; `Coord`/`RouteRequest`/`StitchedRoute` domain types; fake provider; real ORS provider (decode, timeout, cancellation, status mapping); `POST /route/stitch` + validation; config/secret wiring; minimal xUnit project.

**Out of scope:** the curviness algorithm; any mobile-side code (`generateRoute`, hooks, map screen); pattern B / map-matching; a second real provider; changes to the F-01 client; CORS/auth rework; live-provider CI tests; fan-out/caching/retry.

## Architecture / Approach

Endpoint → `IRouteStitcher` (resolved by config) → provider impl. All provider vocabulary (ORS request shape, GeoJSON, `[lng,lat]` axis order) is confined inside the ORS provider; the endpoint, DTOs, and interface never see it. New code lives under `api/Routing/`; the endpoint sits beside `/health` in `Program.cs`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Adapter spine | Domain types + interface + fake provider + `POST /route/stitch` + validation + error contract; runnable end-to-end offline | Getting the status-mapping contract right for the body-blind client |
| 2. Real ORS provider | Typed HttpClient, config/secret, GeoJSON→`Coord[]` decode, timeout + cancellation, provider-failure→status | GeoJSON `[lng,lat]`→`{lat,lng}` axis swap; latency vs NFR-01 |
| 3. Minimal xUnit | First backend test project; hermetic decode + failure-mapping + validation tests | Standing up test infra before Module 3 formally covers it |

**Prerequisites:** none blocking (F-02 is parallel to F-01). To exercise the live path in Phase 2: one OpenRouteService API key (free tier). The unbuilt curviness algorithm is *not* a prerequisite — waypoints are hand-supplied.
**Estimated effort:** ~2–3 after-hours sessions across the three phases.

## Open Risks & Assumptions

- **Pattern A may need revisiting.** If the future curviness algorithm emits a *dense* track, directions may "straighten" it between waypoints and a map-matching (B) provider becomes the better fit. The swappable interface makes this a later drop-in, not a rewrite — but the assumption (algorithm emits sparse/moderate waypoints) is unproven until the algorithm exists.
- **Latency is unproven at scale.** Worst-case (long touristic ride, many waypoints) round-trip against 30s NFR-01 is only truly testable once real waypoint volumes exist (S-01 era).
- **ORS free-tier limits/quality** for the motorcycle use-case aren't validated here (external, non-Context7 — see research-stitching.md open questions).

## Success Criteria (Summary)

- A caller can POST waypoints and get back a road-following route (real provider) or a straight-line route (fake) with distance + duration — camelCased to fit the mobile client.
- Failures are distinguishable by HTTP status (bad input / no route / provider down / timeout) without touching F-01.
- `dotnet test` locks the decode axis-order and failure-mapping logic offline; `dotnet build` and `npm run lint` stay green.
