---
project: RideForge
researched_at: 2026-06-16
recommended_platform: Railway
runner_up: Render
context_type: mvp
tech_stack:
  language: TypeScript (mobile) / C# (backend API)
  framework: Expo React Native (mobile) / ASP.NET Core (backend API)
  runtime: Node.js via EAS (mobile) / .NET 8 (backend API)
---

## Recommendation

**Deploy the backend API on Railway.**

Railway scored 5/5 across all five agent-friendly criteria and is the strongest fit for a cost-sensitive solo developer with no existing platform familiarity. At $5/month (Hobby plan, includes $5 usage credit), it runs a persistent always-on process with zero cold starts, auto-detects ASP.NET Core projects via Nixpacks (`.csproj` detection), deploys to Amsterdam (EU West), and ships a GA official MCP server and `llms.txt` / `agents.md` docs that Claude Code can consume directly. The mobile app itself (Expo) continues to deploy via EAS Build/Submit to App Store and Play Store — Railway is strictly for the backend API layer (route-generation endpoint, and eventually auth endpoints if FR-008 ships).

> **Update 2026-08-11 — routing approach resolved as hybrid (PRD Open Question 2).** The curviness/route-shaping logic is now RideForge's **own algorithm** running in the backend; an external commodity directions / map-matching API only stitches the algorithm's waypoints into a road-following route. This keeps the backend **light** (no in-process routing graph, no OSM extract loaded into RAM), so the Railway Hobby fit and the .NET runtime choice both still hold. If a later iteration moves waypoint stitching in-process (self-hosted OSRM/Valhalla), revisit the RAM ceiling and re-run this research — that is explicitly out of scope for MVP.

**Note on platform elimination:** Cloudflare Workers, Vercel, and Netlify are hard-eliminated — none support a .NET runtime. Cloudflare Workers runs JS/TS/WASM only; Vercel and Netlify serverless functions are limited to Node.js, Python, Ruby, and Go runtimes with no .NET option. Any platform requiring a Docker-capable runtime or native .NET support remains eligible: Railway, Render, and Fly.io.

## Platform Comparison

### Scoring Matrix

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Effective cost (MVP) | .NET eligible |
|---|---|---|---|---|---|---|---|
| **Railway** | Pass | Pass | Pass | Pass | Pass (GA) | $5/mo | ✅ |
| **Render** | Partial | Pass | Partial | Partial | Pass (GA) | $7/mo | ✅ |
| **Fly.io** | Partial | Pass | Pass | Partial | Fail (experimental) | $4–6/mo | ✅ |
| Cloudflare Workers | — | — | — | — | — | — | ❌ eliminated |
| Vercel | — | — | — | — | — | — | ❌ eliminated |
| Netlify | — | — | — | — | — | — | ❌ eliminated |

**Criteria notes (eligible platforms):**

- **CLI-first:** Railway CLI (`railway up`, `railway logs`, `railway redeploy`) covers the full lifecycle. Render loses a point because rollback requires a dashboard click or REST API call — no native CLI rollback command. Fly.io loses a point for the same reason: rollback is `fly deploy -i <image-hash>` after manually locating the prior hash.
- **Managed/Serverless:** All three pass — none require OS patching, network config, or hardware provisioning.
- **Agent-readable docs:** Railway (`railway.com/llms.txt`, `railway.com/agents.md`) and Fly.io (`fly.io/llms.txt`) publish machine-readable docs. Render has no `llms.txt` but ships a Claude Code Skills package (`render skills install`) as a functional equivalent.
- **Stable deploy API:** Railway's `railway up` and `railway redeploy` are deterministic. Fly.io rollback requires a manual image hash lookup (`fly releases --image` then `fly deploy -i <hash>`). Render rollback is a `POST /deploys/{id}/rollback` REST call or dashboard action.
- **MCP/Integration:** Railway MCP server (GA, Aug 2025) and Render MCP server (GA, Aug 2025) are both production-stable. Fly.io's `flymcp` is experimental (handful of commits, no formal release).

