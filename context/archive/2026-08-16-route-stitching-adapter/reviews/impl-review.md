<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Route Stitching Adapter (F-02)

- **Plan**: context/changes/route-stitching-adapter/plan.md
- **Scope**: All phases (1–3 of 3)
- **Date**: 2026-08-16
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — HttpResponseMessage never disposed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: api/Routing/OpenRouteServiceStitcher.cs:32,44
- **Detail**: `response` is assigned at :44 and never disposed on any path. On the non-success throw paths (:63-68) the body is never read, so the pooled connection is held until GC — a connection-pool exhaustion risk under load.
- **Fix**: Restructure so the response is disposed on all paths — `using var response = await _http.SendAsync(req, ct);` inside a scope spanning the status check and the content read.
- **Decision**: FIXED — wrapped response usage in `using (response) { ... }` (OpenRouteServiceStitcher.cs)

### F2 — ParseGeoJson only catches JsonException; wrong-typed JSON escapes as 500

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability)
- **Location**: api/Routing/OpenRouteServiceStitcher.cs:110-129
- **Detail**: `ParseGeoJson` wraps parsing in `catch (JsonException)` only. Well-formed-but-wrong-typed JSON slips through: `pair[0].GetDouble()` on a string value throws `InvalidOperationException`/`FormatException`, and a coordinate pair with <2 elements throws `IndexOutOfRangeException` — none are `JsonException`, so they bubble to an unhandled 500 instead of the intended 502 ProviderError. The `MalformedJson_MapsToProviderError` test only covers non-JSON, so this gap is untested.
- **Fix**: Also catch `InvalidOperationException`/`FormatException` (and guard each pair has ≥2 elements), rethrowing as `RouteStitchException(ProviderError)`; add a unit test with a string-typed coordinate.
  - Strength: Closes the 500 gap so any malformed provider response maps to 502 as designed; extends the existing `JsonException`→ProviderError branch. `RouteStitchException` (NoRoute throws) is not a subtype of the broadened catches, so it won't be swallowed.
  - Tradeoff: Slightly broader catch surface; needs the new test to lock it.
  - Confidence: HIGH — the thrown types are well-documented for System.Text.Json element accessors.
  - Blind spot: None significant.
- **Decision**: FIXED — broadened catch to InvalidOperationException/FormatException + guard pair length ≥2; added WrongTypedCoordinate + ShortCoordinatePair tests

### F3 — No upper bound on waypoint count (public unauthenticated endpoint)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Security/Performance)
- **Location**: api/Routing/RouteValidation.cs:10-28, api/Routing/OpenRouteServiceStitcher.cs:29
- **Detail**: Validation enforces only `>= 2`; the entire list is forwarded to ORS in one call. A large payload (thousands of coords) is an unbounded outbound request — an amplification/latency-cost vector on a public, unauthenticated endpoint. `infrastructure.md:99` explicitly lists "Bound candidate-waypoint count" as an NFR-01 mitigation.
- **Fix**: Add a configurable max-waypoint cap (e.g. `RouteStitchingOptions.MaxWaypoints`, default ~50) enforced in `RouteValidation`, returning 400 when exceeded.
  - Strength: Bounds outbound size/latency and closes the amplification vector; directly aligns with the infra risk-register mitigation.
  - Tradeoff: Introduces a limit that must be chosen; too low could reject legitimate long touristic routes.
  - Confidence: MEDIUM — the right cap depends on the curviness algorithm's output volume, which isn't built yet.
  - Blind spot: Real waypoint volumes are unknown until the algorithm exists; the cap may need tuning at S-01.
- **Decision**: SKIPPED — deferred to S-01 when the curviness algorithm's real waypoint volume is known; amplification risk accepted for MVP

### F4 — No startup guard that ApiKey is set when Provider=openrouteservice

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture (Reliability)
- **Location**: api/Program.cs:32-41
- **Detail**: A misconfigured deploy (provider set, key missing) sends no `Authorization` header and yields a runtime 403→502 on every call instead of failing fast at boot.
- **Fix**: At startup, if `Provider=openrouteservice` and `ApiKey` is empty, throw so the deploy fails fast.
- **Decision**: FIXED — Program.cs throws InvalidOperationException when provider=openrouteservice and ApiKey is empty

### F5 — Caller cancellation is labelled Timeout (504)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency (Semantics)
- **Location**: api/Routing/OpenRouteServiceStitcher.cs:46-51
- **Detail**: A client disconnect (request-aborted `ct`) is indistinguishable from `HttpClient.Timeout` and maps to 504. Harmless for MVP (the message hedges "or was cancelled"), but a client abort isn't semantically a gateway timeout.
- **Fix**: Optionally check `ct.IsCancellationRequested` to treat a caller abort distinctly; acceptable to leave as-is for MVP.
- **Decision**: FIXED — caller-abort (ct cancelled) now rethrows for framework client-closed handling; only HttpClient.Timeout maps to 504

### F6 — AllowAnyOrigin CORS (pre-existing, documented)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Security)
- **Location**: api/Program.cs:9-13
- **Detail**: Wide-open CORS. Already an intentional, documented MVP choice inherited from F-01 (comment :5-8), out of F-02 scope. Recorded only for the triage trail.
- **Fix**: None now — revisit when auth/cookies land (per the existing comment).
- **Decision**: SKIPPED — out of F-02 scope; pre-existing documented MVP choice from F-01, revisit when auth lands

### F7 — No solution file; repo-root dotnet test/build rely on directory discovery

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria (Tooling)
- **Location**: (repo root)
- **Detail**: The test project lives under `api/` with no `.sln`, so `dotnet test`/`dotnet build` need an explicit project path. Works today; a solution file would make CI invocation from the repo root cleaner and less error-prone.
- **Fix**: Add a `.sln` referencing `RideForgeApi` + `RideForgeApi.Tests` (or document the explicit-path invocation).
- **Decision**: FIXED — added api/RideForgeApi.slnx referencing both projects; `dotnet test api/RideForgeApi.slnx` discovers the suite
