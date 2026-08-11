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
  runtime: net8
  deployment_target: railway
  region: eu-west-amsterdam
---

## Why this stack

### Mobile app — TypeScript / Expo

A solo developer building a 3-week after-hours mobile MVP in JavaScript / TypeScript. The core product is a curviness-aware route generator with map preview and GPX download — all user-facing, no SSR or complex server-side logic needed beyond a routing API call. Expo is the recommended default for `(mobile, js)` and clears all four agent-friendly quality gates (TypeScript, convention-based Expo Router file structure, very popular in training data, excellent docs). Its `verified` bootstrapper confidence means scaffolding will run end-to-end without manual patching. Auth is flagged from FR-008 (nice-to-have save-and-revisit routes) but not MVP-blocking — Expo's managed workflow leaves the auth integration open while the core generation flow ships first. Deployment is `appstore-via-eas`, the starter's natural path to App Store + Play Store via EAS Build/Submit; CI runs on GitHub Actions with auto-deploy-on-merge, matching a single-developer workflow.

### Backend API — C# / ASP.NET Core on Railway

The backend is a thin ASP.NET Core Web API (net8.0) deployed on Railway (EU West, Amsterdam). Its primary responsibility at MVP is proxying routing API calls to GraphHopper or OpenRouteService — keeping the API key server-side rather than bundled in the mobile client. If FR-008 (save-and-revisit routes) ships, auth endpoint support will be added here alongside Supabase JWT verification middleware. C# / .NET 8 was chosen over Node.js for the backend: the developer has existing .NET familiarity, ASP.NET Core's type system aligns well with GPX/GeoJSON data modelling, and Railway's Nixpacks builder auto-detects `.csproj` files and handles `dotnet publish` without a Dockerfile. Platform selection rationale and risk register are in `context/foundation/infrastructure.md`.