### Shortlisted Platforms

#### 1. Railway (Recommended)

Railway is a PaaS with a genuine always-on persistent process — no invocation timeouts, no forced cold starts. ASP.NET Core is auto-detected by Nixpacks via `.csproj` file presence: it runs `dotnet publish` at build time and `dotnet <AppName>.dll` at start. Deployment is `railway up`; live log tailing is `railway logs`. The EU West Metal region in Amsterdam is GA. The official Railway MCP server (GA, Aug 2025) exposes deploy, variable, and log tools directly to Claude Code. `railway.com/llms.txt` and `railway.com/agents.md` give the agent structured access to platform docs. Effective cost: $5/month Hobby (includes $5 usage credit, which covers a low-QPS proxy comfortably).

#### 2. Render

Render runs persistent Web Services with no invocation timeout. ASP.NET Core deploys via Docker — Render does not auto-detect .NET projects; you must supply a Dockerfile and configure build/start commands manually. Frankfurt EU region is GA. The official Render MCP server (GA, Aug 2025) exposes 20+ tools for Claude Code. The free tier (750 instance-hours/month) spins down after 15 minutes of inactivity, causing 30–60 second cold starts — a significant UX problem for a mobile app API. The **$7/month Starter plan** eliminates this. Rollback is a REST API call or dashboard action, not a CLI command.

#### 3. Fly.io

Fly.io is a Docker-first PaaS that runs persistent micro-VMs (Firecracker). ASP.NET Core is well-supported via Docker — `fly launch` auto-generates a Dockerfile from the project. Frankfurt (`fra`) region is GA. No free tier for new accounts (trial credit only; credit card required upfront); realistic cost for a minimal always-on .NET service is **$4–6/month** (1 `shared-cpu-1x` VM + IPv4 + 1 GB volume). The `flymcp` integration is experimental. Rollback requires manually identifying a prior image hash (`fly releases --image`, then `fly deploy -i <hash>`). Stronger than Render on docs (llms.txt exists), weaker on MCP story.

## Anti-Bias Cross-Check: Railway

### Devil's Advocate — Weaknesses

1. **Single EU region (Amsterdam only).** No Frankfurt, no Paris, no multi-EU option. If the routing API provider is co-located in Frankfurt or GDPR enforcement requires per-country data residency, Amsterdam is the only choice with no fallback.
2. **Nixpacks .NET detection may not match your SDK version.** Nixpacks auto-selects a .NET SDK version based on the `TargetFramework` in `.csproj`. If the selected SDK lags behind your local version, build output could differ from local dev. Always pin the version explicitly via the `NIXPACKS_DOTNET_VERSION` env var on Railway.
3. **App Sleeping is opt-in but prominently surfaced in the UI.** The toggle appears as a cost-saving option. Enabling it causes 30-second cold starts for mobile users — a .NET process has a non-trivial startup time (~1–3s) even once the container is warm. Must be confirmed OFF.
4. **No SLA on Hobby.** Railway's Hobby plan carries no uptime guarantee. For MVP validation this is acceptable; for post-MVP with paying users, upgrade to Pro or move platforms.
5. **`railway redeploy` only redeploys the current image.** Rolling back to a specific prior deployment requires locating the target deploy in the Railway dashboard and triggering a manual redeploy — the CLI does not expose this directly.

### Pre-Mortem — How This Could Fail

The RideForge backend (ASP.NET Core) deployed to Railway in Day 1 of Week 1. Nixpacks detected the `.csproj`, compiled with `dotnet publish`, started cleanly. But by Week 2, three things compounded. First, the developer enabled App Sleeping during a quiet weekend to save money — Monday morning users got 30-second API timeouts because the .NET process was cold, plus Railway's container startup added another 3 seconds on top. The incident took hours to diagnose because the sleep event wasn't surfaced in `railway logs`. Second, the Nixpacks-selected .NET SDK version (8.0.x) didn't match the local dev environment (8.0.x+patch), and a `DateOnly` serialization behavior differed between SDK patches, causing subtle GPX date formatting bugs in production only. Third, FR-008 (auth/saved routes with Supabase) was declared in-scope during Week 3. Wiring Supabase JWT verification into the ASP.NET Core middleware required `SUPABASE_JWT_SECRET` to live in both Railway's variable vault and the local `appsettings.Development.json` — two sources of truth. A copy-paste error left a stale secret in Railway, causing 401s in production for 6 hours before the mismatch was found.

