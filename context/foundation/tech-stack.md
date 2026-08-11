---
starter_id: expo
package_manager: npm
project_name: rideforge
hints:
  language_family: js
  team_size: solo
  deployment_target: appstore-via-eas
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
backend:
  language: csharp
  framework: aspnet-core
  runtime: net10
  deployment_target: railway
  region: eu-west-amsterdam
  build: dockerfile
  routing: hybrid-own-algorithm
---

## Why this stack

### Mobile app — TypeScript / Expo

A solo developer building a 3-week after-hours mobile MVP in JavaScript / TypeScript. The core product is a curviness-aware route generator with map preview and GPX download — all user-facing, no SSR or complex server-side logic needed beyond a routing API call. Expo is the recommended default for `(mobile, js)` and clears all four agent-friendly quality gates (TypeScript, convention-based Expo Router file structure, very popular in training data, excellent docs). Its `verified` bootstrapper confidence means scaffolding will run end-to-end without manual patching. Auth is flagged from FR-008 (nice-to-have save-and-revisit routes) but not MVP-blocking — Expo's managed workflow leaves the auth integration open while the core generation flow ships first. Deployment is `appstore-via-eas`, the starter's natural path to App Store + Play Store via EAS Build/Submit; CI runs on GitHub Actions with auto-deploy-on-merge, matching a single-developer workflow.

### Backend API — C# / ASP.NET Core on Railway

The backend is a thin ASP.NET Core Web API (net10.0) deployed on Railway (EU West, Amsterdam). Its primary responsibility at MVP is **route generation**: RideForge's own curviness/route-shaping algorithm runs server-side and produces candidate waypoints, then a swappable commodity directions / map-matching API only stitches those waypoints into a road-following route (see PRD Open Question 2, resolved 2026-08-11 as hybrid). The external directions-API key stays server-side rather than bundled in the mobile client; the provider (GraphHopper Directions / OpenRouteService / Mapbox / self-hosted OSRM) is not yet picked and is non-blocking, since curviness is no longer sourced externally. If FR-008 (save-and-revisit routes) ships, auth endpoint support will be added here alongside Supabase JWT verification middleware. C# / .NET 10 was chosen over Node.js for the backend: the developer has existing .NET familiarity, and ASP.NET Core's type system aligns well with GPX/GeoJSON data modelling. **Build note:** Railway's Nixpacks snapshot ships only .NET 6, so it cannot build a `net10.0` target — the backend deploys via a Dockerfile (`api/Dockerfile`, `mcr.microsoft.com/dotnet/sdk:10.0`) with `builder = "dockerfile"` in `api/railway.toml`. Platform selection rationale, the Dockerfile finding, and the risk register are in `context/foundation/infrastructure.md`.
