---
date: 2026-08-16T13:47:31+02:00
researcher: Claude (10x-research)
git_commit: 7253dad8e0d31c843c98b80e00d04dfe66940827
branch: dev
repository: 10xDevs
topic: "Internal codebase grounding for the F-02 server-side route-stitching adapter"
tags: [research, codebase, route-stitching-adapter, backend, dotnet, adapter, nfr-01, wire-contract]
status: complete
last_updated: 2026-08-16
last_updated_by: Claude (10x-research)
---

# Research: Internal codebase grounding for the F-02 route-stitching adapter

**Date**: 2026-08-16T13:47:31+02:00
**Researcher**: Claude (10x-research)
**Git Commit**: 7253dad8e0d31c843c98b80e00d04dfe66940827
**Branch**: dev
**Repository**: 10xDevs

## Research Question

`/10x-research route-stitching-adapter` — What does the codebase already establish that the F-02 change must build on? The external-provider landscape is already researched in [research-stitching.md](research-stitching.md); this document supplies the *internal* grounding a plan needs: the backend adapter seam, the F-01 wire contract F-02's response must fit, the algorithm↔adapter interface that keeps provider choice deferrable, and the NFR-01 latency budget — as a deep architectural dive.

## Summary

F-02 is a **clean-slate, server-side .NET change** riding an existing hybrid decision: RideForge's own curviness algorithm produces candidate waypoints in-process, and a **swappable external directions/map-matching API** stitches them into a road-following route (polyline + distance + duration), key held server-side (PRD Open Question 2, resolved 2026-08-11).

Five load-bearing facts from the codebase:

1. **The backend is a bare scaffold.** `api/` is a single-file minimal-API project (`Program.cs`, 27 lines), only `GET /health`, **zero NuGet dependencies**, **no HttpClient / IHttpClientFactory**, no config sections, no domain models. F-02 introduces the *first* outbound HTTP call, the *first* typed-client/DI registration, and the *first* domain layer — there is no backend pattern to conform to, only conventions to establish.
2. **The client wire contract is fixed by F-01 (mobile-backend-link) and already merged.** `src/api/` has a `request<T>()` transport, a normalized `ApiError` union, and a 30s default timeout deliberately sized to NFR-01. A generate call is expected to be a `useGenerateRoute()` **mutation** reusing this exact client.
3. **The client signals failure by HTTP status only.** `request<T>()` throws `ApiError('http', …, status)` **before reading the body** on any non-2xx response — so **any structured error JSON F-02 returns on failure is invisible** to today's client. F-02 must signal failure through the status code (or the client must be extended).
4. **The response geometry wire-format is an unresolved contract point.** Roadmap fixes the payload as *polyline + distance + duration* and the client's render target as `react-native-maps <Polyline coordinates={[{latitude, longitude}]}>`, but nothing pins the encoding (encoded polyline vs GeoJSON vs decoded `{lat,lng}[]`). This is [research-stitching.md](research-stitching.md) open question #4 and must be resolved in the plan.
5. **The A-vs-B pattern fork is the deferred blocker, and it leaks into the *semantics* of the adapter's input, not its C# signature.** Same `Coord[]`-in / `route`-out interface serves both, but sparse-waypoints (directions) vs dense-track (map-matching) dictate incompatible input *density* — and map-matching degrades on sparse input. This is the decision that waits on the not-yet-built algorithm's output shape.

**Bottom line:** F-02 can be planned now down to the adapter *interface*, the DTO/error contract, the DI/HttpClient wiring, and the timeout/cancellation design — all of which are provider-agnostic. The single genuinely blocked decision is A-vs-B provider selection, which is gated on the algorithm's output shape (owner: user).

## Detailed Findings

### Backend adapter seam — `api/` (RideForgeApi, .NET 10)

The backend is a **single flat ASP.NET Core Web project** — no `Controllers/`, `Services/`, `Models/`, `Dtos/`, or `Endpoints/` folders. Only hand-written source is `Program.cs`.