### Unknown Unknowns

- **Nixpacks on Railway ships only .NET 6 (confirmed 2026-06-16).** Nixpacks v1.41.0 uses a nixpkgs snapshot that contains only `dotnet-sdk-6.0.413`. Targeting `net8.0` or `net10.0` fails with `NETSDK1045`. **Fix: use a Dockerfile** with `mcr.microsoft.com/dotnet/sdk:10.0` and set `builder = "dockerfile"` in `railway.toml`. The `NIXPACKS_DOTNET_VERSION` env var does not help — the nixpkgs snapshot is the constraint, not a version flag.
- **App Sleeping trigger mechanics are counterintuitive.** Sleep is triggered by "no outbound packet for 10 minutes." A background timer (e.g., `IHostedService` pinging a health endpoint or connection pool heartbeat) will prevent sleeping even when no user requests arrive — useful to know if you want to stay warm without enabling always-on.
- **The Railway MCP server has no delete tools by design.** This is a good safety guardrail, but it means Claude Code cannot teardown or delete Railway services via MCP. Destructive operations (remove service, delete environment) require the CLI or dashboard.
- **Usage-based billing doesn't alert by default.** If you add a Railway Postgres or Redis service for testing and forget to remove it, it accrues usage silently against the $5 monthly credit. Set a billing alert in Railway Account Settings on Day 1.
- **ASP.NET Core's default port on Railway.** Railway injects a `PORT` environment variable. ASP.NET Core listens on port 8080 by default in .NET 8 containers. Make sure your `Program.cs` uses `builder.WebApplication.Run()` without a hardcoded port, or explicitly reads `$PORT` — otherwise Railway's reverse proxy can't reach your app.

## Operational Story

