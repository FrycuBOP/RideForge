# S-07 — Saved routes list — Plan Brief

> Full plan: `context/changes/saved-routes-list/plan.md`

## What & Why

A signed-in rider opens their saved routes from Account, sees them newest-first, and taps one to see
it drawn on the map again (FR-010). This closes the PRD's secondary success criterion — save *and*
revisit past rides — whose first half S-06 shipped. It is also the slice S-06 explicitly assigned the
ownership guard to: that slice's review accepted finding F7 rather than fixing it, on the grounds
that S-07's `GET` would filter by owner in the query.

## Starting Point

Persistence, auth and the save path all exist and need no change. `rideforge.saved_routes` already
holds everything a list and a revisit need, and the runtime role already holds `GRANT SELECT` — so
this slice adds no migration. What does not exist: any read endpoint, any screen for the list, and
any per-rider ownership enforcement. The RLS policy is `USING (true)`, an exposure guard that scopes
nothing per rider, so the query's owner filter is the only thing between riders. The only map code
lives inside `result.tsx`, fed by a session-only in-memory store that cannot represent a route saved
last week.

## Desired End State

Account shows **Your saved routes**; the list has each ride's name, distance, duration and save date,
newest first. Tapping one opens it on the map with its stats and no Save action — it is already
saved, and the screen is deep-linkable because it fetches by id rather than reading the store. A
first-time rider gets an empty state pointing back to Plan. A second rider signing in on the same
device sees their own list and no trace of the previous one.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| List shape | Summary envelope + separate detail endpoint | A row carries up to ~600 KB of geometry, so the list response stays a few KB however many routes a rider has. |
| Paging | Newest-first, server-side cap of 50, no cursor | Bounds the response with no client paging state; the envelope lets a cursor be added later without breaking the client's parse guard. |
| Revisit | Its own `/saved-routes/[id]` route that owns its fetch | Keeps a saved route and a just-generated one separate things — deep-linkable, survives reload, and cannot overwrite a ride mid-save. |
| Mutations | Read-only: no delete, no rename | FR-010 is "view a list", and the DB role holds only SELECT/INSERT — no migration, no new grant, no write path on a brand-new ownership surface. |
| Placement | Stack route pushed from Account | Touches no tab config: `NativeTabs` is unstable and the web build carries a second hand-written tab list. |
| Cache key | Keyed by rider id | Makes a cross-rider bleed structurally impossible instead of dependent on a cleanup call firing, unlike today's `['me']`. |
| Freshness | Save mutation invalidates the list key | The one event that can make the list wrong is the one that refreshes it. |
| States | All four — signed out, empty, error, loading | Reuses the error mapping Account and Save already established; the empty state is what every first-time rider sees. |
| Ownership test | Query shaping extracted to a pure `IQueryable` function | LINQ-to-objects executes the *real* filter with no database, so the IDOR guard runs on every `dotnet test` despite the Postgres suite being permanently skipped. |
| Other rider's id | 404, not 403 | A 403 confirms the route exists. |

## Scope

**In scope:** `GET /saved-routes` (capped, newest-first, geometry-free envelope) and
`GET /saved-routes/{id:guid}`; owner-scoped query shaping as a testable pure function; hermetic IDOR,
ordering, cap, generated-SQL and wire-contract tests plus `[PostgresFact]` row tests; client read
functions with response guards; per-rider query hooks and save-time invalidation; the
`/saved-routes` screen with four states and its Account entry point; the map extracted from
`result.tsx` into a shared component with a `.web` sibling; `/saved-routes/[id]` on both platforms;
a test-plan §6 cookbook entry.

**Out of scope:** delete, rename, paging, search/filter/sort; GPX export from a saved route (S-04);
any migration or grant change; RLS policy changes; a frontend test runner; CI (test-plan §5 assigns
it to rollout Phase 4); offline persistence; any change to `/result`'s role or the result store.

## Architecture / Approach

```
Account ──► /saved-routes ──────────► GET /saved-routes      ──┐
              (FlatList of                (summary envelope)   │  SavedRouteQueries
               summaries)                                      ├─ .SummariesOwnedBy(ownerId, 50)
                 │ tap                                         ├─ .OwnedRoute(ownerId, routeId)
                 ▼                                             │      ↑
           /saved-routes/[id] ──────► GET /saved-routes/{id}  ──┘   owner from verified token's
              (RouteMap + RideStats)     (one row + geometry)          sub — never a parameter
```

The filter, ordering, cap and projection live in one pure helper over `IQueryable<SavedRoute>`:
against EF it composes into SQL, against `List<SavedRoute>.AsQueryable()` it runs as LINQ-to-objects
— so the ownership rule is executed by tests that need no database. `ToQueryString()` covers the two
claims in-memory execution cannot make: the predicate and cap reach Postgres parameterised, and the
list SQL never names the `geometry` column.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Read endpoints + tests | Both owner-scoped endpoints, ownership pinned by tests that actually run | An id-only filter on the detail endpoint — the IDOR bug, invisible without the extracted helper |
| 2. Data layer + list screen | Read functions, per-rider keys, save invalidation, the list and its entry point | Rider B inheriting rider A's cached list on a shared device |
| 3. Revisit screen | Map extracted to a shared component, `/saved-routes/[id]` native + web | The extraction regressing `/result`'s map fit or the Android on-mount crash workaround |
| 4. Cookbook + close-out | test-plan §6 entry; F7 recorded as discharged | The pattern going unwritten and the next read endpoint filtering by id alone |

**Prerequisites:** S-06 (done, archived). No migration, no Supabase or Railway change, no new
dependency. **No new EAS dev build** — no phase adds a native module, so everything lands with a
Metro reload.
**Estimated effort:** ~3 after-hours sessions — phase 1, phase 2, then phase 3 + 4 together. Phase 1
suits `/10x-tdd` (the first red test names itself: "returns only the caller's routes from an
interleaved two-rider set").

## Open Risks & Assumptions

- **The hermetic tests are the only executed ownership guard.** The `[PostgresFact]` suite is
  permanently skipped and no CI exists, so a real-database cross-rider read is verified by hand on
  the deployed API and nowhere else.
- **RLS still scopes nothing per rider.** If the query's owner filter is ever dropped, no database
  policy catches it — by design, since a per-rider policy would need per-request Postgres roles the
  session pooler cannot provide.
- **The cap is silent.** A rider who somehow exceeds 50 saved routes loses access to their oldest
  with no indication, and there is no delete to bring them back under.
- **Extraction touches a working screen.** `/result` is the north-star flow's last screen; the map
  extraction must preserve its fit padding and the Android `onLayout` workaround exactly.
- **No index on `owner_id`.** Correct at MVP volumes, but it is a sequential scan that grows.
- Assumes `created_at` ties are possible (`now()` is transaction-scoped), hence the `id` tiebreak.

## Success Criteria (Summary)

- A signed-in rider finds their saved routes from Account, newest-first, and opens one to see it on
  the map again.
- No rider can read another rider's saved route, by list or by id — and a test that runs without a
  database fails if that filter is ever dropped.
- A route saved is visible in the list immediately, and a rider switch on a shared device never shows
  the wrong list.
