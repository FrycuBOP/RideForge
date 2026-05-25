---
starter_id: dotnet
package_manager: dotnet
project_name: document-ailyzer
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: false
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: true
  has_background_jobs: true
---

## Why this stack

After-hours solo project building an insurance back-office document processing tool with a 3-week MVP timeline. Custom path taken to evaluate a two-layer dotnet API + React SPA architecture; the Q8 self-check surfaced a conventions gap (vite-react carries no baked-in routing or folder layout) and a can-judge-agent gap for the React layer, pointing back to the safer single-layer choice. Dotnet (ASP.NET Core WebAPI) is the recommended default for `(web-app, dotnet)` and clears all four agent-friendly gates: C# is typed by the language, ASP.NET Core is strongly convention-based, and the .NET ecosystem is well-represented in training data and docs. Azure App Service aligns with the Azure ecosystem already committed in the PRD. Auth flag is set (SSO via corporate identity provider); AI flag is set (document categorizer and claims extraction agents); background jobs flag is set (async pipeline). Payments and realtime are out of scope. React SPA frontend deferred to a later iteration once ASP.NET Core grounding is established. GitHub Actions with auto-deploy-on-merge.