- **Preview deploys:** Railway supports multiple environments per project. Create a `staging` environment (`railway environment staging`) and deploy branches there via `railway up --environment staging`. No automatic PR-preview URL generation out of the box — previews are manual environment deploys. Staging endpoints are public by default; protect with an `INTERNAL_SECRET` header check in the ASP.NET Core middleware if needed.
- **Secrets:** Environment variables live in Railway's encrypted vault, set via `railway variables set KEY=VALUE` or in the Railway dashboard under Variables → the specific environment. In ASP.NET Core, read them via `Environment.GetEnvironmentVariable("KEY")` or wire them into `IConfiguration` (Railway injects them as environment variables, which `IConfiguration` picks up automatically). Rotate a secret by running `railway variables set KEY=NEW_VALUE`; the service restarts automatically. Never commit secrets to `appsettings.json` or `railway.toml`.
- **Rollback:** Railway has no `railway rollback` CLI command. To revert: open the Railway dashboard → select the service → Deployments tab → find the prior successful deploy → click "Redeploy." Keep deploys small so rollback is rarely needed; each `railway up` from a clean `git push` is a discrete rollback point.
- **Approval:** `railway up` deploys immediately without a manual approval gate. Any action that modifies production variables or triggers a redeploy counts as a production mutation — these should require human initiation. The Railway MCP server deliberately excludes delete tools; treat any MCP-triggered variable change as requiring human review before execution.
- **Logs:** `railway logs` streams live logs to the terminal. Add `--service <name>` or `--environment <env>` to filter. Use `railway logs --since 30m` for a historical window. The Railway dashboard provides a log viewer with search. There is no log export or retention SLA on the Hobby plan — pipe important logs to an external sink (Axiom, Better Stack) if you need durable audit history. ASP.NET Core's `ILogger` output goes to stdout and is captured by `railway logs` without any additional configuration.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| App Sleeping accidentally enabled, causing cold starts compounded by .NET startup time | Devil's advocate | M | H | Confirm `auto_sleep = false` in `railway.toml` on Day 1; add to deploy checklist |
| Nixpacks on Railway ships only .NET 6 — cannot build net8.0 or net10.0 targets | Unknown unknowns (confirmed in first deploy 2026-06-16) | — | — | **Resolved**: switched to `builder = "dockerfile"` in `railway.toml` with `mcr.microsoft.com/dotnet/sdk:10.0` |
| JWT secret mismatch between Railway variables and Supabase when wiring FR-008 | Pre-mortem | H | M | Maintain a single secrets checklist; test auth roundtrip in staging before pushing to production |
| ASP.NET Core binds to hardcoded port instead of Railway's injected `$PORT`, causing 502s | Unknown unknowns | M | H | Ensure `Program.cs` does not hardcode a port; .NET 8 default (8080) matches Railway's expectation but verify |
| Billing surprise from forgotten Railway Postgres/Redis test services | Unknown unknowns | M | L | Enable billing alerts in Railway Account Settings before adding any database service |
| `railway redeploy` targeting wrong image during a time-pressured rollback | Devil's advocate | L | M | Document the rollback procedure in `context/deployment/deploy-plan.md` before going live |
| Directions/map-matching API (waypoint stitching) co-located in Frankfurt while Railway is Amsterdam, adding latency | Pre-mortem | L | L | Lower stakes under hybrid — only the stitching call is external, not the whole route computation. Acceptable at MVP scale; re-evaluate if p95 latency approaches the 30s NFR |
| Own curviness algorithm + directions-API round-trip can't meet the 30s generation NFR for large ride durations | Research finding (hybrid decision, 2026-08-11) | M | H | Bound candidate-waypoint count; cache/limit stitching calls; test worst-case (long touristic ride) in staging before shipping FR-005 |

## Getting Started

1. **Install Railway CLI and log in:**
   ```bash
   npm install -g @railway/cli
   railway login
   ```

2. **Create a new ASP.NET Core Web API project (if not already done):**
   ```bash
   dotnet new webapi -n RideForgeApi --framework net8.0
   cd RideForgeApi
   ```

3. **Initialize a Railway project and pin Nixpacks with explicit .NET version:**
   ```bash
   railway init
   # Select "Empty project", name it "rideforge-api"
   ```
   Create `railway.toml` in the project root:
   ```toml
   [build]
   builder = "nixpacks"

   [deploy]
   startCommand = "dotnet RideForgeApi.dll"
   ```
   And add this Railway variable immediately after init:
   ```bash
   railway variables set NIXPACKS_DOTNET_VERSION=8.0.100
   ```

4. **Verify port binding in `Program.cs`** — do not hardcode a port:
   ```csharp
   // Correct — Railway injects PORT automatically
   var app = builder.Build();
   app.Run();

   // Wrong — hardcoded port will conflict with Railway's proxy
   // app.Run("http://0.0.0.0:5000");
   ```

5. **Deploy and set routing API credentials:**
   ```bash
   railway up
   railway logs  # confirm: no "sleeping" events, app responds
   railway variables set GRAPHHOPPER_API_KEY=your_key
   # or
   railway variables set ORS_API_KEY=your_key
   ```

## Out of Scope

The following were not evaluated in this research:
- EAS Build / Submit configuration for the Expo mobile app (handled separately by the `appstore-via-eas` deployment target in `tech-stack.md`)
- Docker image configuration (Railway uses Nixpacks; Dockerfile is an override option if Nixpacks hits an edge case)
- CI/CD pipeline setup (GitHub Actions wiring for `railway up` on merge)
- Production-scale architecture (multi-region, HA, DR)
- Auth provider selection (Supabase is the assumed external provider for FR-008; not evaluated here)
