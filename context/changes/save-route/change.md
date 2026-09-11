---
change_id: save-route
title: Save route
status: impl_reviewed
created: 2026-09-11
updated: 2026-09-12
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

### 2026-09-11 — Migrations run on deploy, not by hand (supersedes the plan's "applied by hand")

Decided during phase 1 implementation. With code-first, a hand-applied migration will eventually be
forgotten, and code that needs a newer schema would then 500 every save.

- **How:** the Docker build produces an EF migration bundle (`efbundle`); Railway's
  `preDeployCommand` runs it via `api/migrate.sh` before the new version takes traffic. A failing
  migration stops the deploy and the previous version keeps serving — unlike `Migrate()` on API
  startup, which would crash-loop the whole API (generation included).
- **As whom:** a dedicated `rideforge_migrator` role that owns the `rideforge` schema and nothing
  else — not the `postgres` owner, whose password would reach `auth.users`. Railway gets a second
  variable, `ConnectionStrings__RideForgeMigrations`; runtime stays `rideforge_api` (SELECT/INSERT).
  The design-time factory reads the same variable, so local `dotnet ef` also acts as the migrator.
- **One-time bootstrap:** `api/Persistence/bootstrap-supabase.sql`, run once as `postgres` in the
  Supabase SQL editor — creates both roles with logins and hands the schema to the migrator.
- **Replaces plan steps:** phase 1 §8 manual steps 1–2 (hand `database update` + `alter role … login`).
  Rollback via `dotnet dotnet-ef database update 0` still works, run locally as the migrator.
- **New constraint:** during a deploy the old version briefly runs against the new schema, so
  migrations must stay backward-compatible (expand first, contract in a later release).
- **New rule for future migrations:** every new table needs its own `GRANT` to `rideforge_api` in the
  migration — the migrator owns the tables, so nothing is granted implicitly.
- **Pre-deploy is a Railway dashboard setting, not a repo file.** `api/railway.toml` was never read
  (config-as-code paths ignore the Root Directory `/api`; first deploy of `61e11bc` had no Pre-deploy
  step), and Config as Code is deprecated — services that never used it can no longer opt in.
  Infrastructure as Code would need the Railway CLI + `railway config apply` in CI (out of scope).
  So: API service → Settings → Deploy → Pre-deploy Command = `/bin/sh /app/migrate.sh`, and the dead
  `railway.toml` was deleted. The logic stays versioned in `migrate.sh`; only that one line lives in
  Railway. Railway auto-detects `api/Dockerfile`, so nothing else was lost.
- **Tool manifest** lives at the repo root (`dotnet-tools.json`, .NET 10 SDK default), not `.config/`.
- **1.4 (local Postgres apply) deferred** — no local Postgres; the first real apply is the Railway
  pre-deploy against Supabase (1.5). Revisit when phase 2 brings a local database.

### 2026-09-11 — Phase 2 decisions made during implementation

- **2.2 / 2.4 deferred (with 1.4).** Still no local Postgres. The six `[PostgresFact]` tests are
  written and report as skipped; the deployed save (2.6/2.7) is the real-database check for now.
  Run them with `RIDEFORGE_TEST_DB` set as soon as a local Postgres exists (test-plan §6.1.1).
- **Stitched-distance ceiling added** (`SavedRouteValidation.MaxDistanceMeters` = 10,000 km). The
  plan only said "finite and > 0"; without an upper bound a value like 1e300 would produce a
  300-digit name that overflows `name varchar(120)`. Nothing real comes near it.
- **EF failure events demoted to Debug** in `Program.cs` (`ConnectionError`, `CommandError`,
  `SaveChangesFailed`, `QueryIterationFailed`). EF logs Npgsql's raw exception, which names the host,
  and the plan says no connection detail reaches a log line. The endpoint logs its own sanitized
  warning (exception types, socket error, SQLSTATE + server message). A hermetic test checks the
  log sink; it fails if the demotion is removed.
- **A valid token whose `sub` is not a UUID → 401.** The plan didn't specify this case.
- **Naming cut:** 40 characters of label are kept, trailing whitespace trimmed, then `…` is added,
  and a cut never splits a surrogate pair. Kilometres round halves away from zero (42.5 → 43).
- **Repeat detection matches the constraint name** (`ux_saved_routes_owner_client_route`), not just
  SQLSTATE 23505, so a different unique violation can never be answered with an existing row.

### 2026-09-12 — No local Postgres: 1.4 / 2.2 / 2.4 closed as won't-do

