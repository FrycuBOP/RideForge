---
bootstrapped_at: 2026-05-25T00:00:00Z
starter_id: dotnet
starter_name: .NET (ASP.NET Core webapi)
project_name: document-ailyzer
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

```yaml
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
```

### Why this stack

After-hours solo project building an insurance back-office document processing tool with a 3-week MVP timeline. Custom path taken to evaluate a two-layer dotnet API + React SPA architecture; the Q8 self-check surfaced a conventions gap (vite-react carries no baked-in routing or folder layout) and a can-judge-agent gap for the React layer, pointing back to the safer single-layer choice. Dotnet (ASP.NET Core WebAPI) is the recommended default for `(web-app, dotnet)` and clears all four agent-friendly gates: C# is typed by the language, ASP.NET Core is strongly convention-based, and the .NET ecosystem is well-represented in training data and docs. Azure App Service aligns with the Azure ecosystem already committed in the PRD. Auth flag is set (SSO via corporate identity provider); AI flag is set (document categorizer and claims extraction agents); background jobs flag is set (async pipeline). Payments and realtime are out of scope. React SPA frontend deferred to a later iteration once ASP.NET Core grounding is established. GitHub Actions with auto-deploy-on-merge.

## Pre-scaffold verification

| Signal      | Value    | Severity | Notes                                                              |
| ----------- | -------- | -------- | ------------------------------------------------------------------ |
| npm package | not run  | n/a      | language_family is dotnet, not js — npm check does not apply      |
| GitHub repo | not run  | n/a      | docs_url (learn.microsoft.com/aspnet/core) is not a GitHub URL    |

No recency signal available for this starter. The .NET 10.0.300 SDK installed locally is a strong proxy for a current toolchain.

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move (scaffold into a temp directory, then move files up)
**Exit code**: 0
**Files moved**: 6
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold
**.bootstrap-scaffold cleanup**: deleted

Files moved into cwd:

| File                              | Resolution     |
| --------------------------------- | -------------- |
| `.bootstrap-scaffold.csproj`      | moved silently |
| `.bootstrap-scaffold.http`        | moved silently |
| `appsettings.Development.json`    | moved silently |
| `appsettings.json`                | moved silently |
| `Program.cs`                      | moved silently |
| `Properties/launchSettings.json`  | moved silently |

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not distinguished by this tool in the output format used

#### CRITICAL findings

None.

#### HIGH findings

None.

#### MODERATE findings

None.

#### LOW / INFO findings

None.

Clean dependency tree at scaffold time. .NET 10 webapi template ships with minimal direct dependencies (Microsoft.AspNetCore.OpenApi is the only explicit reference in the generated .csproj).

## Hints recorded but not acted on

These hint values were read from the hand-off but bootstrapper v1 takes no automated action on them. Preserved here for the future M1L4 skill ("Memory Architecture") and for human review.

| Hint                    | Value                                                                                        |
| ----------------------- | -------------------------------------------------------------------------------------------- |
| bootstrapper_confidence | verified                                                                                     |
| quality_override        | false                                                                                        |
| path_taken              | custom                                                                                       |
| self_check_answers      | typed: true, from_official_starter: true, conventions: true, docs_current: true, can_judge_agent: false |
| team_size               | solo                                                                                         |
| deployment_target       | azure-app-service                                                                            |
| ci_provider             | github-actions                                                                               |
| ci_default_flow         | auto-deploy-on-merge                                                                         |
| has_auth                | true                                                                                         |
| has_payments            | false                                                                                        |
| has_realtime            | false                                                                                        |
| has_ai                  | true                                                                                         |
| has_background_jobs     | true                                                                                         |

Notable: `has_auth`, `has_ai`, and `has_background_jobs` are all `true`. These flags were selected during tech-stack selection and are preserved here for the M1L4 skill to act on when setting up agent context (CLAUDE.md, AGENTS.md). v1 bootstrapper does not modify the scaffold based on feature flags.

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep (none were created in this run).
- Address audit findings per your project's risk tolerance — the full breakdown is in this log (0 findings, clean tree).
- Note: the project's `.csproj` file is named `.bootstrap-scaffold.csproj` because the dotnet CLI used the temp directory name. You may want to rename it to match your intended project name (e.g., `DocumentAIlyzer.csproj`) and update the `<AssemblyName>` and `<RootNamespace>` in the file if needed.
