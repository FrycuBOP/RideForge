# S-06 — Save a generated route — Plan Brief

> Full plan: `context/changes/save-route/plan.md`

## What & Why

A signed-in rider can save the route they just generated to their account (FR-009). It is the first
half of the PRD's secondary success criterion (save and revisit rides), and after the generation
quota it is the second concrete thing an account gives a rider. It is also the slice that introduces
persistence to the project, so the choices here are the foundation S-07 (list) builds on.

## Starting Point

There is no database layer at all. S-05 kept it out on purpose, leaving an unused Postgres inside the
existing Supabase project and a proven JWT boundary in the .NET API (`/me` reads the verified `sub`).
The generated route lives only in an in-memory store, and the generate response has already dropped
the start point, the start label, and the requested km.

## Desired End State

Signed in, the rider taps **Save route** on the result screen, sees **Saving…** then **Saved ✓**,
and the ride (geometry, stats, inputs, auto-name like `Loop from Kraków · 42 km`) is stored under
their user id. Double taps and retries never duplicate. Signed out, **Sign in to save** leads to the
Account tab and back to the route. The table cannot be reached with the anon key the app ships.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Where data lives | .NET API → Supabase Postgres (EF Core + Npgsql) | Reuses the proven trust boundary and puts the ownership rule where xUnit can test it, with no new provider or bill. |
| Saved shape | Output + inputs + name | S-07 can show meaningful rows and S-04 can export a saved route without regenerating. |
| Naming | Server-derived auto-name, one-tap save | No keyboard over the map or modal built twice; a `name` column keeps rename cheap later. |
| Signed-out rider | "Sign in to save" → Account → "Back to your route" | Makes saving another reason to create an account without losing the route. |
| Duplicates | Idempotent via `UNIQUE (owner_id, client_route_id)` | The database, not UI state, guarantees a lost-response retry never duplicates. |
| Feedback | In-place button states + inline error/retry | Borrows nothing from S-07 and works the same on native and web. |
| Test layer | Hermetic always + real Postgres opt-in (`RIDEFORGE_TEST_DB`) | Constraint rules tested against real Postgres without making Docker a prerequisite. |
| Migrations | EF migrations applied by hand | Schema changes are deliberate and the runtime role never needs DDL rights. |
| Limits | Payload bounds, no per-rider cap | Stops garbage and oversized bodies; a cap would strand riders with no delete yet. |
| Schema placement | Custom `rideforge` schema + RLS + role-scoped policy | Supabase's Data API exposes `public` to the anon key the app ships. |
| DB credentials | Session pooler, dedicated `rideforge_api` role (SELECT/INSERT) | Railway needs IPv4; least privilege follows from hand-applied migrations. |

## Scope

**In scope:** EF Core + Npgsql, `rideforge.saved_routes` and its first migration, runtime role and
grants, `POST /saved-routes` (401/400/201/200/503), server-side validation and naming, hermetic and
opt-in Postgres tests, a §6 cookbook note, and on the client the result store carrying the inputs,
the save API and mutation, a shared Save action on both result screens, and the Account "Back to your
route" link.

**Out of scope:** Listing, reading, renaming, or deleting saved routes (S-07); per-rider caps or save
rate limits; FK to `auth.users`; migrations on startup; Docker/Testcontainers; a frontend test runner;
GPX from saved routes (S-04).

## Architecture / Approach

The app sends the full ride (geometry, stats, inputs, a client-minted route id) to an authenticated
`POST /saved-routes`. The API takes the owner from the verified token and never from the body,
validates, derives the name, and inserts through EF Core into a `jsonb`-backed row in the `rideforge`
schema, connecting as `rideforge_api` via Supabase's session pooler. A unique violation on
`(owner, client route id)` returns the existing row, so a save can be repeated safely.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Persistence foundation | EF Core, schema, migration, role, Railway connection string | Deploying the fail-fast check before the Railway variable exists crash-loops the API |
| 2. `POST /saved-routes` + tests | Idempotent owner-scoped save, hermetic + Postgres suites | A missing RLS policy for `rideforge_api` that owner-connected tests can't see |
| 3. Save from the app | Save action on both result screens, signed-out loop, per-rider Saved state | Rider B seeing rider A's "Saved ✓" on a shared device |

**Prerequisites:** S-05 and S-01 (done), Supabase dashboard access, Railway variable access, a local
Postgres for the opt-in suite. No new EAS dev build (no new native module).
**Estimated effort:** ~3 after-hours sessions, one per phase; phase 2 suits `/10x-tdd`.

## Open Risks & Assumptions

- Scripted saving could fill free-tier storage; bounded per request only.
- No FK to `auth.users`: deleting a user in the dashboard orphans their rows.
- The opt-in Postgres suite skips silently when the variable is unset and can rot.
- Forgetting a hand-applied migration surfaces as 500s on save.
- An unsaved route is lost if the app is killed during the sign-in detour.

## Success Criteria (Summary)

- A signed-in rider saves a route in one tap, and it is stored under their account exactly once.
- A signed-out rider is led to sign in and back to the same route, then saves it.
- Nobody else, including anyone holding the app's anon key, can read or claim a rider's saved route.
