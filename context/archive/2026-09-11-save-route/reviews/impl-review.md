<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-06 — Save a generated route

- **Plan**: context/changes/save-route/plan.md
- **Scope**: Phases 1–3 of 3 (full plan)
- **Date**: 2026-09-12
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Automated criteria re-run during this review

| Check | Result |
|---|---|
| 1.1 `dotnet build api/RideForgeApi.slnx` | PASS — 0 warnings, 0 errors |
| 1.2 / 2.1 `dotnet test api/RideForgeApi.slnx` | PASS — 102 passed, 6 skipped, 0 failed |
| 1.3 `dotnet tool restore` + `dotnet dotnet-ef migrations list` | PASS — `20260911173501_InitialSavedRoutes` |
| 2.3 Postgres tests skip rather than pass when unset | PASS — all 6 report `[SKIP]` |
| 3.1 `npx tsc --noEmit` | PASS — exit 0 |
| 3.2 `npm run lint` | PASS — exit 0 |
| 1.4 / 2.2 / 2.4 | Won't-do (no local Postgres) — see change.md 2026-09-12 |
| 2.8 Stryker | Optional, not run |

## Findings

### F1 — No executable test covers a successful save

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: api/RideForgeApi.Tests/SavedRoutesEndpointTests.cs, api/RideForgeApi.Tests/SavedRoutesPersistenceTests.cs
- **Detail**: The hermetic suite asserts only 401 / 400 / 503. Every assertion about a *successful* save — the 201/200 body, owner stamping, per-owner uniqueness, idempotency, geometry round-trip — lives in the six `[PostgresFact]` tests, which now never run (1.4 / 2.2 / 2.4 closed as won't-do). Both sibling endpoints ship a hermetic wire-contract test for exactly this reason: `MeEndpointTests.cs:143` and `RouteGenerateEndpointTests.cs:43`. Failure scenario: someone renames `SavedRouteResponseDto.Name` or introduces a JSON naming policy; `dotnet test` stays fully green; `isSavedRoute` (src/api/saved-routes.ts:59) returns false and every rider sees "Got an unexpected response from the server" on a save that actually succeeded.
- **Fix A ⭐ Recommended**: Add a hermetic test that serializes `SavedRouteResponseDto` through the app's JSON options and asserts the literal property names `id` / `name` / `createdAt`, plus one asserting `SaveRouteRequestDto` binds those camelCase names.
  - Strength: Runs on every `dotnet test`, needs no database, and closes the one regression the never-run suite used to cover that the client depends on.
  - Tradeoff: Covers the wire contract only, not the DB rules — those stay review-only.
  - Confidence: HIGH — the same pattern exists twice in this suite.
  - Blind spot: Does not detect an endpoint that returns the right shape with wrong values.
- **Fix B**: Wire a CI Postgres service and run the `[PostgresFact]` suite there.
  - Strength: Restores real coverage of the IDOR guard, idempotency and the geometry round-trip.
  - Tradeoff: Real CI work; test-plan §3 Phase 4 territory, not this change.
  - Confidence: MEDIUM — no CI pipeline exists for the API yet.
  - Blind spot: Have not checked what CI the repo has beyond what is on disk.
- **Decision**: FIXED via Fix A — `SuccessWireContract_IsTheOneTheClientConsumes` in SavedRoutesEndpointTests.cs, serializing through the host's own `JsonOptions`.

### F2 — Npgsql connection pool is uncapped behind the session pooler

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:146-168
- **Detail**: The connection string is passed through untouched, so Npgsql keeps its default `Maximum Pool Size=100`. Supabase's Supavisor **session** pooler holds one dedicated Postgres backend per client connection and allows a small number on the free tier. Failure scenario: a burst of concurrent saves (or Railway running two replicas) drives Npgsql to open more session connections than the pooler allows; the surplus opens time out, `IsDatabaseFailure` converts them to 503 for every rider, and the sanitized log gives no hint that the cause is pool saturation.
- **Fix**: Cap the pool in code rather than trusting an env var — set `MaxPoolSize` (≈8) via `NpgsqlConnectionStringBuilder` inside `UseRideForgeDatabase`, and update the sample string in the startup error message.
- **Decision**: FIXED — `RideForgeDbContextOptions.DefaultMaxPoolSize = 8`, applied in `UseRideForgeDatabase` unless the connection string names its own ceiling.

### F3 — A skipped pre-deploy migration is invisible until saves start failing

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: api/migrate.sh:1-20 (and the deleted api/railway.toml)
- **Detail**: The pre-deploy command now exists only as a Railway dashboard setting; nothing in the repo, the suite, or startup detects that it did not run. Failure scenario: a service is recreated or the dashboard field is cleared, the new app boots against the old schema, and every save fails with SQLSTATE `42P01`, which `IsDatabaseFailure` flattens into the same generic 503 as an unreachable database — indistinguishable in the logs from a network blip.
- **Fix**: Add a non-fatal startup check that calls `GetPendingMigrationsAsync()` on a background task and logs an error naming the pending migration. Keeps the "never migrate on startup" rule (it only reports) while making a skipped pre-deploy loud.
- **Decision**: FIXED — `PendingMigrationsCheck` hosted service in Program.cs; logs the pending migration names, applies nothing, and logs only an exception type name if the database is unreachable (verified against the log-leak test).

### F4 — A Postgres auth failure logs the pooler username and project ref

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:509-518
- **Detail**: `LogDatabaseFailure` states the invariant "no connection detail may reach a log line" and then logs `server?.MessageText` verbatim. For a refused TCP connect (what the test exercises) there is no `PostgresException` and the invariant holds. For a server-side refusal it does not: SQLSTATE `28P01` yields `password authentication failed for user "rideforge_api.<project-ref>"` and `3D000` names the database. Failure scenario: the API password is rotated in Supabase but not on Railway; every save writes the pooler username and Supabase project ref into Railway's log stream. The existing leak test (SavedRoutesEndpointTests.cs:166) asserts only on host and the word `Password`, so it cannot see this.
- **Fix**: Log `MessageText` only for an allow-list of harmless SQLSTATEs (e.g. `23505`, `23514`, `22001`, `42501`) and log the bare SQLSTATE otherwise; extend the leak test with a server-side auth failure case.
- **Decision**: FIXED — LogDatabaseFailure now logs the server message only for statement-level SQLSTATEs (MayLogServerMessage allow-list); everything else keeps the bare code.

### F5 — Geometry is materialised in full before the 20,000-point cap is checked

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:285-354, api/SavedRoutes/SavedRouteValidation.cs:54
- **Detail**: The endpoint sets no request-size limit, so Kestrel's 30 MB default applies and System.Text.Json materialises the whole array before `MaxGeometryPoints` is evaluated. Failure scenario: a signed-in caller POSTs a ~29 MB geometry array; the server allocates hundreds of thousands of `Coord` objects before answering 400, and repeats it concurrently with no rate limit, pushing the container toward OOM and taking generation down with it. The storage half of this is an accepted risk in the plan; the memory amplification before validation is not. Note the same posture exists on the other endpoints, so this is the project's standing default, not something this change introduced.
- **Fix**: Chain `.WithRequestSizeLimit(2_000_000)` onto the endpoint — a full 20,000-point payload is roughly 600 KB, so 2 MB is generous and rejects the abusive case at the transport layer.
- **Decision**: FIXED — middleware sets IHttpMaxRequestBodySizeFeature to 2 MB for POST /saved-routes before the body is read (WithRequestSizeLimit does not exist on minimal-API builders).

### F6 — Three phase-3 hardenings beyond the plan are undocumented

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/lib/route-result-store.ts:52, src/hooks/use-save-route-mutation.ts:18-23,52, src/app/(tabs)/account.tsx:235
- **Detail**: The plan specified `markLastRouteSaved(userId)`; the implementation takes `(clientRouteId, userId)` and no-ops when the store has moved on. The rider id is captured in `onMutate` rather than read at success time. The label is cut to 200 chars client-side (a defensive copy of a server rule). The signed-out Account copy gained a mention of saving. All four are strictly safer or cosmetic, none is recorded in `change.md` — so the next reader cannot tell the two-arg signature is deliberate.
- **Fix**: Add a short `change.md` note for the phase-3 deviations, in the style of the existing phase-1 and phase-2 entries.
- **Decision**: FIXED — change.md gained a 2026-09-12 phase-3 decisions note plus a note listing these review fixes.

### F7 — RLS is an exposure guard, not an ownership guard — S-07 inherits that

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architecture
- **Location**: api/Persistence/Migrations/20260911173501_InitialSavedRoutes.cs:81-86
- **Detail**: The policy is `FOR ALL TO rideforge_api USING (true) WITH CHECK (true)` — exactly what the plan specified, and correct for its stated purpose (keeping the table away from the Data API's anon key). The consequence worth recording is forward-looking: RLS performs no owner scoping, so the only IDOR guard is the LINQ `Where(r => r.OwnerId == ownerId)` at Program.cs:334. That is sound today, because the single read is already owner-filtered and reachable only after a unique violation. S-07's `GET /saved-routes` will inherit a database that happily returns every rider's rows to a query that forgets the filter — and test-plan §8 already names that surface.
- **Fix A ⭐ Recommended**: Record it as a lesson and carry it into S-07's research: add an EF global query filter on `SavedRoute` so the owner predicate cannot be forgotten at a call site.
  - Strength: One place to get right; survives every future read endpoint regardless of who writes it.
  - Tradeoff: Global filters need the owner in scope, so it needs a per-request accessor rather than a static.
  - Confidence: MEDIUM — the mechanism is standard EF, but the wiring belongs to S-07's design, not this change.
  - Blind spot: Have not evaluated how the filter interacts with the migration path or with future admin reads.
- **Fix B**: Scope the RLS policy itself with a session GUC (`SET LOCAL app.owner_id`, `USING (owner_id = current_setting(...)::uuid)`).
  - Strength: Enforcement sits in the database, below any application bug.
  - Tradeoff: Every request must set the GUC on its own connection — awkward with pooling, and it would have to be right before it protects anything.
  - Confidence: LOW — untested against Supavisor session pooling in this project.
  - Blind spot: No verification that the GUC survives the pooler's connection reuse.
- **Decision**: ACCEPTED — S-07 owns the mitigation: its `GET` filters by `ownerId` in the query, and test-plan §8 already requires an IDOR read test for it. The database stays a single-layer guard at this scale; a global query filter is S-07's call, not this slice's.

### F8 — Request shaping lives in src/hooks/ rather than src/api/

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/hooks/use-save-route-mutation.ts:18-36
- **Detail**: `toSaveRouteRequest` and `fitStartLabel` are pure functions with no React dependency, exported from a hooks module and imported directly by a component. The established direction is that request shaping lives in `src/api/` and hooks only orchestrate — `use-generate-route-mutation.ts` calls `generateRoute(request)` with a body the screen built. Nothing breaks; it just makes `src/hooks` a place components import non-hook helpers from.
- **Fix**: Move both into `src/api/saved-routes.ts`, next to `MAX_START_LABEL_LENGTH`, which is the constant `fitStartLabel` enforces.
- **Decision**: FIXED — toSaveRouteRequest + fitStartLabel moved to src/api/saved-routes.ts with a structural SaveRouteInput type, so the api layer never imports the store.

### F9 — Postgres test helper parses the body before asserting the status

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: api/RideForgeApi.Tests/SavedRoutesPersistenceTests.cs:40-47
- **Detail**: `Save` deserialises into `SavedRouteResponseDto` before the caller checks the status. Because it is a positional record, System.Text.Json fills missing constructor parameters with defaults instead of throwing, so a 400 or 503 ProblemDetails body parses into `Id = Guid.Empty, Name = null` and passes `Assert.NotNull`. A regression that makes the first save 503 therefore surfaces later as a confusing `Assert.Equal(Created, OK)` or a `SingleAsync` that finds no row. `MeEndpointTests` asserts the status first.
- **Fix**: Assert `response.IsSuccessStatusCode` (with the body text in the failure message) inside `Save` before parsing.
- **Decision**: FIXED — Save asserts response.IsSuccessStatusCode (with the body in the message) before deserialising.

### F10 — Migration credentials on the command line; silent localhost fallback

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/migrate.sh:20, api/Persistence/DesignTimeDbContextFactory.cs:30-34
- **Detail**: Two small operator-surface issues. The migrator connection string, password included, is passed as an `argv` element to `efbundle`, so it is readable from `/proc/<pid>/cmdline` inside the container and would appear in a core dump. Separately, with `ConnectionStrings__RideForgeMigrations` unset the design-time factory falls back silently to `Host=localhost;Username=postgres;Password=postgres` — the doc comment warns that `database update` connects, but nothing enforces it. Both are low-impact here (container-local; and there is no local Postgres on this machine by decision).
- **Fix**: Let `efbundle` read the existing `ConnectionStrings__RideForgeMigrations` environment variable instead of passing `--connection`, and gate the localhost fallback behind an explicit opt-in variable.
- **Decision**: ACCEPTED (risk) — container-local exposure only, and the localhost fallback cannot bite while no local Postgres exists (see change.md, 2026-09-12). The fix touches the deploy path and cannot be verified without a deploy.

## What was verified clean

- **Ownership and the write-path IDOR guard.** Owner comes only from `SubjectOf(user)`; the request DTO has no owner field to bind; the unique index is `(owner_id, client_route_id)`, never `client_route_id` alone, so rider B saving rider A's id creates a new row rather than being handed A's.
- **Idempotency.** Insert-then-catch-unique-violation, matched by constraint *name* so an unrelated unique violation can never be answered with an existing row; the poisoned context is re-read with `AsNoTracking` and never saved again; first-write-wins.
- **Schema isolation.** Table, index, grants, RLS and policy all land in one migration; the migrations history table lives in `rideforge`; nothing is created in `public`; no FK to `auth.users`; no `Migrate()` on API startup.
- **No committed credentials.** Empty connection string in `appsettings.json`; placeholder passwords in the bootstrap SQL; the test password is a deliberate canary for a port nothing listens on.
- **React correctness.** `useSyncExternalStore`'s snapshot is the module-level object, replaced rather than mutated on every write, so there is no render loop; no conditional hooks; `useState(getLastRoute)` is a correct lazy initialiser.
- **Cross-rider state on one device.** The signed-in subtree is keyed by `user.id`, `savedBy` is compared against the current user, `markLastRouteSaved` no-ops on a stale ride, and the rider is captured at request time.
- **lessons.md compliance.** The native-module lesson is respected and generalised: `src/lib/uuid.ts` requires `expo-crypto` lazily inside a try/catch with a documented non-secret fallback. No native dependency added — Metro reload only.
- **Client patterns.** `saved-routes.ts` mirrors `route.ts` (auth, shape guard, `ApiError('parse')`); `saveErrorMessage` follows the `planErrorMessage` shape; barrel exports follow the value/type-pair convention; the 200-char label rule is kept in sync across the boundary and neither side splits a surrogate pair.
- **"What We're NOT Doing"** — all respected: no list/rename/delete endpoint, no rate limit or per-rider cap, no FK to `auth.users`, no startup migrations, no Docker/Testcontainers, no frontend test runner, no GPX export, and the Plan screen's only change is the three-line `startLabel` pass-through.
