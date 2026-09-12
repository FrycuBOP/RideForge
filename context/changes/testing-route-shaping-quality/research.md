---
date: 2026-08-24T19:40:30+02:00
researcher: Claude (10x-research)
git_commit: 0c9eaaf9dfae532c5340b8f7ac9effd81db4af9b
branch: dev
repository: 10xDevs (RideForge)
topic: "Risk #1 — monotone / self-repeating routes; curviness slider that doesn't change the route (test-plan §3 Phase 2)"
tags: [research, codebase, routing, curviness, oracle, risk-1, phase-2]
status: complete
last_updated: 2026-08-24
last_updated_by: Claude (10x-research)
---

# Research: Risk #1 — Route-shaping quality (curviness / variety)

**Date**: 2026-08-24T19:40:30+02:00
**Researcher**: Claude (10x-research)
**Git Commit**: 0c9eaaf9dfae532c5340b8f7ac9effd81db4af9b
**Branch**: dev
**Repository**: 10xDevs (RideForge)

## Research Question

Test-plan **Risk #1** (High × High, protect first), test-plan §3 **Phase 2**
("Route-shaping quality harness", covers Risks #1 + #4):

> Generated routes are monotone / self-repeating — reused road segments, an
> out-and-back instead of a loop, and a curviness slider that doesn't visibly
> change the route — so the core product thesis doesn't deliver.

The research job for this phase (test-plan §1 principle #3 + §2 Risk Response row #1):
produce the **oracle** — what a test must prove, from sources not implementation — and
ground three things: (a) *where shaping output is produced*, (b) *what geometry exists
pre-stitch*, (c) *whether a curviness/variety metric already exists*.

## Summary

**The oracle is well-defined by the sources. The subject it would judge does not exist
in the codebase yet.** `api/Routing/` is exclusively the F-02 *stitching boundary*: it
turns a **caller-supplied** ordered waypoint list into a road-following polyline. There is
no curviness input, no waypoint generation, no route shaping, and no variety/curviness
metric anywhere. The code says so itself — `RouteRequest`'s doc-comment reads *"The (future)
curviness algorithm produces this; the endpoint hands it in directly for now."*
([RouteModels.cs:9-14](api/Routing/RouteModels.cs)).

This matches the test-plan's own §3 note ("Phase 2 tracks S-01/S-02 — the curviness
algorithm it tests is not built yet") and the roadmap: **S-02 `curviness-shaping` = proposed**,
and even its prerequisite **S-01 `generate-route-preview` = proposed** (neither started).

Consequence: Risk #1 has **two faces**, and separating them is the whole point of this research.

- **Face A — the real, unbuilt risk (blocked).** The shaping *quality* of the algorithm's
  output: does a higher slider produce an objectively curvier route? does it avoid reused
  segments and out-and-backs? This is where the product thesis lives — and there is nothing
  to test until S-01+S-02 land. Writing these property tests now would assert against a
  route producer that doesn't exist.
- **Face B — buildable and testable now (the independent oracle).** The plan's entire
  defence against the oracle problem is to judge routes with a *curviness/variety metric
  computed independently of the algorithm*. Those pure geometric functions — total
  curvature (summed turn angle), segment-reuse ratio, loop-closure gap — **do not exist**
  (`GeoMath` has only distance helpers). They can be built and unit-tested **now**, against
  synthetic hand-built geometries with known answers, because they test the *ruler*, not the
  route. This is the one thing that both (1) has real signal today and (2) de-risks Face A by
  making the oracle trustworthy before the thing it measures exists.

**Recommendation (a sequencing decision for the user, see Open Questions):** do not write
Face-A property tests against nonexistent code, and do not mirror-test. Either (i) split
Phase 2 — build + unit-test the independent metric oracle (Face B) now, defer Face-A
monotonicity/floor tests until S-01/S-02; or (ii) mark Phase 2 blocked-on-algorithm and move
to Phase 3 (GPX + latency), whose subjects partially exist. This is a real fork because
Face B blurs the Lesson-2 boundary (the metric is production/helper code, not a test).

## Detailed Findings

### (a) Where is the shaping output produced? — Nowhere; only the stitching boundary exists

The `/route` surface is a single endpoint that consumes a **pre-built** waypoint list:

- `POST /route/stitch` takes `StitchRequestDto { waypoints }` and returns geometry +
  distance + duration ([Program.cs:67-90](api/Program.cs)). The only other route is
  `GET /health` ([Program.cs:62](api/Program.cs)). **There is no `/route/generate`, and no
  endpoint accepts a curviness level, ride length, or pace.**
- The wire input carries *only* waypoints — no curviness/pace/length field:
  `record StitchRequestDto(IReadOnlyList<Coord>? Waypoints)`
  ([RouteModels.cs:30](api/Routing/RouteModels.cs)).
- The internal contract is identical and self-documents the gap:
  `record RouteRequest(IReadOnlyList<Coord> Waypoints)` — *"The (future) curviness algorithm
  produces this; the endpoint hands it in directly for now."*
  ([RouteModels.cs:9-14](api/Routing/RouteModels.cs)).
- The seam interface confirms the same tense: *"The endpoint (now) and the curviness
  algorithm (later) depend on this interface"*
  ([IRouteStitcher.cs:5-7](api/Routing/IRouteStitcher.cs)). `StitchAsync(RouteRequest, ct)`
  ([IRouteStitcher.cs:17](api/Routing/IRouteStitcher.cs)) is the only route-producing method,
  and it *stitches*, it does not *shape*.

A repo-wide search for `curviness|curvature|shaping|generate|monoton|segment-reuse|loop-clos`
in `api/` returns **only two doc-comments**, both marking the algorithm as future work
([IRouteStitcher.cs:5](api/Routing/IRouteStitcher.cs),
[RouteModels.cs:11](api/Routing/RouteModels.cs)). No implementation matches.

### (b) What geometry exists pre-stitch? — An arbitrary caller list; nothing shaped for curviness

- **Input**: any ordered waypoint list, `≥ 2` points, validated only for count and
  lat/lng range — never for shape, variety, or curviness
  ([RouteValidation.cs:10-28](api/Routing/RouteValidation.cs)).
- **Output geometry, `fake` provider (default local/CI)**: the input waypoints returned
  **unchanged** as the geometry (straight-line passthrough)
  ([FakeRouteStitcher.cs:15-29](api/Routing/FakeRouteStitcher.cs)). Per test-plan §7 this is
  a test double and must not be asserted on.
- **Output geometry, `openrouteservice` provider**: the waypoints are POSTed to ORS
  `directions/driving-car/geojson` and the returned polyline is decoded
  ([OpenRouteServiceStitcher.cs:26-84](api/Routing/OpenRouteServiceStitcher.cs)). This yields
  the provider's **default/shortest** driving route between the given points — the *opposite*
  of "twisty". Curviness is explicitly **not** sourced from the provider (PRD Open Question 2,
  resolved 2026-08-11: own algorithm). So even the real provider produces no curviness signal;
  curviness must come from *which waypoints* the (missing) algorithm chooses.

Net: the only lever on route character is the waypoint set, and nothing in the codebase
generates that set from a curviness preference.

### (c) Does a curviness / variety metric already exist? — No; only distance helpers

`GeoMath` is the only geometry utility and contains exactly two functions:

- `HaversineMeters(a, b)` — great-circle distance ([GeoMath.cs:8-20](api/Routing/GeoMath.cs)).
- `PathLengthMeters(points)` — summed great-circle length
  ([GeoMath.cs:22-31](api/Routing/GeoMath.cs)).

There is **no** total-curvature (sum of per-vertex turn angles), **no** segment-reuse ratio,
**no** loop-closure gap. Every metric the test-plan names as "what would prove protection"
for Risk #1 ([test-plan.md:69](context/foundation/test-plan.md)) would be net-new code.

### The oracle (from sources — this is what a Face-A test must prove)

Independently of the implementation, the sources define route-quality as:

- **Product thesis / north star** ([roadmap.md:20](context/foundation/roadmap.md)): *"własny
  algorytm curviness potrafi wygenerować realnie przejezdną, faktycznie krętą trasę w limicie
  30 sekund"* — the own algorithm can generate a genuinely rideable, **actually-twisty** route
  within 30 s. Risk #1 is the direct negation of this.
- **FR-003** ([prd.md:67-69](context/foundation/prd.md)): curviness is a slider (straight →
  very twisty), it is RideForge's **own** algorithm, and it is a **preference, not a
  guarantee** — so the correct oracle is *monotonic response* + a *monotony floor*, never an
  absolute "this route is twisty enough" threshold.
- **S-02 outcome + unknown** ([roadmap.md:113-124](context/foundation/roadmap.md)): a higher
  slider must yield geometry that "realnie odzwierciedla" (genuinely reflects) the level; the
  open unknown is literally *"how to measure and verify that a higher slider = an objectively
  curvier route (a curviness metric)"* — i.e. building the Face-B oracle is S-02's own stated
  gap.

Translating to testable invariants (deferred until the algorithm exists — Face A):
1. **Monotonicity**: curviness-metric(route @ slider=high) ≥ curviness-metric(route @ slider=low),
   for the same start/length. Assert against the *independent metric*, never the algorithm's
   self-reported number (the anti-pattern the plan calls out,
   [test-plan.md:69](context/foundation/test-plan.md)).
2. **Monotony floor**: segment-reuse ratio ≤ a floor (no out-and-back / no heavy backtracking);
   loop-closure gap small when a loop is requested.
3. (Adjacent Risk #4, [prd.md:45-56](context/foundation/prd.md), US-01) first point ≈ requested
   start; total duration within ±20% of the **requested** value — assert against the *request*,
   not the algorithm-computed duration.

### Independent-oracle unit tests that ARE writable now (Face B)

Pure functions, judged against synthetic geometry with hand-computed answers — no algorithm,
no network, no DB. Examples of the known-answer fixtures:

- straight 3-point line → total curvature ≈ 0.
- single 180° hairpin → total turn angle ≈ π (known).
- out-and-back (A→B→A) → segment-reuse ratio ≈ 1.0; loop-closure gap ≈ 0 at the *wrong* end
  (catches "closes because it doubled back", not because it looped).
- square loop returning to start → loop-closure gap ≈ 0; reuse ratio ≈ 0.

These are classic xUnit unit tests in the existing `api/RideForgeApi.Tests/` project
([test-plan.md:107](context/foundation/test-plan.md)) and match the "property/unit on shaping
output" layer the plan prefers — except they run against fixtures, because the shaping output
doesn't exist yet. **Caveat**: this requires *writing* the metric functions first, which is
production/helper code and arguably S-02 work, not Lesson-2 testing work.

## Code References

- `api/Routing/RouteModels.cs:9-14` — `RouteRequest`; "future curviness algorithm produces this; endpoint hands it in directly for now"
- `api/Routing/RouteModels.cs:30` — `StitchRequestDto` carries only `Waypoints` (no curviness/pace/length)
- `api/Routing/IRouteStitcher.cs:5-7,17` — the seam is stitch-only; algorithm is "later"
- `api/Program.cs:62,67-90` — only `/health` and `/route/stitch`; no generate endpoint, no curviness param
- `api/Routing/RouteValidation.cs:10-28` — validates count ≥ 2 + lat/lng range only; no shape check
- `api/Routing/FakeRouteStitcher.cs:15-29` — passthrough; returns waypoints unchanged (test double, do not assert on)
- `api/Routing/OpenRouteServiceStitcher.cs:26-84` — ORS `driving-car` shortest route; curviness not provider-sourced
- `api/Routing/GeoMath.cs:8-31` — only `HaversineMeters` + `PathLengthMeters`; no curvature/reuse/loop-closure metric
- `api/RideForgeApi.Tests/` — existing xUnit project (2 files, Phase-1 territory) where any new unit test would land

## Architecture Insights

- **The stitching seam was deliberately built before the algorithm** (F-02 archived
  2026-08-16). The provider-agnostic `Coord`/`RouteRequest`/`StitchedRoute` boundary means the
  future curviness algorithm will plug in *above* `IRouteStitcher`, feeding it waypoints
  ([RouteModels.cs](api/Routing/RouteModels.cs), [IRouteStitcher.cs](api/Routing/IRouteStitcher.cs)).
  Risk #1 lives entirely in that not-yet-built layer.
- **Curviness is intentionally in-house, not provider-sourced** (PRD OQ2 resolved 2026-08-11).
  So no amount of stitcher testing touches Risk #1 — the character of the route is decided by
  waypoint *selection*, which is exactly the missing code.
- **The oracle problem is structural here.** Because the algorithm and its own "twistiness"
  measure will likely be co-designed, the only safe oracle is a metric derived from geometry +
  the PRD/roadmap rubric, kept in a separate module and pinned by known-answer tests *before*
  it is ever pointed at algorithm output. Building that ruler early is the highest-leverage
  pre-algorithm move.

## Historical Context (from prior changes)

- `context/archive/2026-08-16-route-stitching-adapter/research.md` & `plan.md` — F-02 design:
  the stitching adapter explicitly defers to a future curviness algorithm that will supply
  waypoints; provider choice (sparse waypoints vs dense track) was itself left open pending the
  algorithm's output shape.
- `context/archive/2026-08-16-route-stitching-adapter/change.md` — confirms provider/API
  decision "deferred until the curviness algorithm's output shape is known."
- `context/foundation/test-plan.md:86` — Phase 2 row: covers Risks #1+#4, status "not started",
  change folder "—" (now this folder). §3 prose: "Phase 2 tracks S-01/S-02 (the curviness
  algorithm it tests is not built yet)."

## Related Research

- `context/changes/testing-stitching-boundary/` — test-plan Phase 1 (Risks #2/#3/#7), the
  *stitcher* side that already exists; complementary, non-overlapping with this phase.

## Open Questions

1. **Sequencing decision (needs the user).** Given the algorithm doesn't exist, which path for
   Phase 2?
   - **(i) Split the phase** — build + unit-test the independent metric oracle (total curvature,
     segment-reuse ratio, loop-closure) now against synthetic fixtures (Face B); defer the
     monotonicity/floor property tests until S-01+S-02 land (Face A). Highest value now, but the
     metric is production/helper code — a Lesson-2 boundary call.
   - **(ii) Defer the whole phase** — mark Phase 2 blocked-on-algorithm; jump to Phase 3
     (GPX validity + 30 s latency), whose subjects at least partially exist.
2. **If (i): where does the metric live** — a runtime module in `api/Routing/` (reused by the
   algorithm later) or a test-only oracle under `api/RideForgeApi.Tests/`? Affects whether this
   counts as Lesson-2 test work or S-02 feature work.
3. **Loop vs point-to-point** — Risk #1 assumes a *loop* ("out-and-back instead of a loop"), but
   no FR states rides must be loops (US-01 says "departs from start", not "returns to start").
   Is loop-closure an actual product requirement, or only a variety heuristic? Owner: user.
