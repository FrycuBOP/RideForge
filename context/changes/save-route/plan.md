# S-06 — Save a generated route Implementation Plan

## Overview

A signed-in rider taps **Save** on the result screen and the route they just generated — geometry,
stats, the inputs that produced it, and an auto-derived name — is stored against their account
(FR-009). This slice introduces persistence to the project: EF Core + Npgsql in the .NET API,
writing to the Postgres database that already ships with the Supabase project S-05 set up for auth.

Ownership comes from the verified JWT `sub` claim, never from the request body. Saves are
idempotent: a double tap or a retry after a lost response returns the existing row instead of
creating a duplicate. Signed-out riders see **Sign in to save**, which gives the account a second
concrete reason to exist after the generation quota.

## Current State Analysis

- **No persistence anywhere.** No DbContext, ORM, migrations, or DB driver in
  [api/RideForgeApi.csproj](api/RideForgeApi.csproj). S-05 deliberately kept the database out
  ("Persistence arrives with S-06"). The Supabase project exists and is used for auth only; its
  Postgres is untouched.
- **The trust boundary is proven.** [api/Program.cs](api/Program.cs) validates Supabase JWTs against
  the project JWKS; `/me` already reads the `sub` claim. `AuthenticatedApiFactory` in
  [RideForgeApiFactory.cs](api/RideForgeApi.Tests/RideForgeApiFactory.cs) mints valid tokens
  hermetically.
- **The generate response loses its inputs.** `POST /route/generate` returns only `geometry`,
  `distanceMeters`, `durationSeconds` ([RouteModels.cs](api/Routing/RouteModels.cs)). The start
  point, the start label the rider typed, and the requested km exist only on the Plan screen
  ([index.tsx](src/app/(tabs)/index.tsx)) and in the mutation's variables.
- **The result is held in memory.** [route-result-store.ts](src/lib/route-result-store.ts) keeps
  the last `GeneratedRoute` for the life of the process; [result.tsx](src/app/result.tsx) and
  [result.web.tsx](src/app/result.web.tsx) snapshot it via `useState` on mount. Both screens' empty
  states say "Generated routes aren't saved yet".
- **Established patterns to follow.** Fail-fast startup config (`Program.cs` throws on a blank
  `Supabase:ProjectUrl`); options classes with `SectionName`; validation as a static class returning
  an error string (`RouteValidation`); `ApiError` + a per-screen `…ErrorMessage` mapper on the
  client; `request(..., { auth: true })` for authenticated calls; a lazily required `expo-crypto`
  UUID in [install-id.ts](src/lib/install-id.ts) per [lessons.md](context/foundation/lessons.md).
- **No local infra.** Docker and `dotnet-ef` are not installed on the dev machine.
- **IDOR is already flagged.** [test-plan.md §8](context/foundation/test-plan.md) names "a rider
  reading another rider's saved routes" as a surface S-05/S-06 introduce.

## Desired End State

Signed in, the rider generates a route, taps **Save route**, sees **Saving…** then **Saved ✓**. The
row exists in `rideforge.saved_routes` owned by their Supabase user id, with the geometry in order,
the stats, the start point and label, the requested km, and a name like
`Loop from Kraków · 42 km`. Tapping again, or retrying after a timeout, never creates a second
row. Signed out, the button reads **Sign in to save**, leads to the Account tab, and after signing
in the Account tab offers **Back to your route**, where saving works. A different rider signing in
on the same device never sees the previous rider's **Saved ✓**.

The table is unreachable through Supabase's Data API with the anon key the app ships.

Verified by: `dotnet test api/RideForgeApi.slnx` (hermetic suite always; Postgres suite with
`RIDEFORGE_TEST_DB` set), `npx tsc --noEmit`, `npm run lint`, and a manual pass on the existing EAS
dev build (no new native module — Metro reload only).

### Key Discoveries:

- **Supabase's `public` schema is exposed to the anon key.** The Data API (PostgREST) serves
  `public`, and tables created by SQL/ORM there have no RLS by default. The anon key ships inside the
  app, so a `public.saved_routes` table would be readable and writable by anyone. Supabase's own
  server-ORM quickstarts put tables in a custom schema instead. → Table lives in schema
  `rideforge`, with RLS enabled as a second layer.