- **`api/RideForgeApi.csproj:1-9`** — `net10.0`, `Nullable` enabled, `ImplicitUsings` enabled, and **zero `<PackageReference>` entries**. `api/bin/Debug/net10.0/RideForgeApi.deps.json:16-22` confirms the project itself is the only library. However `System.Net.Http` and `System.Net.Http.Json` are already globally imported via implicit usings (`api/obj/Debug/net10.0/RideForgeApi.GlobalUsings.g.cs:14-15`), so `HttpClient` / `GetFromJsonAsync` are usable, and `IHttpClientFactory`/`AddHttpClient` come from the Web SDK's `Microsoft.Extensions.Http` — **no package add needed** to build a typed client.
- **`api/Program.cs`** (27 lines, top-level statements / minimal API — no controllers):
  - Services: **only CORS** — `builder.Services.AddCors(...)` with `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()` (`api/Program.cs:7-11`). Comment at `:5-6` flags this as an intentional MVP choice ("public, unauthenticated read API") to be scoped down when auth lands.
  - Middleware: only `app.UseCors()` (`:23`).
  - Port: reads `PORT` env var directly via `Environment.GetEnvironmentVariable("PORT")`, binds `0.0.0.0` in container, default `8080` (`:18-21`).
  - Endpoints: **only** `app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "rideforge-api" }))` (`:25`).
- **HttpClient / outbound HTTP: none exists.** No `HttpClient`, `AddHttpClient`, `IHttpClientFactory`, `SendAsync`, or `GetFromJson` anywhere in `api/*.cs`. Built from scratch.
- **Config & secrets: no precedent.** `builder.Configuration`/`IConfiguration` is never accessed. `api/appsettings.json:1-9` and `appsettings.Development.json:1-8` hold only `Logging` + `AllowedHosts`. The only env-var convention in code is `PORT`. `api/Dockerfile:1-11` sets no `ENV`; `api/railway.toml:1-3` declares only the Dockerfile builder (no `[variables]`). A server-side API key would arrive as a **Railway environment variable** (consistent with how `PORT` flows), read either directly via `Environment.GetEnvironmentVariable(...)` or via a new bound `appsettings` section.
- **DTO conventions: none.** The only response shape is the anonymous object at `:25`. No `JsonSerializerOptions` configured → System.Text.Json defaults → **camelCase** JSON on the wire.
- **Natural insertion points** (consistent with what exists): interface + provider impl as new `.cs` files under `api/` (or a first `api/Services/` folder); DTO/records under `api/` (or first `api/Models/`); DI registration in `Program.cs` between `:11` and `:13` via `builder.Services.AddHttpClient<IRouteStitcher, …>()`; endpoint as a new `app.MapPost(...)` beside `:25`; API key via new bound config section or `Environment.GetEnvironmentVariable(...)` mirroring `PORT`.

### Client / wire contract — F-01 (mobile-backend-link), already merged

The transport layer F-02's response must fit lives under `src/api/` (built by F-01, whose purpose was exactly to set the FR-005 loading/error convention for all later backend-calling slices).

- **Base URL / env** — `src/api/config.ts:9-16`: `API_BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'https://rideforge-api-production.up.railway.app'`; `DEFAULT_TIMEOUT_MS = 30_000` (explicitly sized to the NFR-01 budget). Build-time inlined — switching requires `expo start --clear`.
- **Error model** — `src/api/errors.ts:2-19`: `ApiErrorKind = 'network' | 'timeout' | 'http' | 'parse'`; `class ApiError extends Error { kind; status?; }` (status set only for `'http'`). `normalizeError` maps `AbortError`→`timeout`, fetch `TypeError`→`network`.
- **Client** — `src/api/client.ts:19-54`: `request<T>(path, { method?, body?, timeoutMs?, signal? }): Promise<T>`. URL is `` `${API_BASE_URL}${path}` `` (endpoints pass a leading-slash path). A JSON `body` auto-sets `Content-Type: application/json` and is `JSON.stringify`-ed (`:36-37`) — a POST generate call just passes `body: {...}`.
  - **⚠ Load-bearing constraint** (`client.ts:45-47`): on `!response.ok` it `throw new ApiError('http', …, response.status)` **before reading the body**. The current client **discards any structured error JSON** the backend returns on non-2xx. F-02 must signal failure via the **HTTP status code** (surfaced to consumers as `error.kind === 'http'` + `error.status`); an error DTO body is invisible unless the client is extended.
  - Success path parses the whole body as `T` (`:50-51`); malformed JSON → `ApiError('parse')`. A 2xx body must be valid JSON matching the declared TS type exactly.
