---
project: RideForge
deployed_at: 2026-06-16
platform: Railway
environment: production
region: eu-west-amsterdam
status: live
url: https://rideforge-api-production.up.railway.app
---

# Deploy Plan — RideForge Backend API (Railway)

Approved and executed via Plan Mode on 2026-06-16.
Downstream milestone-planning skills should read this file as ground truth for
"what's already deployed and which secrets are wired."

## What is deployed

| Component | Location | Status |
|---|---|---|
| Backend API (ASP.NET Core / net10.0) | `api/` directory | ✅ deployed |
| Railway service | rideforge-api (EU West Amsterdam) | ✅ online |
| Health endpoint | `GET /health` → `{"status":"ok","service":"rideforge-api"}` | ✅ verified |
| Route-generation endpoint (own curviness algorithm + directions-API stitching) | not yet — PRD Open Question 2 resolved 2026-08-11 as hybrid; unblocked, pending implementation + directions-API pick | not deployed |
| Auth endpoints (FR-008) | not yet — nice-to-have | not deployed |

## Manual gates completed before deploy

- [x] Railway account created at railway.com
- [x] `railway login` completed (browser OAuth, mat.frydrych.92@gmail.com)

## Deploy steps executed

1. `dotnet new web --framework net10.0 -o api/` — project created
2. `Program.cs` — port bound from `$PORT` env var; `/health` endpoint added
3. `api/Dockerfile` — multi-stage Docker build (mcr.microsoft.com/dotnet/sdk:10.0 → aspnet:10.0)
4. `api/railway.toml` — `builder = "dockerfile"` (Nixpacks dropped: Railway ships only .NET 6 in its nixpkgs snapshot)
5. Root `.gitignore` updated — `api/bin/` and `api/obj/` excluded
6. `railway init` (from `api/`) — project named `rideforge-api`
7. `railway up --service rideforge-api` — first deploy
8. `railway domain --service rideforge-api` — domain provisioned

## Secrets wired

| Secret | Wired? | Notes |
|---|---|---|
| `NIXPACKS_DOTNET_VERSION` | pending | Set to `10.0.300` to match local SDK |
| Directions/map-matching API key (waypoint stitching) | not yet | Provider TBD (GraphHopper Directions / ORS / Mapbox / self-hosted OSRM). Curviness no longer sourced externally — any road-following directions API works |
| `SUPABASE_JWT_SECRET` | not yet | Blocked on FR-008 (nice-to-have) |

## Verification

```bash
railway logs          # "Now listening on http://+:<PORT>"
railway status        # service: active
curl https://<railway-url>/health
# {"status":"ok","service":"rideforge-api"}
```

Railway-generated URL: https://rideforge-api-production.up.railway.app

Verified 2026-06-16: `curl https://rideforge-api-production.up.railway.app/health` → `{"status":"ok","service":"rideforge-api"}`

## Known constraints

- No rollback CLI command — rollback via Railway dashboard → Deployments → Redeploy prior
- No SLA on Hobby plan ($5/month)
- App Sleeping must remain OFF — confirmed via `railway.toml` (no `auto_sleep` key = defaults to off)
- Single EU region: Amsterdam only (no Frankfurt fallback)