- **Railway must use the Supavisor session pooler.** Supabase's direct connection resolves to IPv6;
  the session pooler (`aws-0-<region>.pooler.supabase.com:5432`, username
  `<role>.<project-ref>`) supports IPv4 and prepared statements. The transaction pooler (6543)
  would require disabling Npgsql prepared statements — not worth it at this scale.
- **EF Core 10 maps the point list to one column.** `ComplexCollection(r => r.Geometry, b =>
  b.ToJson())`; Npgsql defaults JSON mappings to `jsonb`.
- **`dotnet ef` would trip the fail-fast checks.** The EF tools build the app host to find the
  DbContext, which runs `Program.cs` up to `Build()` — including the throws on blank
  `Supabase:ProjectUrl` and the new connection string. An `IDesignTimeDbContextFactory` makes the
  tools bypass the host entirely.
- **xUnit 2.9 has no runtime skip.** A `FactAttribute` subclass that sets `Skip` in its constructor
  when an env var is missing gives opt-in tests with no new package.
- **The existing UUID helper already respects the native-module lesson.** `install-id.ts` requires
  `expo-crypto` lazily with a `Math.random` fallback. Extracting it lets the route id reuse it —
  this slice adds no native dependency.

## What We're NOT Doing

- **No listing, reading, renaming, or deleting saved routes.** S-07 (`saved-routes-list`) owns
  `GET`. No read endpoint means no IDOR read surface yet — S-07 must test it.
- **No per-rider cap or save rate limit.** Payload bounds only (decision; see Open Risks).
- **No foreign key to `auth.users`.** It would break migrations on a plain local Postgres (no `auth`
  schema) and account deletion is out of scope anyway.
- **No migrations on startup.** Applied by hand with `dotnet ef database update`.
- **No Docker / Testcontainers.** Real-Postgres tests are opt-in via an env var.
- **No frontend test runner** ([test-plan.md §4](context/foundation/test-plan.md)).
- **No GPX export from saved routes** (S-04), no editable name sheet, no toast library.
- **No change to generation, the quota, or the Plan screen's inputs** beyond passing the start
  label through to the result store.

## Implementation Approach

Backend first, in two steps, because the client has nothing to call until the endpoint is deployed:

1. **Foundation** — packages, DbContext, entity, schema, first migration, config, design-time
   factory; the manual Supabase/Railway setup lands the schema and the runtime credentials before
   any code that needs them is deployed.
2. **Endpoint** — `POST /saved-routes`: authorize → validate → derive name → insert → on unique
   violation return the existing row. Hermetic tests pin the boundary (401/400/503); opt-in
   Postgres tests pin the rules a stub would lie about (owner stamping, per-owner uniqueness,
   idempotency, geometry round-trip).
3. **Client** — carry the generation inputs and a client route id through the result store, then a
   shared Save action on both result screens.

Phase 2 is a good `/10x-tdd` candidate — its first red test is nameable in one sentence: *"saving the
same `clientRouteId` twice as the same rider returns the first row's id and leaves exactly one
row."* Phases 1 and 3 are setup and UI wiring — `/10x-implement`.

## Critical Implementation Details

**Deploy ordering.** Phase 1 adds a fail-fast check on `ConnectionStrings:RideForge`. Deploying that
code before the Railway variable exists crash-loops the API — generation included. Set the variable
(and apply the migration, create the role) first, then deploy.

**Idempotency is a constraint, not a pre-check.** Insert, and when `SaveChangesAsync` throws a
`DbUpdateException` whose inner `PostgresException.SqlState` is `23505` (unique violation), load the
existing row by `(owner_id, client_route_id)` and return it. A select-then-insert leaves a race
between two concurrent taps; the constraint closes it. After the failed save the entity is still
tracked as `Added` — read the existing row with a no-tracking query and do not call `SaveChanges`
again on that context. First write wins: a repeat carrying a different payload returns the original
row unchanged.

**The unique rule is per owner.** `UNIQUE (owner_id, client_route_id)`, never `client_route_id`
alone. With a global unique, rider B saving an id rider A already used would hit the conflict branch
and be handed **rider A's row** — an IDOR leak through the write path. The Postgres suite pins this.

**"Saved ✓" belongs to a rider.** The result store records which user saved the route. The Save
action shows **Saved ✓** only when that matches the current session's user id; otherwise it offers
**Save route** again (and the server's per-owner rule makes that a genuine new row for the new
rider).

