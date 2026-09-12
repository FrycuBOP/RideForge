# S-07 — Saved routes list Implementation Plan

## Overview

A signed-in rider opens their saved routes from the Account tab, sees them newest-first, and taps
one to see it drawn on the map again (FR-010). This closes the PRD's secondary success criterion —
"logged-in riders can save generated routes and revisit past rides from their account" — whose first
half S-06 delivered.

It is also the slice that discharges the ownership guard S-06 deferred. That slice's implementation
review accepted finding F7 rather than fixing it, on the explicit grounds that S-07 owns the
mitigation: the RLS policy on `rideforge.saved_routes` is `FOR ALL TO rideforge_api USING (true)`, an
exposure guard that keeps the table away from the anon key and scopes nothing per rider. The owner
filter in this slice's queries is the only thing standing between riders.

## Current State Analysis

**The persistence layer is ready and needs no migration.** `rideforge.saved_routes` already stores
everything a list and a revisit need — `name`, `start_lat`/`start_lng`, `start_label`,
`requested_distance_km`, `distance_meters`, `duration_seconds`, `created_at`, and `geometry` as one
`jsonb` column ([RideForgeDbContext.cs:29](api/Persistence/RideForgeDbContext.cs:29)). The runtime
role holds `GRANT SELECT, INSERT`
([migration:79](api/Persistence/Migrations/20260911173501_InitialSavedRoutes.cs:79)), so reading
needs no new grant. It deliberately holds no `UPDATE` or `DELETE`.

**A row is heavy.** A full-size geometry (20,000 points, `SavedRouteValidation.MaxGeometryPoints`)
serializes to roughly 600 KB. The save endpoint caps request bodies at 2 MB for exactly that reason
([Program.cs:243](api/Program.cs:243)). Nothing in the API currently produces an unbounded response.

**The write path establishes every convention this slice follows.** `POST /saved-routes`
([Program.cs:318](api/Program.cs:318)) takes the owner from `SubjectOf(user)` and never the body,
answers 401 when a validly signed token's `sub` is not a UUID, wraps database faults as 503 through
`IsDatabaseFailure`/`LogDatabaseFailure`, and keeps wire DTOs (`api/SavedRoutes/SavedRouteModels.cs`)
apart from the storage entity.

**The revisit target exists but cannot represent a saved route.** `/result` snapshots
`getLastRoute()` once on mount ([result.tsx:49](src/app/result.tsx:49)) and the store is a single
module-level `lastRide` ([route-result-store.ts:38](src/lib/route-result-store.ts:38)) holding "the
ride just generated", complete with a `savedBy` field and a `SaveRouteAction`. It is session-only by
design and is lost on reload.

**The map lives in one screen, with a web split that is not optional.** `MapView`, `Polyline`,
`boundingRegion` and the `onLayout` fit are all inside `result.tsx`; `result.web.tsx` exists because
react-native-maps calls `codegenNativeComponent`, which react-native-web does not implement, so
importing it into the web bundle crashes the app.

**The query cache is keyed by endpoint, not by rider.** `useMeQuery` uses `queryKey: ['me']`, which
is why `SessionProvider` has to hand-drop that key on `SIGNED_OUT`
([session-provider.tsx:57](src/components/session-provider.tsx:57)) or the next rider on the device
sees the previous rider's identity.

**Executable test coverage is constrained in a way that shapes this plan.** test-plan §7 deliberately
excludes `src/app/` and `src/components/` from automated testing and no frontend runner exists, so
every executable test here is backend xUnit. The real-Postgres suite is **permanently skipped** — the
S-06 epilogue closed rows 1.4 / 2.2 / 2.4 as won't-do because there is no local Postgres and will not
be one — and no CI exists at all (`.github/workflows` is absent; test-plan §5 assigns CI YAML to §3
Phase 4). Anything that must actually run on every `dotnet test` has to work without a database.

## Desired End State

A signed-in rider taps **Your saved routes** on the Account tab and lands on a screen listing their
rides newest-first, each row showing the saved name, distance, duration and when it was saved.
Tapping a row opens that ride drawn on the map with its stats, fetched by id. A rider with no saved
routes sees an empty state that points them back to Plan; a failure shows a retryable error, with an
expired session branched to its own copy as elsewhere in the app. A rider signing in on a device
another rider used sees their own list and never a cached trace of the previous one.

Verification: `dotnet test` is green with the hermetic ownership, ordering, cap and wire-contract
tests passing (not skipped); `GET /saved-routes` with rider A's token never returns a row owned by
rider B, and `GET /saved-routes/{B's route id}` as A answers 404; the list response never carries
geometry; and the deployed app walks save → list → open → back on a real device.

### Key Discoveries:

- `GRANT SELECT` is already in place — this slice adds **no migration**
  ([migration:79](api/Persistence/Migrations/20260911173501_InitialSavedRoutes.cs:79))
- The RLS policy is `USING (true)` and scopes nothing per rider — the query's owner filter is the
  whole ownership guard (S-06 review finding F7, accepted and assigned here)
- `SubjectOf(user)` + `Guid.TryParse` → 401 is the established owner-identification contract
  ([Program.cs:345](api/Program.cs:345))
