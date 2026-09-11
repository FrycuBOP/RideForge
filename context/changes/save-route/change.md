---
change_id: save-route
title: Save route
status: implementing
created: 2026-09-11
updated: 2026-09-11
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