**RLS applies to the runtime role.** Only the table owner (and superusers / `BYPASSRLS` roles)
skip row-level security. `rideforge_api` is neither, so with RLS enabled and no policy naming it,
every insert fails and every select returns zero rows. The migration therefore ships a policy scoped
to `rideforge_api`. The Postgres test suite connects as the owner and **cannot** catch a missing
policy — only the phase 1 role check and the phase 2 deployed save can.

**Connection string format.** Npgsql takes key-value form, not the `postgresql://` URI the Supabase
dashboard displays: `Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;
Username=rideforge_api.<project-ref>;Password=…;SSL Mode=Require`.

---

## Phase 1: Persistence foundation (backend)

### Overview

Stand up the data layer and land the schema in Supabase, with no endpoint yet. At the end the API
boots against Supabase through the pooler, the existing suite is green, and the table exists where
the Data API cannot reach it.

### Changes Required:

#### 1. Packages and tooling

**File**: `api/RideForgeApi.csproj`, `.config/dotnet-tools.json`

**Intent**: Add the EF Core Postgres provider and make `dotnet ef` reproducible for anyone cloning
the repo.

**Contract**: `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design`
(`PrivateAssets="all"`), both on the `10.0.x` line matching the existing ASP.NET packages.
`dotnet-ef` `10.0.x` as a repo-local tool (`dotnet new tool-manifest` + `dotnet tool install
dotnet-ef`), invoked as `dotnet tool run dotnet-ef` / `dotnet dotnet-ef`.

#### 2. Entity

**File**: `api/Persistence/SavedRoute.cs`

**Intent**: The stored shape of a saved ride.

**Contract**: `Id` (Guid, server-generated), `OwnerId` (Guid — the `sub` claim), `ClientRouteId`
(Guid), `Name` (string), `StartLat`/`StartLng` (double), `StartLabel` (string?, nullable),
`RequestedDistanceKm` (double), `DistanceMeters` (double), `DurationSeconds` (double), `Geometry`
(list of `Coord`), `CreatedAt` (DateTimeOffset, UTC). Reuses `RideForgeApi.Routing.Coord`.

#### 3. DbContext

**File**: `api/Persistence/RideForgeDbContext.cs`

**Intent**: Map the entity to Postgres in a schema the Supabase Data API does not expose.

**Contract**: `DbSet<SavedRoute> SavedRoutes`. `HasDefaultSchema("rideforge")`; table
`saved_routes` with snake_case columns; `Geometry` as a complex collection mapped `ToJson()`
(→ `jsonb`); unique index on `(OwnerId, ClientRouteId)`; `Name` max length 120, `StartLabel` max
length 200; `CreatedAt` defaulting to `now()`. The migrations history table also lives in
`rideforge` (`MigrationsHistoryTable("__EFMigrationsHistory", "rideforge")`) so nothing lands in
`public`.

#### 4. Design-time factory

**File**: `api/Persistence/DesignTimeDbContextFactory.cs`

**Intent**: Let `dotnet ef` build the context without booting the app host, whose fail-fast checks
would otherwise throw.

**Contract**: `IDesignTimeDbContextFactory<RideForgeDbContext>` reading the connection string from
the `ConnectionStrings__RideForge` environment variable, falling back to a local placeholder so
`migrations add` works with nothing set. `database update` is always run with the variable set
explicitly to the target database.

#### 5. Startup registration + fail-fast config

**File**: `api/Program.cs`, `api/appsettings.json`

**Intent**: Register the context and refuse to boot on a missing connection string, matching the
stitching and Supabase precedent.

**Contract**: `AddDbContext<RideForgeDbContext>(o => o.UseNpgsql(connectionString))` reading
`ConnectionStrings:RideForge`; throw `InvalidOperationException` at startup when it is blank, naming
the `ConnectionStrings__RideForge` variable in the message. `appsettings.json` gains a
`ConnectionStrings` section with an empty `RideForge` value. Registration opens no connection —
nothing touches the database until a request needs it.

#### 6. First migration

**File**: `api/Persistence/Migrations/` (generated)

**Intent**: Create the schema, table, and index; create the runtime role and its grants so they are
versioned with the schema; enable RLS as defense in depth.

