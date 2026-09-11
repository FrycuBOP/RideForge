-- One-time setup of the RideForge database identities in Supabase (S-06).
-- Run once, as postgres, in the Supabase SQL editor, BEFORE the first deploy that carries
-- migrations. Everything after this — schema objects, grants, RLS — arrives through EF migrations,
-- applied by Railway's pre-deploy step (api/migrate.sh) as rideforge_migrator.
--
-- Replace both placeholders with freshly generated passwords. They go into Railway variables only,
-- never into this file or any other file in the repo:
--   ConnectionStrings__RideForgeMigrations  -> Username=rideforge_migrator.<project-ref>
--   ConnectionStrings__RideForge            -> Username=rideforge_api.<project-ref>

-- Owns the rideforge schema and everything migrations create in it. Can reach nothing else in the
-- database — not auth.users, not public — so its password sitting in Railway is a bounded risk.
create role rideforge_migrator with login password '<migrator-password>';

-- What the API runs as: SELECT/INSERT on saved routes, granted by the migrations themselves.
-- (The first migration also creates this role when it is missing, so it applies to a fresh local
-- Postgres; here it already exists and that step is skipped.)
create role rideforge_api with login password '<api-password>';

-- Handing a schema to another role requires membership in it. Kept afterwards on purpose: it lets
-- postgres fix or roll back rideforge objects by hand from the SQL editor.
grant rideforge_migrator to postgres;

create schema rideforge authorization rideforge_migrator;
