<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-07 — Saved routes list

- **Plan**: context/changes/saved-routes-list/plan.md
- **Scope**: Phases 1–5 of 5 (full plan), plus the post-plan tab move (`61f576c`)
- **Date**: 2026-09-12
- **Verdict**: NEEDS ATTENTION at review time; all findings closed — 7 fixed, 1 partially fixed (half withdrawn), 1 accepted (web is test-only), 1 verified on device as needing no change
- **Findings**: 0 critical, 4 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

### What passed cleanly

**Every planned code item verified as MATCH.** All 26 numbered items across the five phases were
read against disk. The ownership guard — the whole point of the slice — holds: the four query sites
against `rideforge.saved_routes` are the insert, the repeat-save read, `SummariesOwnedBy` and
`OwnedRoute`, and all three reads carry `OwnerId ==`. Each endpoint calls `RequireAuthorization()`
individually, the owner comes only from the verified token, and the detail answers 404 rather than
403 so it cannot be used as an id probe. All four "Critical Implementation Details" claims are
honored, and the SQL guards are non-vacuous (the detail test asserts `geometry` *is* named, which is
what makes the list's absence assertion mean something).

**No "What We're NOT Doing" violations.** No migration, no `UPDATE`/`DELETE` grant, no RLS change, no
delete/rename/paging/search/sort, no GPX export, no offline persistence, no frontend test runner, no
CI. `/result` and the result store are untouched. The one deliberate departure — touching tab
configuration, which the plan's Implementation Approach avoided — was user-requested after the plan
closed and is recorded in `change.md`.

**Automated success criteria: all pass.** `dotnet build` 0 warnings / 0 errors. `dotnet test`
130 passed / 9 skipped / 0 failed, and the three new test classes are 27 passed / **0 skipped**
(7 + 5 + 15). All 9 skips are `[PostgresFact]`, and phase 1 added exactly 3 — the rise the plan
predicted. `npx tsc --noEmit` and `npm run lint` clean. Phase 4's "no test file was edited" holds:
`git diff --stat 6595d22^..6595d22 -- api/RideForgeApi.Tests/` is empty. Phase 4's readability claim
verified from the diff too: `Program.cs` went ~680 → 384 lines, and the only endpoint bodies left are
`/health` and `/me`, exactly the two the plan said stay.

## Findings

### F1 — 20,000-argument spread can throw during render of a saved ride

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/components/route-map.tsx:29
- **Detail**: `boundingRegion` computes extrema with `Math.min(...lats)` / `Math.max(...lngs)`,
  spreading one argument per geometry point. The server accepts up to
  `SavedRouteValidation.MaxGeometryPoints = 20_000` points, so a full-size saved ride spreads 20,000
  arguments on Hermes. Engine argument limits are exactly the territory this sits in, and the call
  happens during render — if it throws, the revisit screen dies rather than degrading. The exact
  Hermes threshold was not measured here, so this is a risk rather than a demonstrated crash; the
  point is that the fix costs nothing and removes the question. Carried over from `result.tsx`, but
  phase 3 put it on a second screen, and the revisit screen is the one that replays
  arbitrarily-large stored geometry.
- **Fix**: Replace the four spreads with a single `reduce` pass computing all four extrema — same
  cost, no argument-count ceiling.
- **Decision**: FIXED — replaced the four spreads with a single loop over the points (route-map.tsx:26-39); tsc + eslint clean.

### F2 — Two native navigation states around the hidden tab were never exercised

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/components/app-tabs.tsx:39, src/app/(tabs)/_layout.tsx:36
- **Detail**: The recorded rule is followed — the layout holds the navigator back on `isRestoring`
  and keys `AppTabs` on signed-in-ness, so `hidden` is constant per instance and the original
  `FragmentManager is already executing transactions` path is closed. What is untested is where the
  remount *lands*. `SIGNED_OUT` can fire from a failed token refresh while Saved is the focused tab,
  so a fresh navigator mounts with `saved-routes` hidden while that route is the active one. Same
  question for a cold-start deep link to `/saved-routes/<id>` while signed out. The code comment
  asserts a hidden tab "cannot be navigated to at all" but does not say what the rider sees instead
  — blank screen, silent fallback to Plan, or a throw. Per the accepted rule "Settle a native
  navigator's structure before it mounts", this class is unverified until it runs on a device, and
  no green gate here can see it.
- **Fix A ⭐ Recommended**: Run the two device checks first — sign out with Saved focused, and
  cold-start a deep link to a saved route while signed out — and only add code if one misbehaves.
  - Strength: The guards may already handle both; adding a redirect blind would be speculative code
    on a path that is hard to reason about and easy to get wrong.
  - Tradeoff: Costs a manual pass before the slice can be called closed.
  - Confidence: HIGH — this is the same verification gap that produced the launch crash, and the
    lesson entry already names device runs as the only detector for this class.
  - Blind spot: Whether a failed token refresh actually reaches `SIGNED_OUT` on this Supabase
    configuration was not traced.
- **Fix B**: Pre-emptively `router.replace('/')` in the sign-out path so a focused hidden tab can
  never be the landing state.
  - Strength: Removes the bad state by construction instead of relying on the navigator to cope.
  - Tradeoff: Adds a navigation side effect to sign-out that fires even when unnecessary, and yanks
    a rider off Account mid-flow.
  - Confidence: MEDIUM — correct in shape, but untested against the remount ordering.
  - Blind spot: Interaction with the keyed remount — the redirect and the key flip race.
- **Decision**: NO CHANGE NEEDED (Fix A) — both device checks were run on Android and neither misbehaves (user, 2026-09-12): signing out with Saved focused, and a cold-start deep link to /saved-routes/<id> while signed out. The guards already cope, so Fix B was not applied.

### F3 — The close-out record contradicts the shipped code in three places

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/saved-routes-list/change.md:54, :57, :62
- **Detail**: `change.md`'s "Decisions taken while implementing" section was written against the
  pre-tab-move code, and the later tab section did not correct the specific bullets it invalidated.
  Three concrete contradictions: (1) `:54` says "the revisit screen takes the default" fit padding —
  it does not, `(tabs)/saved-routes/[id].tsx:19` defines `FIT_BOTTOM_PADDING = 200 + BottomTabInset`
  and passes it explicitly; (2) `:57` says the list screen carries "one `Platform.OS === 'web'`
  branch … everything else is shared" — there are two, `index.tsx:96` (`Platform.select` for android
  and web padding) and `:205` (the max-width branch); (3) `:62` scopes the supersede to "plan phase 2
  §5/§7", but phase 2 §8 is superseded too — it required root-stack `<Stack.Screen>` entries and
  stated "the `saved-routes` directory intentionally has no `_layout.tsx`", and a nested
  `(tabs)/saved-routes/_layout.tsx` is now exactly what exists. Also unrecorded: because the native
  tab uses `hidden`, `/saved-routes` is unreachable on native while signed out, so the list screen's
  signed-out branch is web-only — the code comment says this, the change record does not, and plan
  phase 2 §5's stated rationale for that branch ("reachable by deep link") now holds on web alone.
  This matters because manual criterion 5.3 ("nothing in change.md contradicts what actually
  shipped") was confirmed against this document.
- **Fix**: Correct the three bullets, widen the supersede scope to §5/§7/§8, and add one line
  recording that the signed-out branch is web-only on native.
- **Decision**: FIXED — the fitBottomPadding and Platform-branch bullets now describe what shipped and say which part the tab move changed; the supersede scope widened to §5/§7/§8 with a bullet explaining that §8 was reversed, not merely superseded; and a bullet records that the signed-out branch is web-only on native.

### F4 — Both web manual criteria predate the commit that rewrote the web surface

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/saved-routes-list/plan.md (Progress rows 2.9, 3.9)
- **Detail**: Row 2.9 ("same behaviour on web as on native") carries SHA `07ae28e` and row 3.9 ("both
  screens render on web") carries `0bf2e18`. Both are ancestors of `61f576c`, which rewrote
  `app-tabs.web.tsx` into the hidden-`TabList`-plus-visible-bar shape and moved both screens into
  `(tabs)/saved-routes/`. The confirmations therefore attest to a surface that no longer exists. The
  signed-out web state was re-verified during the tab work (`/saved-routes` renders the "Sign in
  first" branch, tab bar shows Home + Account only); the **signed-in** web state — the Saved button
  appearing, and the list and revisit screens rendering under the new bar — has not been verified by
  anyone.
- **Fix**: Re-run the signed-in web pass (`npm run web`, sign in, open Saved, tap a row) and note it
  in `change.md`; leave the Progress SHAs alone, since they correctly record when the original check
  happened.
- **Decision**: ACCEPTED — web is a development/testing convenience only; the product ships mobile-only (user, 2026-09-12). An unverified signed-in web state is not a release risk. Recorded as a project fact so future plans stop generating web parity criteria. Progress SHAs left alone.

### F5 — ~600 KB detail responses ship uncompressed

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs (no `UseResponseCompression` anywhere; `grep` count 0)
- **Detail**: The detail endpoint serialises a geometry array up to ~600 KB and no response
  compression is configured. Unless Railway's edge gzips on the way out, every revisit ships the full
  payload over mobile data. A coordinate array is highly compressible — roughly an order of
  magnitude.
- **Fix**: Add response compression (Brotli/gzip for `application/json`), or confirm the platform
  edge already does it and note that instead.
- **Decision**: FIXED — AddResponseCompression (json + problem+json, EnableForHttps) registered in Program.cs and UseResponseCompression wired after UseForwardedHeaders.

### F6 — An immutable saved ride is re-downloaded every 30 seconds

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/hooks/use-saved-route-query.ts:21 (inherits src/api/query-client.ts:11)
- **Detail**: A saved route cannot change once written — the runtime role holds no `UPDATE` grant —
  yet the detail query inherits the global `staleTime: 30_000`. Re-entering a ride half a minute
  later re-fetches ~600 KB for data that is incapable of having changed.
- **Fix**: Give the detail query a long or `Infinity` `staleTime`/`gcTime`; the save-mutation
  invalidation already covers the only event that can change a rider's saved data.
- **Decision**: FIXED — staleTime: Infinity on the detail query, with the immutability reasoning in a comment.

### F7 — Navigating away mid-load leaves the transfer running

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/hooks/use-saved-route-query.ts:23
- **Detail**: `queryFn: () => getSavedRoute(id)` discards react-query's `AbortSignal`. This is not a
  no-op here: `src/api/client.ts:89-105` chains a caller signal into its own controller and passes it
  to `fetch`, so the signal genuinely would abort the request. At ~600 KB per detail read, backing out
  of a ride leaves a full transfer completing in the background.
- **Fix**: `queryFn: ({ signal }) => getSavedRoute(id, signal)`, threading the signal through to
  `request`.
- **Decision**: FIXED — getSavedRoute takes an optional AbortSignal and forwards it to request; the hook passes react-query's signal.

### F8 — The identity behind the sole cross-rider guard is read ambiguously

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Auth/RiderIdentity.cs:17, :26
- **Detail**: `SubjectOf` reads `ClaimTypes.NameIdentifier` first and falls back to `"sub"`. With
  default inbound claim mapping a literal `nameid` claim also lands on `NameIdentifier`, so a token
  carrying both would resolve to whichever appears first in claim order — not necessarily `sub`.
  Separately, `TryGetRiderId` accepts `Guid.Empty`, making an all-zero subject a valid owner
  namespace. Neither is reachable on this Supabase configuration today (extra top-level claims need a
  custom access-token hook), so this is hardening on the one predicate that stands between two
  riders, not an open hole.
- **Fix**: Read `"sub"` first, and reject `Guid.Empty` in `TryGetRiderId`.
- **Decision**: FIXED — SubjectOf reads "sub" first; TryGetRiderId rejects Guid.Empty. No test depended on the old behaviour (130 passed / 9 skipped unchanged).

### F9 — Two map/loader edge behaviours worth tightening

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/components/saved-route-loader.tsx:77, src/components/route-map.tsx:66
- **Detail**: (1) When the route param is empty the query is `enabled: false`, react-query reports
  `isPending`, and the loader renders a spinner with no error branch and no timeout — a permanent
  spinner if the param never resolves. The window is narrow (the route pattern requires a segment, so
  `id` resolves after the first render), but the failure mode is terminal rather than degraded.
  (2) `onLayout` re-runs `fitToCoordinates` on *every* layout change, so a rider who pans or zooms a
  saved ride loses their position on rotation or split-screen. Pre-existing behaviour, but it matters
  more on a revisit screen than on a just-generated one.
- **Fix**: Treat a still-missing `id` after mount as the 404 branch; latch the map fit to run once per
  geometry identity.
- **Decision**: PARTIALLY FIXED — (1) a missing route param now falls through to the unavailable branch after a 1s grace period (a timer rather than setState-in-effect, which the React compiler lint rejects). (2) WITHDRAWN: the finding cited rotation, but `app.json` locks `orientation: portrait`, so that scenario cannot occur. A latch was applied and then reverted — it traded a self-healing behaviour (a later layout pass corrects a bad fit) for protection against split-screen and font-scale relayouts that this app effectively never sees, and introduced a "first layout wins forever" risk. The repeated fit stands as it was.

### F10 — Five convention divergences introduced by the refactor

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: api/RideForgeApi.Tests/SavedRoutesReadEndpointTests.cs:30, api/SavedRoutes/SavedRoutesEndpoints.cs:36, api/Program.cs:292, src/lib/query-keys.ts:16
- **Detail**: None break anything; each makes the next contributor guess.
  (1) `SavedRoutesReadEndpointTests` borrows its fixture from another test class's nested type
  (`SavedRoutesEndpointTests.UnreachableDatabaseFactory`) and repeats that qualified name nine times,
  while every other shared factory in the suite has its own file.
  (2) With reads split out, the unqualified `SavedRoutesEndpointTests` is now the *write* suite and
  nothing in the name says so.
  (3) The two new map-extensions disagree on shape: `MapSavedRoutes` (feature-named) vs
  `MapRouteEndpoints` (type-named).
  (4) `/health` and `/me` stayed inline while two features moved out, so "where do endpoints get
  declared" has two answers and no note saying trivial ones stay put by design.
  (5) The slice introduces a key-factory module and then leaves `['me']` a bare literal in
  `use-me-query.ts:16` and `session-provider.tsx:61` — the latter sitting directly beside
  `SAVED_ROUTES_KEY`, and the module's own docstring gives a reason that applies to `['me']` verbatim.
- **Fix**: Promote `UnreachableDatabaseFactory` to its own file, rename the write suite to
  `SavedRoutesSaveEndpointTests`, settle one `Map*` naming shape, add `meKey()` to `query-keys.ts`,
  and either move `/me` beside `RiderIdentity` or state the inline-by-design rule in a comment.
- **Decision**: FIXED all five — UnreachableDatabaseFactory + CapturingLoggerProvider promoted to UnreachableDatabaseFactory.cs; SavedRoutesEndpointTests renamed SavedRoutesSaveEndpointTests (references updated in DatabaseFailures.cs, the read suite, test-plan §6.1.1 and change.md; archived slices left alone); MapSavedRoutes renamed MapSavedRouteEndpoints to match MapRouteEndpoints; meKey()/ME_KEY added to query-keys.ts and used in both call sites; Program.cs now states the inline-endpoint rule.

## Notes not raised as findings

- **`(tabs)/account.tsx` nets to zero** in the diff because phase 2 added the `Link` and the tab
  commit removed it. Verified: the file is byte-identical to its pre-slice state and no other planned
  account change was lost.
- **Missing index for the list ordering.** The list filters `owner_id` and sorts
  `created_at DESC, id DESC`, but the only index is the unique `(owner_id, client_route_id)` — so
  Postgres uses the leading column for the filter and then sorts. Correct at MVP volumes by the plan's
  own explicit reasoning; worth an index when paging arrives, and the "indexed-ish" comment slightly
  overstates today.
- **Two SQL guards beyond the plan** (`ORDER BY` assertion, detail-names-geometry assertion) are
  strictly additive and are now codified as test-plan §6.1.2 step 4.
- **The plan says Program.cs keeps "the three `Map*` calls"**; only two route-group extensions exist
  alongside the inline `/health` and `/me` the same sentence lists separately. Plan-side arithmetic,
  not code drift.
- **Roadmap S-07 is still `in-progress`** and `change.md` is `implemented` / `archived_at: null` —
  consistent, and exactly the state expected before `/10x-archive` runs.
- **EAS**: no new native dependency, so the existing dev build stands. But because a native tab
  trigger set and a new nested `Stack` are involved, verify with a full app restart rather than Fast
  Refresh.