**Contract**: Generated with `dotnet ef migrations add InitialSavedRoutes`, then hand-extended with
`migrationBuilder.Sql(...)` for, in order:
1. Create role `rideforge_api` **NOLOGIN** if it does not exist (a `DO` block checking `pg_roles` —
   roles are cluster-wide, so a re-run or a second database must not fail).
2. `GRANT USAGE ON SCHEMA rideforge` and `GRANT SELECT, INSERT ON rideforge.saved_routes` to
   `rideforge_api`.
3. `ALTER TABLE rideforge.saved_routes ENABLE ROW LEVEL SECURITY`.
4. A policy `FOR ALL TO rideforge_api USING (true) WITH CHECK (true)`.

`Down` reverses 4→2 (the role is left in place — dropping a role that may own grants elsewhere is
not a migration's job). Step 4 is not optional: see Critical Implementation Details. No password is
in the migration; phase 1's manual setup grants `LOGIN` and sets it. Commit the generated snapshot.

#### 7. Test factory placeholder

**File**: `api/RideForgeApi.Tests/RideForgeApiFactory.cs`

**Intent**: Keep every existing suite booting now that startup requires a connection string.

**Contract**: `ConfigureWebHost` also sets `ConnectionStrings:RideForge` to an unroutable
placeholder (`Host=rideforge-tests.invalid;…`), with a doc comment in the same style as the
`ProjectUrl` one: never dialled, and a hermetic test that accidentally reaches the database fails
loudly rather than silently touching a real one.

#### 8. Supabase + Railway setup (manual, out of repo)

**Intent**: Land the schema and give the API least-privilege credentials before code that needs
them is deployed.

**Contract**, in order:
1. Apply the migration to Supabase: `ConnectionStrings__RideForge="<session-pooler string as
   postgres.<ref>>" dotnet dotnet-ef database update --project api`.
2. In the Supabase SQL editor, give the role (created NOLOGIN by the migration) a login:
   `alter role rideforge_api with login password '<generated>';` — the password goes only into
   Railway, never into the repo.
3. Set `ConnectionStrings__RideForge` on Railway to the session-pooler string with username
   `rideforge_api.<project-ref>` (key-value form — see Critical Implementation Details).
4. Deploy.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build api/RideForgeApi.slnx`
- Existing suite still passes with the placeholder connection string: `dotnet test api/RideForgeApi.slnx`
- Migration generates and the tool resolves via the manifest: `dotnet tool restore && dotnet dotnet-ef migrations list --project api`
- Migration applies cleanly to a local Postgres: `ConnectionStrings__RideForge=<local> dotnet dotnet-ef database update --project api`

#### Manual Verification:

- Migration applied to Supabase; `rideforge.saved_routes` visible in the table editor with RLS on and nothing created in `public`
- A Data API request with the anon key (`GET {project-url}/rest/v1/saved_routes` and with `Accept-Profile: rideforge`) cannot read the table
- Connected with the `rideforge_api` credentials (psql or any DB client via the session pooler), `select count(*) from rideforge.saved_routes` returns 0 — not a permission error
- Railway deploy boots and logs no startup exception; `/health` and anonymous generation still work
- Starting the API locally with `ConnectionStrings__RideForge` unset fails loudly at startup

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: `POST /saved-routes` + tests (backend)

### Overview

The save endpoint, its validation and naming rules, and the two test layers that pin it. Deployed
to Railway at the end so phase 3 has something real to call.

### Changes Required:

#### 1. Wire contract

**File**: `api/SavedRoutes/SavedRouteModels.cs`

**Intent**: Define the request and response bodies, kept separate from the entity so the wire and
storage shapes can evolve independently (the `RouteModels.cs` precedent).

**Contract**: `SaveRouteRequestDto(Guid? ClientRouteId, Coord? Start, string? StartLabel, double?
RequestedDistanceKm, IReadOnlyList<Coord>? Geometry, double? DistanceMeters, double?
DurationSeconds)` — camelCase on the wire. **No owner field**: anything the client sends as an owner
is ignored by binding. `SavedRouteResponseDto(Guid Id, string Name, DateTimeOffset CreatedAt)`.

#### 2. Validation

**File**: `api/SavedRoutes/SavedRouteValidation.cs`

**Intent**: Reject malformed or oversized saves with 400 before the database is touched.

**Contract**: Static `Validate(SaveRouteRequestDto?) → string?` in the `RouteValidation` style.
Rules: `ClientRouteId` present and not `Guid.Empty`; `Start` present and in range (reuse the
coordinate-range rule); `RequestedDistanceKm` in `(0, RouteValidation.MaxDistanceKm]`; `Geometry`
between 2 and `MaxGeometryPoints` (20,000) points, every point in range; `DistanceMeters` and
`DurationSeconds` finite and `> 0`; `StartLabel`, when present, at most 200 characters.

#### 3. Naming

**File**: `api/SavedRoutes/SavedRouteNaming.cs`

**Intent**: Derive the auto-name on the server so it is consistent and unit-testable.

**Contract**: Pure `Derive(string? startLabel, double distanceMeters) → string`:
`Loop from {label} · {km} km`, where `label` is the trimmed start label cut to 40 characters with an
ellipsis when longer, and `km` is the **actual** stitched distance rounded to a whole kilometre (the
ride the rider saw on the stats card, not the requested one). A blank or missing label yields
`Loop · {km} km`. Always within the 120-character column limit.

#### 4. Endpoint

**File**: `api/Program.cs`

**Intent**: Persist a route for the authenticated rider, idempotently.

**Contract**: `app.MapPost("/saved-routes", ...).RequireAuthorization()`, no rate limit. Flow:
validate (400 via `Results.Problem`, as elsewhere) → owner = `sub` claim parsed as Guid (the
`ClaimTypes.NameIdentifier` / `"sub"` fallback `/me` uses) → derive name → insert → **201** with
`SavedRouteResponseDto`. On unique violation → **200** with the existing row's DTO (see Critical
Implementation Details). On an `NpgsqlException` / DB unavailability → **503** problem with a
generic detail — no connection-string fragment, host, or credential ever reaches the body or a log
line.

#### 5. Opt-in Postgres test plumbing

**File**: `api/RideForgeApi.Tests/PostgresFactAttribute.cs`, `api/RideForgeApi.Tests/PostgresApiFactory.cs`

**Intent**: Real-database tests that run when a test database is configured and skip cleanly
otherwise.

**Contract**: `PostgresFactAttribute : FactAttribute` sets `Skip` when `RIDEFORGE_TEST_DB` is unset
(and a `PostgresTheoryAttribute` if a theory needs it). `PostgresApiFactory : AuthenticatedApiFactory`
overrides `ConnectionStrings:RideForge` with the env var and, once per fixture, applies migrations
(`Database.MigrateAsync()`) — which is itself the proof that the migration applies to real
Postgres — then deletes rows for the test owner ids it uses. Tests use unique owner and client
route ids so they never depend on execution order.

#### 6. Hermetic endpoint tests

**File**: `api/RideForgeApi.Tests/SavedRoutesEndpointTests.cs`

**Intent**: Pin the boundary behaviour that needs no database.

**Contract**: No token → 401. Each invalid-payload case → 400, as one `[Theory]` with one row per
rule (missing/empty client id, <2 points, >20,000 points, out-of-range point, out-of-range start,
distance ≤0 or >500 km, non-positive stats, label >200 chars). Valid token + valid payload against an
unreachable database (`Host=127.0.0.1;Port=1`) → 503, and the response body contains neither the
host nor the word `Password`.

#### 7. Postgres rule tests

**File**: `api/RideForgeApi.Tests/SavedRoutesPersistenceTests.cs`

**Intent**: Pin the rules a stub would lie about.

**Contract**, each `[PostgresFact]`:
- First save → 201; the stored row's `owner_id` equals the token's `sub`; geometry reads back with
  the same points in the same order and `lat`/`lng` not swapped; name matches the naming rule.
- Same rider, same `clientRouteId`, second save → 200, same `id`, exactly one row.
- Same `clientRouteId`, **different rider** → 201 with a different `id`; the first rider's row is
  unchanged (per-owner uniqueness — the IDOR guard).
- A body carrying an extra `ownerId` naming another rider → the row is owned by the token's `sub`.
- A repeat save carrying a different payload → returns the original row, unchanged (first write
  wins).

#### 8. Naming unit tests

**File**: `api/RideForgeApi.Tests/SavedRouteNamingTests.cs`

**Intent**: Pin the naming rule decided in this plan.

**Contract**: Short label; label over 40 characters truncated with an ellipsis; blank and null
label fallback; km rounding (e.g. 41,600 m → `42 km`).

#### 9. Cookbook note

**File**: `context/foundation/test-plan.md` (§6)

**Intent**: Record how to add a real-database test, since this is the first one.

**Contract**: A short §6 subsection: `[PostgresFact]` + `PostgresApiFactory`, the
`RIDEFORGE_TEST_DB` variable, a local Postgres as the target, unique ids per test, and that the gate
is ad hoc (skipped when unset). No change to §1–§5.

### Success Criteria:

#### Automated Verification:

- Hermetic suite passes with no database configured: `dotnet test api/RideForgeApi.slnx`
- Postgres suite passes against a local database: `RIDEFORGE_TEST_DB=<local> dotnet test api/RideForgeApi.slnx`
- Postgres tests report as skipped, not passed, when `RIDEFORGE_TEST_DB` is unset
- Per-owner uniqueness, idempotent repeat, owner-from-token, and geometry round-trip cases present and passing

#### Manual Verification:

- Deployed `POST /saved-routes` without a token → 401
- Deployed `POST /saved-routes` with a real token from the dev build → 201; the row appears in Supabase under that user's id
- Replaying the same request → 200 with the same `id`, still one row
- (Optional) Stryker on `SavedRouteValidation.cs` / `SavedRouteNaming.cs`; survived mutants triaged, not chased

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: Save from the app (client)

### Overview

Carry the generation inputs and a client route id to the result screen, and give both result screens
a Save action with signed-out, pending, saved, and error states.

### Changes Required:

#### 1. Shared UUID helper

**File**: `src/lib/uuid.ts`, `src/lib/install-id.ts`

**Intent**: One lazily loaded UUID source for both the install id and the route id.

**Contract**: Move `randomUUID()` (lazy `expo-crypto` require + `Math.random` fallback) into
`uuid.ts` as an export; `install-id.ts` imports it. Keep the lesson-referencing comment with the
function. The fallback's weaker randomness is acceptable for the route id too: it is scoped per
owner by the server and is not a secret.

#### 2. Result store carries the whole ride

**File**: `src/lib/route-result-store.ts`

**Intent**: Keep what saving needs alongside the route, and remember who saved it.

**Contract**: The stored value becomes `{ clientRouteId, request: GenerateRequest, startLabel:
string | null, route: GeneratedRoute, savedBy: string | null }`. `setLastRoute` takes the ride
(minting `clientRouteId` when the route is set); add `markLastRouteSaved(userId)`. Existing readers
move to the new shape.

#### 3. Generate mutation passes the inputs through

**File**: `src/hooks/use-generate-route-mutation.ts`, `src/app/(tabs)/index.tsx`

**Intent**: Get the request and the start label into the store without sending the label to
`/route/generate`.

**Contract**: Mutation variables become `{ request: GenerateRequest; startLabel: string }`;
`mutationFn` sends only `request`; `onSuccess` stores the ride. The Plan screen passes the trimmed
`origin` text as `startLabel`.

#### 4. Save API

**File**: `src/api/saved-routes.ts`, `src/api/index.ts`

**Intent**: Type the endpoint.

**Contract**: `SaveRouteRequest` (mirrors the backend DTO) and `SavedRoute = { id: string; name:
string; createdAt: string }`. `saveRoute(req)` calls `request<SavedRoute>('/saved-routes', {
method: 'POST', body: req, auth: true, timeoutMs: SAVE_TIMEOUT_MS })` with a 15s timeout, and
guards the response shape into `ApiError('parse')` like `generateRoute` does. Both 200 and 201 are
success. Re-exported from the barrel.

#### 5. Save mutation

**File**: `src/hooks/use-save-route-mutation.ts`

**Intent**: Drive the save from React Query like generation.

**Contract**: `useMutation<SavedRoute, ApiError, SaveRouteRequest>`; on success calls
`markLastRouteSaved` with the current user id.

#### 6. Save action component

**File**: `src/components/save-route-action.tsx`

**Intent**: One Save control used by both result screens.

**Contract**: Reads `useSession()` and the store snapshot. States: session restoring → nothing;
signed out → **Sign in to save** navigating to `/account`; signed in and `savedBy === user.id` →
**Saved ✓** (disabled); pending → **Saving…**; idle → **Save route**; error → mapped message inline
plus retry. `saveErrorMessage(ApiError)` follows the `planErrorMessage` shape: 401 → session expired,
sign in again; 400 → this route can't be saved; 503/5xx → couldn't save right now, try again;
`network` / `timeout` / `parse` → their usual copy. Uses `ThemedText`/`ThemedView`, `Spacing`, the
existing primary/secondary button styles.

#### 7. Result screens

**File**: `src/app/result.tsx`, `src/app/result.web.tsx`

**Intent**: Place the action and fix the stale empty-state copy.

**Contract**: Native — the action sits with `RideStats` in the bottom overlay, inside the safe-area
inset. Web — the action below `RideStats`. Both screens read `route` from the new store shape.
Empty state drops "Generated routes aren't saved yet" in favour of copy that stays true (e.g. "Plan
a ride to see it drawn here.").

#### 8. Account tab: way back to the route

**File**: `src/app/(tabs)/account.tsx`

**Intent**: Close the "Sign in to save" loop.

**Contract**: When signed in and the store holds a route whose `savedBy` is not the current user,
render a **Back to your route** link to `/result`. Nothing is shown otherwise.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npx tsc --noEmit`
- Linting passes: `npm run lint`

#### Manual Verification:

- Signed in: generate → **Save route** → **Saving…** → **Saved ✓**; the row appears in Supabase with the typed start label and a sensible name
- Double-tapping Save quickly leaves exactly one row
- Airplane mode during save shows the network message and a working retry once back online
- Signed out: result shows **Sign in to save** → Account → sign in → **Back to your route** → save succeeds
- Rider A saves, signs out, rider B signs in and returns to the route → **Save route**, not **Saved ✓**; saving creates B's own row
- Web build: result screen shows the action and saving works
- Anonymous generation and the quota message are unchanged
- No new native module — Metro reload on the existing dev build is enough

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

Oracles come from this plan's decisions and the PRD, not from the implementation: FR-009 (signed-in
only → 401), the ownership rule (owner = verified `sub`), the idempotency and per-owner uniqueness
decisions, the payload bounds, and the naming rule.

