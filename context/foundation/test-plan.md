# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-08-16 (§3 Phase 1 → change opened)

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic geometric metric that already
   catches the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the
   developer is worried about X, and the failure would surface somewhere in
   `<area>`" carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `src/`, `api/` (excluding
docs, archive, `obj/`, `bin/`, generated).

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | Generated routes are monotone / self-repeating — reused road segments, an out-and-back instead of a loop, and a curviness slider that doesn't visibly change the route — so the core product thesis doesn't deliver | High | High | interview Q1 + Q3; roadmap north-star thesis line; PRD FR-003 |
| 2 | OpenRouteService drifts (response shape / path / contract) and generation silently breaks — empty or wrong geometry, or a failure that lands as the wrong HTTP status the client can't interpret | High | Medium | interview Q2; hot-spot dir `api/Routing/` (8 commits/30d) |
| 3 | GeoJSON `[lng,lat]` axis mishandling renders a plausible-but-wrong route that doesn't depart from the real start or follow real roads | High | Medium | archive `route-stitching-adapter/plan.md` ("single most error-prone line"); hot-spot dir `api/Routing/` (8 commits/30d) |
| 4 | Route breaks a US-01 guarantee — total duration falls outside ±20% of the requested value, or the route doesn't depart from the specified start | High | Medium | PRD Success Criteria + US-01 acceptance; roadmap slice S-01 |
| 5 | Generation blows the 30s NFR for long / touristic rides — the own algorithm plus the external ORS round-trip accumulate past budget and the rider gets a timeout, not a route | High | Medium | PRD NFR-01; infrastructure.md risk register (M/H); archive F-02 latency note |
| 6 | Exported GPX is structurally invalid and fails to load in Garmin / Google Maps / OsmAnd — the rider discovers it once already on the bike | High | Medium | PRD Guardrails (broken GPX = hard regression); US-01 acceptance; roadmap slice S-04 |
| 7 | The `/route/stitch` path echoes the server-side ORS key into an error body or log, or is hammered unmetered so each call triggers a billed external directions request | Medium | Medium | archive F-02 (key held server-side, wide-open MVP CORS); abuse lens (secret leakage + resource abuse) |

**Impact × Likelihood rubric.** Both axes on a coarse High / Medium / Low
scale so two readers agree on the same row.

| Rating | Impact | Likelihood |
|--------|--------|------------|
| High   | user loses access, data, or money; failure is publicly visible | area changes weekly, or we have already been burned here |
| Medium | feature degrades, a workaround exists, only some users affected | touched occasionally, has been a source of bugs |
| Low    | cosmetic, easily reverted, no data effect | stable code, rarely touched |

Protect Risk #1 (High × High) first. The infra risk register's App-Sleeping
cold-start and JWT-secret-mismatch items are real but belong to
deploy-checklist / observability, not a test — they are deliberately not in
this map.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | A curviness / variety metric (total curvature, segment-reuse ratio, loop-closure) computed from route geometry responds monotonically to the slider and stays above a monotony floor | "The algorithm's own output is the definition of twisty/varied" | where shaping output is produced; what geometry exists pre-stitch; whether a metric already exists | property/unit on shaping output (optional AI-native judge as coarse gate) | oracle problem — asserting against the algorithm's own numbers instead of an independent rubric |
| #2 | A pinned ORS response fixture decodes correctly; a drifted or garbage response surfaces as a clean, status-coded error and never as empty-success | "A 200 with empty geometry means success" | which ORS contract fields are relied on; how empty/garbage responses are handled today | contract test on a recorded fixture + hermetic decode | flaky live-network test in CI; testing the `fake` stitcher (a test double) |
| #3 | A known input coordinate decodes to the correct `{lat,lng}` and is not swapped end-to-end | "It rendered on the map, so the axis order is right" | how much of the axis decode the existing xUnit already covers | unit on the decode | duplicating a decode test that already exists — verify current coverage first |
| #4 | The generated route's first point ≈ the requested start, and total duration is within ±20% of the **requested** value | "A passing short happy-path ride implies all rides pass" | how duration is computed; the start-injection path into the route | property / integration | asserting against the algorithm-computed duration instead of the request (tautology) |
| #5 | A worst-case long/touristic generation completes under the 30s budget with the provider call time-bounded and cancellation threaded | "Short-ride latency generalises to long rides" | waypoint-count bounds; the timeout + cancellation chain across client→endpoint→provider | integration latency assertion with stubbed provider timing | happy-path-only timing; depending on real network latency in CI |
| #6 | The generated GPX validates against the GPX 1.1 schema and round-trips a parse; the 3-app device load stays a documented manual smoke | "Well-formed XML equals a loadable GPX" | where GPX is generated; which namespace/schema version is targeted | schema-validate + fixture round-trip (plus documented device smoke) | meaningless snapshot; silently dropping the real-device check |
| #7 | Error responses and logs provably never contain the key; abusive call volume is bounded | "Key held server-side means the key can never leak" | the error-mapping and logging path; what actually reaches the response body | unit on error/log output | testing the absence of a rate-limit that doesn't exist — assert leakage now, treat rate-limiting as a hardening control to add |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|---------------|------------|--------|---------------|
| 1 | Harden the stitching boundary | Pin the ORS contract, decode, and error/leak behaviour on code that is live now | #2, #3, #7 | unit + contract | change opened | context/changes/testing-stitching-boundary/ |
| 2 | Route-shaping quality harness | Prove the thesis with measurable route-quality invariants against independent oracles | #1, #4 | property/unit + integration (optional AI-native judge) | not started | — |
| 3 | Output & budget guardrails | Defend GPX validity and the 30s worst-case | #5, #6 | schema-validate + integration latency; manual device smoke | not started | — |
| 4 | Quality-gates wiring | Lock the floor: `dotnet test` + lint + typecheck in CI | cross-cutting | gates | not started | — |

