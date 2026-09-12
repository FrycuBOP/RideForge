---
change_id: saved-routes-list
title: Saved routes list
status: impl_reviewed
created: 2026-09-12
updated: 2026-09-12
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

### 2026-09-12 — S-06 review finding F7 is discharged

S-06's implementation review accepted F7 rather than fixing it, on the grounds that S-07 owns the
mitigation. It does now. The RLS policy on `rideforge.saved_routes` is unchanged and still
`FOR ALL TO rideforge_api USING (true)` — an exposure guard that keeps the table off the anon key and
scopes nothing per rider — so the guard between two riders is the `owner_id` predicate in the query,
and nothing else.

- Both reads filter on `OwnerId` in `api/SavedRoutes/SavedRouteQueries.cs`; the owner comes from
  `RiderIdentity` and never from the request.
- `GET /saved-routes/{id}` answers **404** for another rider's route id — the same answer as a route
  that does not exist, so the response cannot be used to probe for other riders' ids.
- The rule is pinned by tests that **run without a database**: `SavedRouteQueriesTests` executes the
  real filter expressions over `List<SavedRoute>.AsQueryable()`, and `SavedRouteQuerySqlTests` uses
  `ToQueryString()` to prove the predicate and the cap reach Postgres parameterised and that the list
  SQL never names `geometry`. The `[PostgresFact]` versions in `SavedRoutesPersistenceTests` exist and
  report as skipped, as everything Postgres-bound does on this project.
- Written up for the next contributor as test-plan §6.1.2; §8's ledger bullet now records that the
  IDOR surface has hermetic coverage while still being absent from §2's risk map.

### 2026-09-12 — Decisions taken while implementing that the plan did not specify

- **`src/lib/query-keys.ts` is a new module** rather than keys declared beside the hooks. The plan put
  `['saved-routes', userId]` in the hooks and had `SessionProvider` sweep the prefix, which closes an
  import cycle (`use-saved-routes-query → use-session → session-provider`). A key module with no
  imports of its own cannot. Also gives the save mutation and the provider one definition to agree on.
- **`src/components/saved-route-loader.tsx` was extracted in phase 3.** The plan had
  `saved-routes/[id].tsx` and its `.web` sibling each own their loading / 404 / 401 / retry branches;
  two copies of that state machine would have drifted. The loader owns every state and hands the
  loaded ride to a render prop, so the only difference between native and web is what gets drawn.
- **`RiderIdentity.TryGetRiderId` was added**, which the plan listed as "consider". Three handlers
  repeated `Guid.TryParse(SubjectOf(user))`; the rule that a non-UUID subject identifies nobody now
  has one home.
- **The 503 helper was renamed in phase 1**, as the plan allowed: `SaveUnavailable()` →
  `DatabaseUnavailable(detail)`, with `SaveFailedDetail` / `ReadFailedDetail` passed in. The mapping,
  the status and the sanitized log are unchanged — the save suite still passed untouched (that class is
  now `SavedRoutesSaveEndpointTests`, renamed during the implementation review).
  Phase 4 moved it to `api/Persistence/DatabaseFailures.cs`.
- **Client timeout budgets**: list 10 s, detail 15 s. The plan said "a short budget" for the list and
  "the save path's 15 s" for the detail; these are the numbers. The detail is defined as
  `SAVE_TIMEOUT_MS` rather than a second literal, since the reason is the same ~600 KB transfer.
- **`RouteMap` takes `fitBottomPadding` with a default** instead of requiring it at both call sites.
  The result screen passes its existing 280 (stats card + Save action). The revisit screen took the
  default at the time of this decision; the later tab move gave it an explicit
  `200 + BottomTabInset` instead, because the tab bar now floats over its map.
- **The list screen branches on platform twice** rather than carrying a `.web` sibling: once for the
  content container's padding (`Platform.select`, android vs web) and once for its max width
  (`Platform.OS === 'web'`). The second was there from the start; the first arrived with the tab
  move, which gave the screen the tab-screen inset convention. Everything else about the screen is
  shared, so a `.web` sibling would have duplicated ~200 lines to change a style — the repo
  convention (a `.web.` file over a `Platform` branch) earns its keep when a native module is
  involved, which here it is not.

### 2026-09-12 — The list became a tab, after the plan closed (supersedes plan phase 2 §5/§7/§8)