### Hermetic (always run):

- 401 without a token
- 400 per validation rule, one parameterised theory — each row catches a different regression
- 503 on an unreachable database, with no connection detail in the body
- Naming rule: truncation, fallback, rounding
- Existing regression: anonymous `POST /route/generate` still 200

### Integration (real Postgres, opt-in via `RIDEFORGE_TEST_DB`):

- Migration applies to an empty database (fixture setup)
- Owner stamped from the token; body-supplied owner ignored
- Idempotent repeat → same id, one row; different payload → original row unchanged
- Same client id, different rider → separate rows (IDOR guard on the write path)
- Geometry round-trips in order with axes intact

A stub would lie about every integration case — they are all constraint or storage behaviour.

### Manual testing steps:

1. Signed in on the dev build: generate, save, confirm **Saved ✓** and the Supabase row
2. Double-tap Save → one row
3. Signed out: Sign in to save → Account → sign in → Back to your route → save
4. Two accounts on one device: B never sees A's Saved state
5. Airplane mode mid-save → message + retry
6. Web build: save from the web result screen
7. Anon-key Data API request against the table → no data

## Performance Considerations

A save is one insert of a row whose `jsonb` geometry is tens to a few hundred KB at the 20,000-point
ceiling; far below Kestrel's default 30 MB body limit and irrelevant to the 30s generation budget,
since it is a separate request. The session pooler adds one hop; the free-tier pooler connection
limit is not a concern for a single API instance with EF's default pooling.