**Status vocabulary** (fixed — parser literals): `not started` →
`change opened` → `researched` → `planned` → `implementing` → `complete`.

Phase 1 comes first because it hardens code that already exists (F-02's ORS
adapter, a hot-spot) using the xUnit project already on disk — the cheapest,
highest-signal start. Phases 2 and 3 land alongside the product roadmap:
Phase 2 tracks S-01/S-02 (the curviness algorithm it tests is not built
yet), Phase 3 tracks S-01 latency and S-04 (GPX). Phase 4 wires the floor
once earlier phases have produced tests worth gating on.

## 4. Stack

The classic test base for this project. AI-native tools carry a `checked:`
date so future readers can see which lines need re-verification.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| unit + integration (backend) | xUnit (`api/RideForgeApi.Tests/`) | net10 | Already configured; 2 files, all in `api/Routing/`. Run with `dotnet test`. Extend here for Phases 1–3 |
| HTTP boundary (backend) | fake `HttpMessageHandler` | n/a | Precedent set by F-02's ORS tests — hermetic, no network. Mock only at the provider edge |
| GPX schema validation | GPX 1.1 XSD validation (.NET `XmlReader` + schema) | n/a | none yet — see §3 Phase 3 |
| unit + integration (frontend) | none — deliberately not added for MVP | n/a | Expo `src/` is thin and deprioritized (§7). Add a JS runner only if a specific frontend risk earns it |
| e2e / on-device | none | n/a | GPX 3-app load is a documented manual smoke (§3 Phase 3), not automated |
| (optional) AI-native | LLM/vision route-character judge — checked: 2026-08-16 | n/a | Coarse gate for Risk #1 only. When NOT to use: any invariant expressible as a deterministic geometric metric (segment-reuse, ±20% duration, departs-from-start) — those stay classic |

**Stack grounding tools (current session):**
- Docs: Context7 MCP — available; use for exact xUnit / ASP.NET Core / GPX-in-.NET APIs and Expo SDK 56 specifics when a phase needs them; checked: 2026-08-16
- Search: web search MCP — available; use only to confirm a tool's current status, then prefer official docs; checked: 2026-08-16
- Runtime/browser: in-app browser tool — available; a possible visual/device layer, but not used at MVP (frontend deprioritized); checked: 2026-08-16
- Provider/platform: none detected (no GitHub/Railway/Supabase MCP in session); Railway CI/secrets remain manual per infrastructure.md; checked: 2026-08-16

Use docs MCPs for current framework/library APIs. Use search MCPs for
discovery or current status only. Do not use MCP docs/search to infer code
failure anchors; those belong in per-phase `/10x-research`.

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.
"Required after §3 Phase N" means the gate is enforced once that rollout
phase lands; before that, the gate is `planned`.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| lint + typecheck (`expo lint`, `dotnet build`) | local + CI | required | syntactic / type drift (strict TS, no `any`) |
| unit + contract (`dotnet test`) | local + CI | required after §3 Phase 1 | ORS decode / status-mapping / leak regressions |
| route-quality invariants | local + CI | required after §3 Phase 2 | monotone routes, ±20% / departs-from-start breaks |
| GPX schema validation | CI on PR | required after §3 Phase 3 | structurally invalid GPX |
| latency budget assertion | CI on PR | required after §3 Phase 3 | worst-case generation past 30s NFR |
| on-device GPX smoke (Garmin / Maps / OsmAnd) | manual, pre-release | recommended | environment-specific load failures classic tests can't reach |