- **Adding a typed endpoint** — pattern from `src/api/health.ts:1-15`: declare the response type + a `request<T>('/path', …)` fn, re-export both from the barrel `src/api/index.ts:6-7`, add a hook under `src/hooks/`. Query hook pattern `src/hooks/use-health-query.ts:9-14` pins `useQuery<T, ApiError>` so consumers branch on `error.kind`. Query-client defaults `src/api/query-client.ts:7-14`: `retry: 1`, `staleTime: 30_000`. A generate call is expected to be a **mutation** (`plan.md:37-38`: `useGenerateRoute()`), not a query.
- **Casing** — ASP.NET Core minimal API serializes camelCase by default; F-02's response DTO fields will arrive camelCased and the mobile TS type must match (mirrors the existing `{ status, service }` health type).

### Algorithm↔adapter interface (synthesis, grounded below)

Both the curviness algorithm (upstream) and the stitching adapter (downstream) run **in the same .NET process**. For the provider to stay swappable without touching the algorithm, the seam must be expressed **only in RideForge's own domain types** — the algorithm must never see an ORS `coordinates` array or an OSRM `match` response.

- **Minimal stable input**: an ordered coordinate list, e.g. `RouteRequest { IReadOnlyList<Coord> Waypoints }`, `Coord { double Lat; double Lon; }`. Narrowest thing both patterns need; carries no provider vocabulary and no algorithm internals (curviness/pace already consumed to *produce* the coordinates). US-01's "departs from starting location" is satisfied by pinning element 0.
- **Minimal stable output**: the roadmap already names it (`roadmap.md:83`) — **polyline + distance + duration**, e.g. `StitchedRoute { IReadOnlyList<Coord> Geometry; double DistanceMeters; double DurationSeconds; }`. Distance/duration feed US-01's "±20% of requested duration" check. Returning `Geometry` as **decoded `Coord[]`** (not an encoded-polyline string or provider GeoJSON) matches the client's `react-native-maps <Polyline coordinates={[{latitude, longitude}]}>` directly — i.e. **decoding provider geometry belongs inside the adapter**, resolving open question #4 in favor of adapter-side normalization.
- **Interface**: one method — `Task<StitchedRoute> StitchAsync(RouteRequest, CancellationToken)`. Providers are implementations; the algorithm depends on the interface, never a concrete provider.

#### Where the A-vs-B fork actually leaks

The `Coord[]`-in / `StitchedRoute`-out **C# signature is identical for both patterns**, but the two interpret the same list incompatibly:

- **Pattern A (directions)** treats each element as a hard waypoint to pass *through*; designed for a **sparse** list.
- **Pattern B (map-matching)** treats the list as a **dense, noisy trace** to snap; **degrades on sparse input** — OSRM `match` splits into sub-traces and drops outliers on large gaps ([research-stitching.md:44-46](research-stitching.md)). Feeding B an A-style sparse list yields a *broken/fragmented* route, not merely a lower-quality one. B also needs per-point inputs A doesn't (GPS `radiuses`, `gaps`, `tidy`, waypoint-index markers).

So the fork leaks into the **density/semantics of the input**, not the type. Two framings for the plan to weigh (neither chosen here — this is the deferred blocker):

1. **One interface, density is the algorithm's contract** — keep `StitchAsync(Coord[])`, document that the algorithm emits density matching the wired provider. Cleanest signature, but A↔B is not a drop-in swap unless the algorithm's output density is itself configurable.
2. **One interface, optional per-point metadata** — widen `Coord` with optional `AccuracyMeters?`/`IsHardWaypoint?`; A-impls ignore them, B-impls use them. Preserves a true drop-in swap at the cost of a richer input type.