## Migration Notes

First migration on an empty schema — no existing data. Rollback: revert the code and redeploy; the
`rideforge` schema can stay (unused) or be dropped with `dotnet dotnet-ef database update 0`. Keep the
Railway variable until the revert is deployed — the reverted code ignores it, while the reverse
order would crash-loop the current code.

## Open Risks & Assumptions

- **Scripted saving could fill free-tier storage.** No per-rider cap or rate limit (decision).
  Bounded per request by the 20,000-point ceiling only.
- **Orphaned rows on user deletion.** No FK to `auth.users`; deleting a user in the Supabase
  dashboard leaves their rows. Account deletion is not a feature yet — revisit when it is.
- **The Postgres suite can rot unnoticed.** It skips silently without `RIDEFORGE_TEST_DB`. Mitigated
  by the phase 2 success criterion and the §6 cookbook note; a CI Postgres service would close it
  (§3 Phase 4 territory).
- **Client-supplied geometry is trusted.** The server stores whatever valid geometry the rider sends.
  Harmless — it only affects the sender's own account — but a saved route is not proof the API
  generated it.
- **Manual migrations can be forgotten.** Deploying code that needs a newer schema than Supabase has
  surfaces as 500s on save. Phase 1's ordering and the plan's Migration Notes are the only guard.