- `IsDatabaseFailure` → sanitized log → 503 is the established database-fault contract
  ([Program.cs:384](api/Program.cs:384))
- `SuccessWireContract_IsTheOneTheClientConsumes` (added as S-06 review fix F1) is the pattern for
  pinning a response shape through the host's own `JsonOptions` without a database
- `PostgresApiFactory.NewRider()` hands out per-test rider ids and cleans their rows up
  ([PostgresApiFactory.cs:36](api/RideForgeApi.Tests/PostgresApiFactory.cs:36)) — but the suite is
  skipped in practice
- Typed routes are enabled (`app.json` `typedRoutes: true`), so the detail screen reads its
  parameter as `useLocalSearchParams<'/saved-routes/[id]'>()` (verified against the Expo SDK 56 docs)
- `react-native-maps` is already a dependency at 1.27.2 — **no new native module, so no new EAS dev
  build**; everything in this slice lands with a Metro reload

## What We're NOT Doing

- **No delete and no rename.** FR-010 is "view a list". Staying read-only means no new migration, no
  `DELETE`/`UPDATE` grant, and no write path on a surface whose ownership guard is itself new here.
  The consequence is accepted: a rider's list only grows, and a route saved by mistake is permanent.
- **No paging.** Newest-first with a server-side cap. The response is an envelope rather than a bare
  array specifically so a cursor can be added later without breaking a client parse guard.
- **No GPX export from a saved route.** That needs S-04 (`gpx-download`), which is still `proposed`.
- **No search, filter, or sort controls.**
- **No offline persistence of the list.** The query cache is in-memory, as everywhere else in the app.
- **No frontend test runner.** test-plan §7 excludes `src/app/` and `src/components/` deliberately;
  introducing a runner is not this slice's decision to make.
- **No CI pipeline.** test-plan §5 assigns CI YAML to §3 Phase 4 of the test rollout. This slice does
  not create `.github/workflows`, and therefore does not un-skip the Postgres suite.
- **No change to the RLS policy.** Widening it to a per-rider policy would require the API to run
  under a per-request Postgres role, which the session-pooler connection model does not support.
- **No change to `/result` or the result store's role.** The revisit screen is a separate route with
  its own fetch; the store stays "the ride just generated".

## Implementation Approach

Two owner-scoped read endpoints, with the ownership rule factored out of the endpoint so it can be
executed by a test that needs no database.

The filter, ordering, cap and projection live in a pure query-shaping helper over
`IQueryable<SavedRoute>`. Against EF it composes into SQL; against a `List<SavedRoute>.AsQueryable()`
it runs as LINQ-to-objects, so hermetic tests execute the *real* filter expression rather than a
stub's imitation of it. `ToQueryString()` — which builds SQL without opening a connection, so it
works under the hermetic factory's unroutable connection string — covers the two claims the in-memory
run cannot make: that the cap and the owner predicate are applied server-side and parameterised, and
that the list's SQL never names the `geometry` column.

`GET /saved-routes` returns a capped, newest-first envelope of summaries with no geometry.
`GET /saved-routes/{id:guid}` returns one route including geometry, and answers **404** for a route
that belongs to someone else — the same answer as a route that does not exist, because a 403 would
confirm it does.

On the client, a per-rider query key makes a cross-rider cache bleed structurally impossible rather
than dependent on a cleanup call firing. The save mutation invalidates the list key on success, so
the one event that can make the list wrong is the one that refreshes it. The list is a stack route
pushed from Account (no tab configuration is touched — `NativeTabs` is unstable and the web build
carries a second, hand-written tab list). The revisit screen reuses the map by extracting it from
`result.tsx` into a shared component with its own `.web` sibling.

## Critical Implementation Details