Every row corresponds to a gate that either is wired or will be wired by a
named rollout phase. CI configuration itself (GitHub Actions YAML) is owned
by the module's CI lesson; §3 Phase 4 names it.

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once the
relevant rollout phase ships; before that, it reads "TBD — see §3 Phase N."

### 6.1 Adding a unit test (backend)

- **Location**: `api/RideForgeApi.Tests/`, one file per unit under test.
- **Naming**: `<Type>Tests.cs`.
- **Reference test**: `api/RideForgeApi.Tests/FakeRouteStitcherTests.cs`.
- **Run locally**: `dotnet test`.
- (Established convention; Phase 1 extends it — expect refinements then.)

#### 6.1.1 Adding a real-Postgres test (backend)

- **When**: the rule lives in the database — a constraint, a per-owner unique index, what `jsonb`
  stores. A stubbed context would fake exactly that. Everything else stays hermetic (see
  `SavedRoutesEndpointTests`, which points at a dead port so a request that slips validation shows
  up as 503, not a pass).
- **How**: mark the test `[PostgresFact]` and take `IClassFixture<PostgresApiFactory>`. The fixture
  applies the migrations once, gives out signed-in riders via `NewRider()` and deletes their rows
  when it is done. `QueryAsync` reads the database directly; use raw SQL when the storage shape
  itself is the thing under test.
- **Ids**: every test mints its own riders and client route ids. Never share rows across tests.
- **Run locally**: `RIDEFORGE_TEST_DB="Host=localhost;Port=5432;Database=rideforge_tests;Username=postgres;Password=…" dotnet test api/RideForgeApi.slnx`
  against a disposable local Postgres, connected as its owner (the migration creates the
  `rideforge_api` role). Never point it at Supabase.
- **Gate**: ad hoc. With the variable unset these tests report as *skipped*, not passed — watch the
  skipped count. The owner connection bypasses RLS, so a missing RLS policy for `rideforge_api` is
  only caught by a deployed save.
- **Reference test**: `api/RideForgeApi.Tests/SavedRoutesPersistenceTests.cs`.

### 6.2 Adding a contract / decode test (ORS boundary)

- TBD — see §3 Phase 1 (ORS drift + `[lng,lat]` axis decode, Risks #2/#3).

### 6.3 Adding a route-quality invariant test

- TBD — see §3 Phase 2 (curviness/variety metric + ±20% / departs-from-start, Risks #1/#4).

### 6.4 Adding a GPX-validity test

- TBD — see §3 Phase 3 (GPX 1.1 schema validation, Risk #6).

### 6.5 Adding a latency-budget test

- TBD — see §3 Phase 3 (worst-case 30s NFR, Risk #5).

### 6.6 Per-rollout-phase notes

(Optional. After each phase lands, `/10x-implement` appends a 2–3 line note
here capturing anything surprising the phase taught.)

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview). Future
contributors should respect these unless the underlying assumption changes.

- **The Expo frontend UI (`src/app/`, `src/components/`)** — solo developer
  with full control who verifies every UI change by hand; snapshot/UI tests
  would break on style nudges and catch little. Re-evaluate if a second
  contributor joins or the UI gains non-trivial client logic. (Source:
  Phase 2 interview Q4 + Q5.)
- **The `fake` route stitcher itself** — it is a test double (straight-line
  passthrough); testing it is testing the test. Use it, don't assert on it.
  Re-evaluate if the fake grows real logic. (Source: cost × signal.)
- **A rate-limit on `/route/stitch`** — not tested because it does not exist
  yet; it is a hardening *control to add*, not a test target. Risk #7 tests
  key-leakage now; revisit rate-limiting when abuse volume becomes real.

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-08-16
- Stack versions last verified: 2026-08-16
- AI-native tool references last verified: 2026-08-16

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive (e.g. auth/saved
  routes S-05/S-06 landing introduces an IDOR surface — a rider reading
  another rider's saved routes — not yet in this map),
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