- **The route store is process-lived.** A rider who kills the app between "Sign in to save" and
  returning loses the unsaved route. Accepted; the route was never persisted.
- **Session-pooler SSL specifics are unverified from Railway.** If `SSL Mode=Require` rejects the
  certificate chain, adjust the Npgsql SSL settings rather than disabling TLS.

## References

- Roadmap slice: `context/foundation/roadmap.md` → `### S-06: Zapis wygenerowanej trasy`
- PRD: `context/foundation/prd.md` → FR-009, Access Control
- Auth boundary this builds on: `context/archive/2026-09-08-rider-auth/plan.md`, `api/Program.cs` (`/me`)
- Test harness: `api/RideForgeApi.Tests/RideForgeApiFactory.cs` (`AuthenticatedApiFactory`)
- Validation pattern: `api/Routing/RouteValidation.cs`
- Client patterns: `src/api/route.ts` (shape guard), `src/app/(tabs)/index.tsx` (`planErrorMessage`), `src/lib/install-id.ts` (lazy native require)
- IDOR note: `context/foundation/test-plan.md` §8
- Native-module lesson: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Persistence foundation (backend)

#### Automated

- [x] 1.1 Backend builds: `dotnet build api/RideForgeApi.slnx`
- [x] 1.2 Existing suite still passes with the placeholder connection string
- [x] 1.3 Migration generates and the tool resolves via the manifest
- [ ] 1.4 Migration applies cleanly to a local Postgres