Asked for after phase 4 landed: move the saved-routes list into the bottom tab bar in the slot the
scaffold's Explore screen held, visible only to a signed-in rider. The plan had deliberately kept
tab configuration untouched ("`NativeTabs` is unstable and the web build carries a second,
hand-written tab list"), so this supersedes it rather than fulfilling it.

What shipped:

- **`src/app/(tabs)/saved-routes/`** — the list (`index.tsx`), the detail (`[id].tsx` + `.web`) and a
  nested `_layout.tsx` stack. Both URLs are unchanged (`(tabs)` is not part of the path).
- **Explore is gone**: `src/app/(tabs)/explore.tsx` deleted, and the Account link removed — the tab
  replaces it rather than duplicating it. `assets/images/tabIcons/explore.png` is now unreferenced.
- **The detail had to move with the list.** Declaring `(tabs)/saved-routes.tsx` while
  `src/app/saved-routes/[id].tsx` stayed at the root put two subtrees on the `saved-routes` segment;
  the list lost silently and `/saved-routes` rendered the Plan screen. One subtree, one segment.
- **The detail now sits inside the tab**, so the tab bar floats over its map. Its stats overlay and
  the map's fit padding gained `BottomTabInset` accordingly.
- **The web tab bar was restructured** to the SDK 56 custom-tabs shape: a hidden `TabList` registers
  every route, and the visible bar is built from triggers outside it. Rendering the Saved trigger
  only when signed in — the obvious version — *deregisters* the route, and a signed-out rider
  opening `/saved-routes` lands on Plan instead of the screen's own "sign in first" state.
- **Phase 2 §8 is reversed, not just superseded.** It required `<Stack.Screen>` entries for
  `saved-routes/index` and `saved-routes/[id]` in the root stack and stated that "the `saved-routes`
  directory intentionally has no `_layout.tsx`". Both held after phase 2 and both were undone here:
  the root stack now registers only `(tabs)` and `result`, and a nested
  `(tabs)/saved-routes/_layout.tsx` is exactly what does the registering. A reader reconciling the
  plan against disk should start here rather than assuming the entries were lost.
- **On native, the list's signed-out branch is dead code.** A hidden `NativeTabs` tab cannot be
  navigated to at all, so a signed-out rider cannot reach `/saved-routes` even by deep link. Plan
  phase 2 §5 justified that branch as "reachable by deep link even though the entry point is
  signed-in only"; that rationale now holds on **web only**, where the URL is still reachable and the
  branch still renders. It is kept rather than deleted because a session can also expire while the
  screen sits open.

**The Android crash this cost, and the rule it leaves behind.** `hidden={session === null}` on the
trigger crashed the app on launch with `FragmentManager is already executing transactions`
(`TabsContainer.onAttachedToWindow` → `flushPendingUpdates` → `commitNowAllowingStateLoss`, raised
from inside the activity's own resume transaction). Session restore resolves a few frames after
launch, so the trigger set changed exactly as the native container was attaching.

The fix is in `(tabs)/_layout.tsx`: hold the navigator back until `isRestoring` is false, then key
`AppTabs` on signed-in-ness so a session change swaps in a new navigator instead of mutating an
attached one. `hidden` is now constant for the life of each instance. The SDK 56 docs frame this as
"visibility changes should ideally occur before the navigator is mounted"; on Android the penalty
for ignoring it is a crash, not the documented state reset.

Worth knowing: typecheck, lint and the web preview were all green while the app died on launch. Only
a device catches this class of fault.

**Not done, deliberately:** the web tab bar still reads "Expo Starter" and links to docs.expo.dev —
scaffold leftovers, out of scope for a tab move.

### 2026-09-12 — EAS build verdict

**No new dev build.** `react-native-maps` was already a dependency at 1.27.2 and nothing in this
slice adds a native module, so every client change in phases 2 and 3 lands with a Metro reload. The
later tab move is the same verdict: `NativeTabs` and `react-native-screens` were already installed,
so it too is a reload — which is also why the launch crash it caused reproduced without one.

### 2026-09-12 — Left out deliberately

- No delete, rename, paging, search or sort — FR-010 is "view a list". The table still holds no
  `UPDATE`/`DELETE` grant, so a route saved by mistake is permanent. Accepted.
- No frontend test runner (test-plan §7 excludes `src/app/` and `src/components/`), so the date
  formatter's must-not-throw behaviour is verified by hand.
- No CI, so the `[PostgresFact]` suite stays skipped. test-plan §3 Phase 4 owns that.