The plan must state explicitly **whether input density is part of the adapter contract or the algorithm contract** — that is exactly where A-vs-B leaks.

### NFR-01 30-second budget → adapter design constraints

**NFR-01** (`prd.md:100`), the PRD's only non-functional requirement: *"Route generation (from input submission to route visible on map) completes within 30 seconds for any valid input combination."* The clock is **end-to-end** (mobile submit → pixels on map), so the external round-trip is only one slice, shared with algorithm compute + Railway network + client render. The infra risk register (`infrastructure.md:99`) rates "own algorithm + directions round-trip can't meet 30s for large ride durations" at **M-likelihood / H-impact**.

- **Call count dominates.** Pattern A can usually stitch the whole list in **one** directions call; per-segment fan-out multiplies latency linearly. Adapter should **minimize call count** — single call preferred; if fan-out is unavoidable, bound parallelism rather than go serial. Matches register mitigation "Bound candidate-waypoint count; cache/limit stitching calls."
- **Adapter needs its own timeout, tighter than 30s.** The client already spends the full 30s (`config.ts:16`), so the adapter cannot — it must cap the *provider* call well below 30s, reserving headroom for compute + serialization + render, and assume **cross-region RTT** (Railway is Amsterdam-only, `infrastructure.md:62`; a provider in e.g. Frankfurt adds fixed RTT, `:98`).
- **Cancellation must propagate end-to-end.** The client aborts via `AbortController` on its 30s timeout (`client.ts:22-29`). To avoid orphaned backend work after the client gives up, the adapter takes a `CancellationToken` and threads it into the outbound `HttpClient` call — client timeout, adapter budget timeout, and provider cancellation become one linked chain.
- **Worst case must be tested, not assumed.** Both `infrastructure.md:99` and `roadmap.md:93` name the **long touristic ride** (most waypoints, most calls) as the budget-buster. The adapter's timeout/call-count design should be validated against it in staging before FR-005 ships (the actual test is an S-01 concern; the *design constraint* is F-02's).

## Code References

- `api/Program.cs:5-11` — intentional wide-open CORS (MVP), the only registered service
- `api/Program.cs:18-21` — `PORT` env-var handling (the only env-var precedent for a server-side secret)
- `api/Program.cs:25` — sole endpoint `GET /health`; anonymous-object response → camelCase JSON
- `api/RideForgeApi.csproj:1-9` — net10.0, nullable, **zero package references**
- `api/obj/Debug/net10.0/RideForgeApi.GlobalUsings.g.cs:14-15` — `System.Net.Http[.Json]` already globally imported
- `api/appsettings.json:1-9` / `api/railway.toml:1-3` / `api/Dockerfile:1-11` — no custom config, no env conventions defined
- `src/api/config.ts:9-16` — base URL + `DEFAULT_TIMEOUT_MS = 30_000` (NFR-01-sized)
- `src/api/errors.ts:2-19` — `ApiErrorKind` union + `ApiError` class
- `src/api/client.ts:45-47` — **throws on non-2xx before reading body** (error-DTO-blind)
- `src/api/client.ts:50-51` — success parses whole body as `T`
- `src/api/health.ts:1-15` — the endpoint-fn pattern F-02's `generateRoute()` follows
- `src/api/index.ts:6-7` — barrel re-export convention
- `src/hooks/use-health-query.ts:9-14` — `useQuery<T, ApiError>` hook pattern (generate call is a *mutation* instead)
- `src/api/query-client.ts:7-14` — query defaults (`retry: 1`, `staleTime: 30_000`)
- `src/app/index.tsx:52-54,73,207` — the generation form: origin/destination as **strings**, curviness slider, submit only `console.log`s (no backend call, no route type yet)

## Architecture Insights