**The list projection must be server-side.** The whole point of the summary shape is that `geometry`
never leaves Postgres. A `Select` of scalars translates to a SELECT naming only those columns; a
materialise-then-map (`ToListAsync()` followed by a C# projection) would read every `jsonb` blob into
the API's memory and produce the exact unbounded read this design exists to avoid. The
`ToQueryString()` test is what keeps a future refactor from silently making that mistake.

**The detail read deliberately does not project.** `Geometry` is mapped as an EF complex collection
with `ToJson` ([RideForgeDbContext.cs:53](api/Persistence/RideForgeDbContext.cs:53)); composing it
inside a `Select` projection risks a translation failure that surfaces only against a real database —
which is to say, never, given the Postgres suite is skipped. The detail path therefore filters
through the shared helper and then loads the tracked-free entity, mapping to its DTO in memory. One
row, bounded by the save-side geometry ceiling.

**Ordering needs a tiebreak.** `created_at` defaults to `now()`, which is transaction-scoped — two
routes saved in the same transaction would share a timestamp. Order by `created_at` descending then
`id` descending so the sequence is deterministic and the cap always keeps the same rows.

## Phase 1: Owner-scoped read endpoints + tests

### Overview

Both read endpoints, with the ownership rule extracted into a helper that hermetic tests can execute,
and the cross-rider read pinned by tests that actually run on every `dotnet test`.

### Changes Required:

#### 1. Wire DTOs for reading

**File**: `api/SavedRoutes/SavedRouteModels.cs`

**Intent**: Add the read-side wire shapes alongside the existing save DTOs, keeping wire and storage
shapes separate as this file already does. The summary omits geometry; the detail carries it.

**Contract**: Three new records plus one constant, all serializing camelCase through the host's
`JsonOptions` like the existing DTOs:
- `SavedRouteSummaryDto(Guid Id, string Name, double DistanceMeters, double DurationSeconds, DateTimeOffset CreatedAt)`
- `SavedRouteListResponseDto(IReadOnlyList<SavedRouteSummaryDto> Items)` — an envelope, not a bare
  array, so a `cursor` field can be added later without breaking the client's parse guard
- `SavedRouteDetailDto(Guid Id, string Name, Coord Start, string? StartLabel, double RequestedDistanceKm, double DistanceMeters, double DurationSeconds, IReadOnlyList<Coord> Geometry, DateTimeOffset CreatedAt)`
- `SavedRouteListLimits.MaxItems = 50` — the server-side cap, a named constant so the tests and the
  endpoint cannot disagree

#### 2. Owner-scoped query shaping

**File**: `api/SavedRoutes/SavedRouteQueries.cs` (new)

**Intent**: Hold the ownership filter, ordering, cap and summary projection as pure functions over
`IQueryable<SavedRoute>` so they compose into SQL against EF and run as LINQ-to-objects against an
in-memory list. This is what makes the IDOR guard testable without a database, and it is the reason
the rule does not live inline in the endpoint.

**Contract**: Two static methods on a static class in the `RideForgeApi.SavedRoutes` namespace:

```csharp
// Newest-first, capped, geometry-free. The Select must stay inside the IQueryable so the
// projection reaches SQL — see "Critical Implementation Details".
public static IQueryable<SavedRouteSummaryDto> SummariesOwnedBy(
    this IQueryable<SavedRoute> routes, Guid ownerId, int limit);

// Filter only. The caller loads the entity and maps it, because projecting the ToJson
// complex collection risks a translation failure no running test would catch.
public static IQueryable<SavedRoute> OwnedRoute(
    this IQueryable<SavedRoute> routes, Guid ownerId, Guid routeId);
```

Both predicates must name `OwnerId` — an id-only filter is the IDOR bug this slice exists to prevent.

#### 3. The two endpoints

**File**: `api/Program.cs`

**Intent**: Map both reads next to `POST /saved-routes`, reusing that endpoint's owner-identification
and database-fault contracts verbatim so all three behave the same way under the same failures.

**Contract**:
- `GET /saved-routes` → `.RequireAuthorization()`; owner from `Guid.TryParse(SubjectOf(user))` else
  401; `SavedRouteListResponseDto` at 200 (an empty `items` array for a rider with none, never 404);
  503 via the existing `IsDatabaseFailure`/`LogDatabaseFailure`/`SaveUnavailable` path. No rate
  limit, matching the save.
- `GET /saved-routes/{id:guid}` → same authorization and owner rule; 200 with
  `SavedRouteDetailDto`; **404** when the id is unknown *or* owned by another rider — one answer for
  both, so the response cannot be used to probe for other riders' route ids. The `:guid` route
  constraint makes a malformed id a 404 before the handler runs.
- Both use `AsNoTracking()`.

Rename the 503 helper if its current name reads as save-only; the mapping itself must not change.

#### 4. Hermetic query-shaping tests

**File**: `api/RideForgeApi.Tests/SavedRouteQueriesTests.cs` (new)

**Intent**: Execute the real filter and ordering expressions against a `List<SavedRoute>.AsQueryable()`
holding two riders' interleaved routes. These are the tests that run on every `dotnet test` and that
fail if the owner predicate is ever dropped.

**Contract**: LINQ-to-objects, no host and no fixture. Cases:
- `SummariesOwnedBy` returns only the caller's rows from an interleaved two-rider set — the list IDOR
  regression
- Results are ordered `CreatedAt` descending, then `Id` descending when timestamps are equal
- At most `MaxItems` rows, and the ones kept are the newest
- A rider with no rows yields an empty sequence
- `OwnedRoute` yields nothing for a route id that exists but belongs to another rider — the detail
  IDOR regression
- `OwnedRoute` yields the row for its own owner

#### 5. Generated-SQL guards

**File**: `api/RideForgeApi.Tests/SavedRouteQuerySqlTests.cs` (new)

**Intent**: Pin the two claims the in-memory run cannot make. Uses `ToQueryString()`, which builds
SQL without opening a connection, so it runs under `RideForgeApiFactory`'s unroutable connection
string.

**Contract**: Assert on the generated SQL for `SummariesOwnedBy`:
- it carries a `WHERE` on `owner_id` bound to a **parameter**, not an inlined literal
- it carries a `LIMIT` — the cap is applied by Postgres, not after the rows arrive
- it does **not** name the `geometry` column — geometry never leaves the database for a list

Assert on substrings and column names, not the whole SQL string, so a legitimate query rewrite does
not fail the test for the wrong reason.

#### 6. Hermetic endpoint tests

**File**: `api/RideForgeApi.Tests/SavedRoutesReadEndpointTests.cs` (new)

**Intent**: Cover the status contract and the wire shape without a database, following
`SavedRoutesEndpointTests` (which points at a dead port so anything reaching the database surfaces as
503 rather than passing).

**Contract**: Via `AuthenticatedApiFactory`. Cases:
- No token → 401 on both endpoints
- Expired / wrong-issuer / wrong-audience token → 401 (one case each, mirroring the save suite)
- Valid token whose `sub` is not a UUID → 401 on both
- `GET /saved-routes/not-a-guid` → 404 from the route constraint
- Unreachable database → 503 on both, with the sanitized-log assertion the save suite already makes
- A wire-contract test in the shape of `SuccessWireContract_IsTheOneTheClientConsumes`: serialize
  both response DTOs through the host's own `JsonOptions` and pin the exact camelCase field names the
  client will consume (`items`, `id`, `name`, `distanceMeters`, `durationSeconds`, `createdAt`,
  `start`, `startLabel`, `requestedDistanceKm`, `geometry`)

#### 7. Real-Postgres row tests

**File**: `api/RideForgeApi.Tests/SavedRoutesPersistenceTests.cs`

**Intent**: Add the end-to-end cross-rider read against a real database, ready for the day one is
pointed at `RIDEFORGE_TEST_DB`. These are expected to report as *skipped* — the hermetic tests above
are what actually guard the rule today.

**Contract**: `[PostgresFact]` with the existing `IClassFixture<PostgresApiFactory>`, each test
minting its own riders via `NewRider()`:
- Rider A saves two routes and rider B one; `GET /saved-routes` as A returns exactly A's two,
  newest-first
- `GET /saved-routes/{B's route id}` as A → 404
- Detail round-trips the geometry byte-for-identical to what was saved (the `jsonb` mapping)

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build api/RideForgeApi.slnx`
- Backend tests pass: `dotnet test api/RideForgeApi.slnx`
- The new query-shaping, SQL-guard and endpoint tests report as **passed, not skipped** (check the
  skipped count: it should rise only by the three new `[PostgresFact]` cases)

#### Manual Verification:

- Deployed, `GET /saved-routes` with a real rider's token returns that rider's saved routes and
  nothing else
- Deployed, `GET /saved-routes/{id}` for a route owned by a different rider answers 404
- The list response body for a rider with several saved routes is a few KB, not megabytes

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: Client data layer + list screen

### Overview

The API functions with response guards, per-rider query hooks, the cache invalidation that keeps the
list honest after a save, and the `/saved-routes` screen with its four states plus the Account entry
point.

### Changes Required:

#### 1. Read functions and types

**File**: `src/api/saved-routes.ts`

**Intent**: Add the two read calls next to `saveRoute`, each with the same response-shape guard the
file already applies — `request` casts without checking, so a 2xx carrying the wrong shape would
otherwise reach the screen and crash it.

**Contract**: `SavedRouteSummary` and `SavedRouteDetail` types mirroring the backend DTOs;
`listSavedRoutes(): Promise<SavedRouteSummary[]>` unwrapping the `items` envelope; and
`getSavedRoute(id: string): Promise<SavedRouteDetail>`. Both `auth: true`. Both throw
`ApiError('parse', …)` when the guard fails, as `saveRoute` and `generateRoute` do. Timeouts: the
list is metadata-only so it gets a short budget; the detail carries up to ~600 KB of geometry, so it
gets the save path's 15 s rather than the 30 s generation default. Re-export from `src/api/index.ts`.

#### 2. Per-rider query hooks

**File**: `src/hooks/use-saved-routes-query.ts` (new), `src/hooks/use-saved-route-query.ts` (new)

**Intent**: Query the list and one route, keyed by the signed-in rider so a cross-rider cache bleed
is structurally impossible rather than dependent on a cleanup call firing. Follows `useMeQuery`'s
template, including `enabled` so a signed-out rider never fires a request that can only come back
401 — but keyed by rider, which `['me']` is not.

**Contract**: `queryKey: ['saved-routes', userId]` and `['saved-routes', userId, routeId]`;
`enabled: session !== null`. The rider id comes from `useSession()`.

#### 3. Invalidate the list when a save lands

**File**: `src/hooks/use-save-route-mutation.ts`

**Intent**: A rider who saves a route and immediately opens the list must see it there. The save is
the only event that can make the list wrong, so it is the event that refreshes it.

**Contract**: In `onSuccess`, alongside the existing `markLastRouteSaved`, invalidate
`['saved-routes', userId]`. Use the `userId` already captured in `onMutate` — not a value read at
success time — for the same reason that callback exists: a session change mid-flight must not have
the next rider's cache invalidated in place of the one whose token went out.

#### 4. Drop the list cache on sign-out

**File**: `src/components/session-provider.tsx`

**Intent**: Belt and braces behind the per-rider key. Nothing should outlive a sign-out even if a
future refactor loosens the key.

**Contract**: Extend the existing `SIGNED_OUT` handler to also remove the `['saved-routes']` key
prefix.

#### 5. The list screen

**File**: `src/app/saved-routes/index.tsx` (new)

**Intent**: Render the rider's saved routes newest-first, with every non-happy state handled. Reuses
the error-mapping shape `account.tsx` and `save-route-action.tsx` already established so copy and
behaviour stay consistent.

**Contract**: Default-export screen, `StyleSheet.create` at the bottom, `Spacing` tokens and
`useTheme()` per repo convention. Four states:
- **restoring / loading** — spinner, and nothing else rendered while `isRestoring`, so a signed-in
  rider never sees the signed-out branch flash on a cold start
- **signed out** — a prompt with a link to `/account` (reachable by deep link even though the entry
  point is signed-in only)
- **empty** — copy naming what is missing, plus a link back to `/` to plan a ride
- **error** — the message card with a retry, and a 401 branched to its own copy with a link to
  `/account`, as `saveErrorMessage` does

Rows show name, distance and duration (via the existing `formatDistanceKm` / `formatDuration`) and
the saved date, and link to `/saved-routes/[id]`. Use `FlatList` so a long list is not all mounted at
once. No `useMemo`/`useCallback` — `reactCompiler` is enabled.

#### 6. A date formatter

**File**: `src/lib/format-ride.ts`

**Intent**: The list is the first place a saved timestamp is shown to a rider; formatting belongs
next to the existing distance and duration formatters so every screen reads it the same way.

**Contract**: A function taking the ISO `createdAt` string and returning rider-facing text. Must not
throw on an unparseable value — the whole list would blank out over one bad row.

#### 7. The entry point

**File**: `src/app/(tabs)/account.tsx`

**Intent**: Give the signed-in rider the way in, in the signed-in branch next to the existing "Back
to your route" link.

**Contract**: A `Link` to `/saved-routes`, rendered only in the signed-in branch. The signed-out copy
already mentions saving routes and needs no change.

#### 8. Register the routes

**File**: `src/app/_layout.tsx`

**Intent**: Give the new screens titles in the root `Stack`, as `result` has.

**Contract**: `<Stack.Screen>` entries for `saved-routes/index` and `saved-routes/[id]` with titles.
The `saved-routes` directory intentionally has no `_layout.tsx` — its screens are pushed over the
tabs by the root stack, like `/result`.

### Success Criteria:

#### Automated Verification:

- Typecheck passes: `npx tsc --noEmit`
- Lint passes: `npm run lint`
- Backend tests still pass: `dotnet test api/RideForgeApi.slnx`

#### Manual Verification:

- Signed in with saved routes: Account → Your saved routes shows them newest-first with correct
  distance, duration and date
- Signed in with none: the empty state appears and its link returns to Plan
- Save a route, then open the list: the new route is there immediately, not after a delay
- Kill the network, open the list: the error card appears and its retry works once the network is back
- Sign out, sign in as a second rider on the same device: the list shows only the second rider's
  routes, with no flash of the first rider's
- Same on web (`npm run web`) as on native

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: Revisit screen

### Overview

Extract the map out of `result.tsx` into a shared component with its own `.web` sibling, then build
`/saved-routes/[id]` on top of it, fed by its own fetch rather than the result store.

### Changes Required:

#### 1. Extract the route map

**File**: `src/components/route-map.tsx` (new), `src/components/route-map.web.tsx` (new)

**Intent**: The map, polyline, start marker, `boundingRegion` fallback and `onLayout` fit are the
parts both the result screen and the revisit screen need. Move them out of `result.tsx` unchanged in
behaviour so the working screen keeps working, and add the `.web` sibling the web bundle requires —
react-native-maps calls `codegenNativeComponent`, which react-native-web does not implement.

**Contract**: Named export taking the geometry (`GeoPoint[]`) and the bottom fit padding, which
differs between the two screens: the result screen's overlay carries the stats card *and* the Save
action (its current `FIT_EDGE_PADDING.bottom` is 280), the revisit screen's carries only stats. The
web sibling renders the "map preview is available in the mobile app" fallback that `result.web.tsx`
shows today. No behaviour change to the native map: the Android `fitToCoordinates`-on-mount crash
that `onLayout` works around must stay worked around.

#### 2. Result screen uses the extracted map

**File**: `src/app/result.tsx`, `src/app/result.web.tsx`

**Intent**: Consume the shared component so there is one map implementation, not two that drift.

**Contract**: Same rendering as today, including the current fit padding. The result store, the
snapshot-on-mount behaviour and `SaveRouteAction` are untouched.

#### 3. The revisit screen

**File**: `src/app/saved-routes/[id].tsx` (new), `src/app/saved-routes/[id].web.tsx` (new)

**Intent**: Show one saved ride drawn on the map with its stats. It owns its data — a route fetched
by id, not the in-memory store — so it is deep-linkable, survives a reload, and cannot overwrite the
ride the rider may be mid-save on.

**Contract**: `useLocalSearchParams<'/saved-routes/[id]'>()` for the id (typed routes are enabled),
feeding `useSavedRouteQuery`. Renders the shared route map plus `RideStats`, and the saved name as
the screen title. **No `SaveRouteAction`** — this route is already saved. States: spinner while
loading; a 404 branched to its own copy ("this route is no longer available") distinct from a
transient failure with a retry; 401 branched to `/account` as elsewhere. The web sibling shows stats
and the same fallback copy as `result.web.tsx`.

### Success Criteria:

#### Automated Verification:

- Typecheck passes: `npx tsc --noEmit`
- Lint passes: `npm run lint`

#### Manual Verification:

- Generating a route and viewing `/result` behaves exactly as before the extraction, including the
  map fit not hiding the loop behind the overlay
- Tapping a row in the list opens that route drawn correctly on the map with its saved stats
- The revisit screen shows no Save action
- Reloading (or deep-linking) `/saved-routes/{id}` loads the route rather than showing an empty state
- Opening a route id that does not exist shows the not-available copy, not a crash or a spinner
- Generating a new route while a revisit screen is open does not change what the revisit screen shows
- Both screens render on web

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 4: Endpoint declarations out of `Program.cs`

### Overview

`Program.cs` is ~680 lines and holds six endpoint declarations, the service configuration for five
subsystems, and eight top-level helper functions shared between them. This slice added two more
endpoints to that pile; the file is now the place you have to scroll through to find anything. Move
the endpoint declarations into route-group extension classes next to the feature they belong to, and
the helpers they share into named static classes, leaving `Program.cs` as composition: configure
services, build the pipeline, map the groups, run.

Behaviour must not change — not one status code, not one log line, not one JSON field. The suite is
the proof: **no test file may be edited in this phase.** If a test needs changing, the refactor
changed behaviour and that is a mismatch to stop on, not to accommodate.

Placed after the feature phases deliberately. Refactoring the endpoints while the client is still
being built against them would put a moving structure under Phase 2 and 3, and the argument for the
move is readability, which does not expire.

### Changes Required:

#### 1. Shared rider identity

**File**: `api/Auth/RiderIdentity.cs` (new)

**Intent**: `SubjectOf` is the one rule the whole API uses to answer "who is calling", and it is
currently a top-level local function that only `Program.cs` can reach. Both `/me` and all three
saved-route endpoints need it, so it cannot stay there once they leave.

**Contract**: `public static class RiderIdentity` with `SubjectOf(ClaimsPrincipal)` moved verbatim,
including the comment explaining why both claim names are read. Consider adding
`TryGetRiderId(ClaimsPrincipal, out Guid)` wrapping the `Guid.TryParse(SubjectOf(user))` that three
handlers now repeat — one place for the rule that a non-UUID subject identifies nobody.

#### 2. Shared database-failure handling

**File**: `api/Persistence/DatabaseFailures.cs` (new)

**Intent**: `IsDatabaseFailure`, `ExceptionChain`, `LogDatabaseFailure`, `MayLogServerMessage` and
`DatabaseUnavailable` are one cohesive rule — what counts as a database fault, what may be said about
it in a log, and what the caller is told — split across five top-level functions. They travel
together and they are what keeps connection details out of the logs, so they deserve a name.

**Contract**: `public static class DatabaseFailures` holding all five, moved verbatim. The sanitizing
reasoning in the XML docs moves with them unchanged — that commentary is the reason the code looks
the way it does, and `SavedRoutesEndpointTests` asserts on its behaviour.

#### 3. Saved-routes endpoints

**File**: `api/SavedRoutes/SavedRoutesEndpoints.cs` (new)

**Intent**: The three saved-route endpoints plus the operation/detail constants and `IsRepeatSave`,
which only they use.

**Contract**: `public static class SavedRoutesEndpoints` with
`public static IEndpointRouteBuilder MapSavedRoutes(this IEndpointRouteBuilder app)`. Keep the
`.RequireAuthorization()` on each endpoint rather than hoisting it to a group — a group-level
default is one edit away from silently covering a future endpoint that should not have it, and the
per-endpoint call is what `SavedRoutesReadEndpointTests` is really asserting. `SaveOperation`,
`ReadOperation`, `SaveFailedDetail`, `ReadFailedDetail` and `IsRepeatSave` become private members of
this class. Every explanatory comment moves with its endpoint.

#### 4. Routing endpoints

**File**: `api/Routing/RouteEndpoints.cs` (new)

**Intent**: `POST /route/stitch` and `POST /route/generate`, which share a failure→status mapping and
a rate-limiting policy.

**Contract**: `public static class RouteEndpoints` with `MapRouteEndpoints(this IEndpointRouteBuilder)`.
The `RouteStitchException.Kind` → status switch is duplicated across both handlers today; collapsing
it into one private helper is in scope, because it is the same mapping and a divergence between the
two would be a silent contract break. `.RequireRateLimiting(GenerationQuotaPolicy)` stays on each
endpoint, so the policy name has to be reachable — move it to a const on `GenerationQuotaOptions`
rather than passing it in.

#### 5. What stays in `Program.cs`

**File**: `api/Program.cs`

**Intent**: Composition only, so the file reads as a table of contents for the service.

**Contract**: Service registration (CORS, stitching provider resolution, Supabase auth, DbContext,
rate limiter), the pipeline (`UseForwardedHeaders` → CORS → body-size middleware → auth → rate
limiter), `/health`, `/me`, and the three `Map*` calls. `SaveRequestSizeLimitBytes` and the body-size
middleware stay here: it is pipeline configuration keyed on a path, not an endpoint declaration.
`AnonymousPartitionKey` stays with the rate-limiter configuration it serves. `PendingMigrationsCheck`
stays as-is. `Program` must remain the entry-point type — `WebApplicationFactory<Program>` in every
test fixture depends on it.

### Success Criteria:

#### Automated Verification:

- Backend builds with no new warnings: `dotnet build api/RideForgeApi.slnx`
- Full backend suite green: `dotnet test api/RideForgeApi.slnx` — same pass and skip counts as before
  the refactor (130 passed / 9 skipped at the end of Phase 1, plus nothing this phase adds)
- `git diff --stat api/RideForgeApi.Tests/` is empty: no test file was edited. A refactor that needs a
  test changed is not a refactor.

#### Manual Verification:

- `Program.cs` reads end-to-end as configure → pipeline → map → run, with no endpoint body in it
- Every comment that explained *why* an endpoint behaves as it does is still attached to that endpoint

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 5: Cookbook + close-out

### Overview

Record the owner-scoped read test pattern where the next contributor will look for it, and close the
S-06 finding this slice was assigned.

### Changes Required:

#### 1. Cookbook entry for owner-scoped reads

**File**: `context/foundation/test-plan.md`

**Intent**: §6.1.1 tells a contributor how to write a test that needs a real database. Nothing tells
them how to test an ownership rule when the real-database suite is skipped — which is now the
project's normal case. Write that down while the reasoning is fresh.

**Contract**: A new sub-section under §6 covering: extract the filter into a query-shaping function
over `IQueryable<T>`; test ownership and ordering with LINQ-to-objects over `AsQueryable()`; use
`ToQueryString()` for the claims that need generated SQL (server-side predicate, cap, and columns
*not* selected); keep the real-database version as `[PostgresFact]` knowing it is skipped. Reference
tests: `SavedRouteQueriesTests.cs` and `SavedRouteQuerySqlTests.cs`. Also note in §8 that the IDOR
surface the ledger flagged as "not yet in this map" now has executable hermetic coverage.

#### 2. Close out the change record

**File**: `context/changes/saved-routes-list/change.md`, `context/foundation/roadmap.md`

**Intent**: Record the decisions taken during implementation and reflect the slice's state where the
roadmap sync reads it.

**Contract**: `## Notes` entries for anything decided while implementing that this plan did not
specify — in the shape S-06's change.md uses. Explicitly record that S-06 review finding **F7** is
discharged: the `GET` endpoints filter by owner in the query, and the ownership rule is pinned by
tests that run without a database. Roadmap S-07 status moves to `done` via `/10x-archive`.

### Success Criteria:

#### Automated Verification:

- Full suite green: `dotnet test api/RideForgeApi.slnx`, `npm run lint`, `npx tsc --noEmit`

#### Manual Verification:

- test-plan §6 reads as something a contributor could follow without this plan open
- Nothing in `change.md` contradicts what actually shipped

---

## Testing Strategy

### Unit Tests:

- `SavedRouteQueriesTests` — ownership filter (both endpoints), newest-first ordering with the `Id`
  tiebreak, the `MaxItems` cap keeping the newest, and the empty case. LINQ-to-objects, no database.
- `SavedRouteQuerySqlTests` — the owner predicate is parameterised, the cap is a `LIMIT`, and the
  list SQL never names `geometry`. `ToQueryString()`, no connection.
- Date formatting must not throw on an unparseable timestamp (verified by hand; §7 excludes frontend
  automated tests).

### Integration Tests:

- `SavedRoutesReadEndpointTests` — the 401 family, the `:guid` constraint's 404, the 503 on an
  unreachable database with its sanitized log, and the wire contract pinned through the host's
  `JsonOptions`. Hermetic, dead-port database.
- `SavedRoutesPersistenceTests` (`[PostgresFact]`, expected **skipped**) — cross-rider list and
  detail reads, and the geometry round-trip.

### Manual Testing Steps:

1. Signed in with several saved routes: Account → Your saved routes; check order, distance, duration
   and date against what was saved.
2. Tap a row; confirm the route drawn matches the one saved and no Save action is shown.
3. Reload the app on `/saved-routes/{id}` (or open it by deep link); confirm it loads.
4. Save a new route, return to the list; confirm it appears at the top immediately.
5. Sign out, sign in as a second rider; confirm only their routes appear, with no flash of the first
   rider's.
6. With a token for rider A, request `GET /saved-routes/{a route id owned by rider B}` against the
   deployed API; confirm 404.
7. Kill the network and open the list; confirm the error card and that retry recovers.
8. A rider with no saved routes: confirm the empty state and its link back to Plan.
9. Repeat 1, 4 and 8 on web.

## Performance Considerations

The list response is metadata for at most `MaxItems` rows — a few KB regardless of how many routes a
rider has saved, because the projection keeps `geometry` in Postgres. The detail response is one row
bounded by the save-side 20,000-point ceiling (~600 KB), which is why it gets the save path's 15 s
budget rather than the 30 s generation default.

Both reads are single indexed-ish queries; the owner filter has no dedicated index, which is correct
at MVP volumes — a sequential scan over a table holding tens of rows is cheaper than maintaining an
index, and adding one later is a migration with no schema change for clients. The connection pool
stays at `DefaultMaxPoolSize = 8`: reads are short and the Supavisor session pooler's allowance is
the binding constraint, not query time.

## Migration Notes

None. `GRANT SELECT` is already held by `rideforge_api`, the schema is unchanged, and no new grant is
required. Nothing about this slice needs a deploy-time migration step, so the Railway pre-deploy
command is untouched.

No new native module, so **no new EAS dev build** — every client change here lands with a Metro
reload.

## References

- Predecessor slice (schema, save endpoint, client patterns, and the F7 finding assigned here):
  `context/archive/2026-09-11-save-route/plan.md` and its `change.md`
- Auth boundary and the `SubjectOf`/401 contract: `context/archive/2026-09-08-rider-auth/plan.md`
- Roadmap slice S-07: `context/foundation/roadmap.md`
- Risk/coverage constraints: `context/foundation/test-plan.md` §5, §6.1.1, §7, §8
- Save endpoint: [api/Program.cs:318](api/Program.cs:318)
- Storage shape: [api/Persistence/RideForgeDbContext.cs:29](api/Persistence/RideForgeDbContext.cs:29)
- Grants and RLS policy: [api/Persistence/Migrations/20260911173501_InitialSavedRoutes.cs:79](api/Persistence/Migrations/20260911173501_InitialSavedRoutes.cs:79)
- Map + web-split precedent: [src/app/result.tsx:49](src/app/result.tsx:49), `src/app/result.web.tsx`
- Cache-keying precedent (and its weakness): [src/components/session-provider.tsx:57](src/components/session-provider.tsx:57)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Owner-scoped read endpoints + tests

#### Automated

- [x] 1.1 Backend builds: `dotnet build api/RideForgeApi.slnx` — ea0729d
- [x] 1.2 Backend tests pass: `dotnet test api/RideForgeApi.slnx` — ea0729d
- [x] 1.3 New query-shaping, SQL-guard and endpoint tests report as passed, not skipped — ea0729d

#### Manual

- [x] 1.4 Deployed list with a real token returns that rider's routes and nothing else — ea0729d
- [x] 1.5 Deployed detail for another rider's route id answers 404 — ea0729d
- [x] 1.6 List response for a rider with several routes is a few KB, not megabytes — ea0729d

### Phase 2: Client data layer + list screen

#### Automated

- [x] 2.1 Typecheck passes: `npx tsc --noEmit` — 07ae28e
- [x] 2.2 Lint passes: `npm run lint` — 07ae28e
- [x] 2.3 Backend tests still pass: `dotnet test api/RideForgeApi.slnx` — 07ae28e

#### Manual

- [x] 2.4 Account → Your saved routes shows them newest-first with correct distance, duration, date — 07ae28e
- [x] 2.5 A rider with no routes sees the empty state and its link returns to Plan — 07ae28e
- [x] 2.6 A route saved then listed appears immediately, not after a delay — 07ae28e
- [x] 2.7 With the network killed the error card appears and retry works once it is back — 07ae28e
- [x] 2.8 A second rider on the same device sees only their own routes, with no flash of the first's — 07ae28e
- [x] 2.9 Same behaviour on web as on native — 07ae28e

### Phase 3: Revisit screen

#### Automated

- [x] 3.1 Typecheck passes: `npx tsc --noEmit` — 0bf2e18
- [x] 3.2 Lint passes: `npm run lint` — 0bf2e18

#### Manual

- [x] 3.3 `/result` behaves exactly as before the extraction, including the map fit — 0bf2e18
- [x] 3.4 Tapping a list row opens that route drawn correctly with its saved stats — 0bf2e18
- [x] 3.5 The revisit screen shows no Save action — 0bf2e18
- [x] 3.6 Reloading or deep-linking `/saved-routes/{id}` loads the route — 0bf2e18
- [x] 3.7 An unknown route id shows the not-available copy, not a crash or a spinner — 0bf2e18
- [x] 3.8 Generating a new route does not change what an open revisit screen shows — 0bf2e18
- [x] 3.9 Both screens render on web — 0bf2e18

### Phase 4: Endpoint declarations out of `Program.cs`

#### Automated

- [x] 4.1 Backend builds with no new warnings: `dotnet build api/RideForgeApi.slnx` — 6595d22
- [x] 4.2 Full backend suite green with unchanged pass and skip counts: `dotnet test api/RideForgeApi.slnx` — 6595d22
- [x] 4.3 No test file was edited: `git diff --stat api/RideForgeApi.Tests/` is empty — 6595d22

#### Manual

- [x] 4.4 `Program.cs` reads as configure → pipeline → map → run, with no endpoint body in it — 6595d22
- [x] 4.5 Every comment explaining why an endpoint behaves as it does is still attached to that endpoint — 6595d22

### Phase 5: Cookbook + close-out

#### Automated

- [x] 5.1 Full suite green: `dotnet test api/RideForgeApi.slnx`, `npm run lint`, `npx tsc --noEmit` — 043a5b8

#### Manual

- [x] 5.2 test-plan §6 reads as something a contributor could follow without this plan open — 043a5b8
- [x] 5.3 Nothing in `change.md` contradicts what actually shipped — 043a5b8