Decided at the end of phase 3. There is no local Postgres on this machine and there will not be
one, so the three rows that ask for an apply/run against a local database are not deferred work —
they are out of scope for good. Left unchecked in `## Progress` deliberately: `/10x-archive` will
warn about them, and that warning is accurate.

- **What still covers those rules:** the Railway pre-deploy migration (the real apply, 1.5) and the
  deployed save with a real token (2.6/2.7). The six `[PostgresFact]` tests stay in the repo,
  skipped, ready for the day a database is pointed at `RIDEFORGE_TEST_DB` — a CI Postgres service
  or a Supabase branch, not a local install.
- **The risk this makes real:** the plan's "the Postgres suite can rot unnoticed" is now permanent,
  not a temporary gap. Per-owner uniqueness, idempotency, owner-from-token and the geometry
  round-trip are pinned only by code review and the deployed smoke test until that gate exists.
- **2.8 (Stryker)** was optional and was not run.

### 2026-09-12 — Phase 3 decisions made during implementation

- **`markLastRouteSaved(clientRouteId, userId)`** takes the route id as well as the rider (the plan
  said `markLastRouteSaved(userId)`), and no-ops when the store has already moved on to a newer
  ride. A slow save must never mark a route the rider did not save.
- **The rider is captured in `onMutate`**, not read at success time, so a session change mid-flight
  cannot hand the next rider a "Saved ✓". The signed-in subtree is keyed by `user.id` so rider B
  inherits none of rider A's pending/error/success state.
- **The start label is cut to 200 characters client-side** (`toSaveRouteRequest`), without splitting
  a surrogate pair. A typed address longer than the column would otherwise be a guaranteed 400.
- **`router.navigate('/account')`**, not `push`: the tabs sit under the result screen, so navigate
  unwinds to them instead of stacking a second tab navigator.
- **Copy:** signed-out Account text now mentions saving; both result screens dropped "Generated
  routes aren't saved yet"; the native map's bottom fit padding grew 200 → 280 for the taller overlay.

### 2026-09-12 — Fixes applied from the implementation review

`context/changes/save-route/reviews/impl-review.md` — F1–F5, F8, F9 fixed; F6 is this note.

- **Hermetic wire-contract test** (F1). The `[PostgresFact]` suite never runs, so nothing executable
  covered the shape of a *successful* save. `SuccessWireContract_IsTheOneTheClientConsumes`
  serializes through the host's own `JsonOptions` and pins `id` / `name` / `createdAt` plus the
  camelCase request binding — the regression the client's response guard would otherwise turn into
  "unexpected response" on every save.
- **Connection pool capped** (F2). `RideForgeDbContextOptions.DefaultMaxPoolSize = 8`, applied unless
  the connection string names its own. Npgsql's default of 100 against the Supavisor *session*
  pooler could exhaust the pooler's allowance on one burst and 503 every rider.
- **Pending-migrations check** (F3). `PendingMigrationsCheck` reports at startup, in the background,
  when the database is missing migrations this build expects — the symptom of a pre-deploy step that
  did not run, which otherwise surfaces only as undifferentiated 503s. It applies nothing.
- **Server messages allow-listed in logs** (F4). `LogDatabaseFailure` logged `MessageText` verbatim;
  SQLSTATE 28P01 reads `password authentication failed for user "rideforge_api.<project-ref>"`. Only
  statement-level codes (constraint, privilege, undefined table/column) keep their message now.
- **Request body capped at 2 MB for saves** (F5). The 20,000-point ceiling is reachable only after
  the whole array is materialised; Kestrel's 30 MB default let a signed-in caller force hundreds of
  thousands of allocations per request. A full-size geometry is ~600 KB.
- **Request shaping moved to `src/api/saved-routes.ts`** (F8) — it is wire shaping, not a hook.
- **Postgres test helper asserts the status before parsing** (F9).

Accepted rather than fixed:

- **F7 — RLS is an exposure guard, not an ownership guard.** The policy is `USING (true)`: it keeps
  the table away from the Data API and the anon key, and scopes nothing per rider. S-07 owns the
  mitigation — its `GET` filters by `ownerId` in the query, and test-plan §8 already requires an
  IDOR read test for it. A belt-and-braces EF global query filter is S-07's decision, not this
  slice's. What must not happen is anyone reading "RLS is on" as "rows are owner-scoped".
- **F10 — migrator connection string in `argv`, silent localhost fallback.** Container-local
  exposure only, and the fallback cannot bite while no local Postgres exists. The fix touches the
  deploy path and cannot be verified without a deploy.