- **Two deployables, no shared error type.** The backend (`api/`, .NET) and client (`src/api/`, TS) are separate deployments with no shared schema/codegen. Consistency is a *convention* to uphold by hand: F-02's backend should mirror the client's error-normalization/timeout posture, and its response DTO must be hand-kept in sync with the mobile TS type (camelCase).
- **Failure is status-code-shaped, not body-shaped.** Because `client.ts` is error-body-blind, the backend's error *contract* today is purely the HTTP status. Richer error payloads (e.g. "no route found", "provider down", "timeout") require either mapping to distinct status codes or a deliberate client extension — a plan decision, not a given.
- **The adapter is a textbook swappable-provider seam.** Interface + N implementations + DI selection, with all provider vocabulary (encoded polylines, ORS/OSRM request shapes, GeoJSON) confined inside the implementation. Everything provider-agnostic (interface, DTOs, wiring, timeout/cancellation) is plannable now; only the concrete implementation choice is deferred.
- **Progressive-disclosure discipline applies.** F-01's review flagged scope creep; the roadmap warns the adapter must not balloon. F-02 should ship the one endpoint + one interface + one first provider impl, not a generic geo-services layer.

## Historical Context (from prior changes)

- [context/changes/mobile-backend-link/plan.md:37-38](../mobile-backend-link/plan.md) — explicit hand-off: "S-01 can then add `generateRoute()` next to `getHealth()` and a `useGenerateRoute()` mutation reusing the exact same client, error model, and query provider." F-02's backend produces what that call consumes.
- [context/changes/mobile-backend-link/plan-brief.md:28-34](../mobile-backend-link/plan-brief.md) — the transport decisions (TanStack Query, normalized `ApiError`, 30s timeout ↔ NFR-01) F-02 inherits.
- [context/changes/mobile-backend-link/reviews/impl-review.md:29](../mobile-backend-link/reviews/impl-review.md) — carry-over: CORS is `AllowAnyOrigin`, to be re-scoped when auth lands; the web target depends on backend CORS headers.
- [context/changes/bootstrap-verification/verification.md](../bootstrap-verification/verification.md) — Expo scaffold, npm, no test runner (type-check + lint only); `has_auth: true` but non-MVP-blocking. Says nothing about the `.NET api/` project directly.
- PRD Open Question 2 (resolved 2026-08-11, `prd.md:128`) — the hybrid decision F-02 implements: backend generates waypoints, commodity API only stitches; "any road-following directions API works since curviness is no longer sourced externally."

## Related Research

- [context/changes/route-stitching-adapter/research-stitching.md](research-stitching.md) — the companion **external** research: Directions-with-waypoints (A) vs map-matching (B), provider comparison (ORS / OSRM / GraphHopper / Mapbox), self-host vs SaaS, and the selection criterion keyed to algorithm output shape. This document (internal) + that one (external) together are the evidence base for `/10x-plan`.

## Open Questions

1. **[BLOCKING — deferred] Algorithm output shape: sparse ordered waypoints vs dense track?** Determines pattern A (directions, e.g. OpenRouteService) vs B (map-matching, OSRM/GraphHopper), and whether input *density* is part of the adapter contract or the algorithm contract (§"Where the A-vs-B fork actually leaks"). Owner: user. Gated on the not-yet-built curviness algorithm.
2. **[PLAN DECISION] Geometry wire-format.** Recommendation from synthesis: adapter decodes provider geometry to `Coord[]` server-side so the client renders `<Polyline>` directly. Confirm vs sending encoded-polyline/GeoJSON (moves a decode step onto the client).
3. **[PLAN DECISION] Error contract granularity.** Given the client is error-body-blind, decide whether distinct failures (no route, provider down, budget timeout) map to distinct HTTP status codes, or whether to extend `client.ts` to read an error body. Affects both deployables.
4. **[PLAN DECISION] Config/secret mechanism for the provider API key.** `Environment.GetEnvironmentVariable(...)` (mirrors `PORT`) vs a bound `appsettings` section. Railway supplies it as an env var either way.
5. **[PLAN DECISION] Per-call timeout budget + call-count policy.** Concrete sub-30s ceiling and single-call-vs-bounded-fan-out policy, assuming cross-region RTT; worst case = long touristic ride.
6. **[NON-BLOCKING] Provider pick** (GraphHopper / OpenRouteService / Mapbox / self-hosted OSRM) — follows from #1; pricing/rate-limits/route-quality still to verify outside Context7 (see research-stitching.md open questions #2, #3).