#### Manual

- [ ] 1.5 Migration applied to Supabase; table in `rideforge` with RLS on, nothing in `public`
- [ ] 1.6 Anon-key Data API request cannot read the table
- [ ] 1.7 `rideforge_api` can select from the table (0 rows, no permission error)
- [ ] 1.8 Railway deploy boots; `/health` and anonymous generation still work
- [ ] 1.9 Local startup with the connection string unset fails loudly

### Phase 2: `POST /saved-routes` + tests (backend)

#### Automated

- [ ] 2.1 Hermetic suite passes with no database configured
- [ ] 2.2 Postgres suite passes against a local database
- [ ] 2.3 Postgres tests report as skipped when `RIDEFORGE_TEST_DB` is unset
- [ ] 2.4 Per-owner uniqueness, idempotent repeat, owner-from-token, and geometry round-trip cases present and passing

#### Manual

- [ ] 2.5 Deployed `POST /saved-routes` without a token → 401
- [ ] 2.6 Deployed save with a real token → 201 and the row in Supabase
- [ ] 2.7 Replaying the same request → 200, same `id`, one row
- [ ] 2.8 (Optional) Stryker on validation/naming; survived mutants triaged

### Phase 3: Save from the app (client)

#### Automated

- [ ] 3.1 Type checking passes: `npx tsc --noEmit`
- [ ] 3.2 Linting passes: `npm run lint`

#### Manual

- [ ] 3.3 Signed in: Save → Saving… → Saved ✓; row in Supabase with label and name
- [ ] 3.4 Double-tap Save leaves one row
- [ ] 3.5 Airplane mode shows the network message and retry works
- [ ] 3.6 Signed out: Sign in to save → Account → Back to your route → save succeeds
- [ ] 3.7 Rider B never sees rider A's Saved ✓; B's save creates B's own row
- [ ] 3.8 Web build: save works from the web result screen
- [ ] 3.9 Anonymous generation and the quota message unchanged
- [ ] 3.10 Metro reload on the existing dev build is enough (no new native module)
