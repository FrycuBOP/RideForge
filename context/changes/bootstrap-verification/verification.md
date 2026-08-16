---
bootstrapped_at: 2026-06-09T20:11:49Z
starter_id: expo
starter_name: "Expo (React Native)"
project_name: rideforge
language_family: js
package_manager: npm
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "npm audit --json"
---

## Hand-off

```yaml
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
```

### Why this stack

A solo developer building a 3-week after-hours mobile MVP in JavaScript / TypeScript. The core product is a curviness-aware route generator with map preview and GPX download — all user-facing, no SSR or complex server-side logic needed beyond a routing API call. Expo is the recommended default for `(mobile, js)` and clears all four agent-friendly quality gates (TypeScript, convention-based Expo Router file structure, very popular in training data, excellent docs). Its `verified` bootstrapper confidence means scaffolding will run end-to-end without manual patching. Auth is flagged from FR-008 (nice-to-have save-and-revisit routes) but not MVP-blocking — Expo's managed workflow leaves the auth integration open while the core generation flow ships first. Deployment is `appstore-via-eas`, the starter's natural path to App Store + Play Store via EAS Build/Submit; CI runs on GitHub Actions with auto-deploy-on-merge, matching a single-developer workflow. The routing API dependency (GraphHopper / OpenRouteService — an open PRD question) will be wired as a plain network call, not a framework concern.

## Pre-scaffold verification

| Signal      | Value                                       | Severity | Notes                                 |
| ----------- | ------------------------------------------- | -------- | ------------------------------------- |
| npm package | create-expo-app v4.0.0 published 2026-05-15 | fresh    | resolved from cmd_template            |
| GitHub repo | not run                                     | n/a      | docs_url is https://docs.expo.dev (not a GitHub repo URL) |

## Scaffold log

**Resolved invocation**: `npx create-expo-app .bootstrap-scaffold --yes --template default`
**Strategy**: scaffold into a temp directory then move files up (subdir-then-move)
**Exit code**: 0
**Files moved**: 14 (`.vscode`, `assets`, `node_modules`, `scripts`, `src`, `.gitignore`, `AGENTS.md`, `app.json`, `CLAUDE.md`, `LICENSE`, `package-lock.json`, `package.json`, `README.md`, `tsconfig.json`)
**Conflicts (.scaffold siblings)**: `.claude` → `.claude.scaffold` (scaffold shipped a `settings.json` inside `.claude/`; cwd already contains the 10xDevs skills tree — existing wins)
**.gitignore handling**: moved silently (no .gitignore existed in cwd)
**.bootstrap-scaffold cleanup**: deleted

## Post-scaffold audit

**Tool**: `npm audit --json`
**Summary**: 0 CRITICAL, 0 HIGH, 11 MODERATE, 0 LOW
**Direct vs transitive**: 2 direct MODERATE of 11 total MODERATE

#### CRITICAL findings

None.

#### HIGH findings

None.

#### MODERATE findings

All 11 findings are advisory-level moderate from the `xcode` → `@expo/config-plugins` → `expo` chain, plus a `uuid` buffer bounds advisory. All are transitive except `expo` and `expo-splash-screen` which are direct. Root causes:

- **uuid < 11.1.1** — `GHSA-w5hq-g745-h8pq`: Missing buffer bounds check in v3/v5/v6 when `buf` is provided. CVSS 7.5 (npm severity: moderate). Pulled in via `xcode` → `@expo/config-plugins`. Fix requires `expo` major version bump to 46.0.21.
- **expo (direct)** — moderate via `@expo/cli`, `@expo/config`, `@expo/config-plugins`, `@expo/local-build-cache-provider`, `@expo/metro-config`. Fix: `expo@46.0.21` (major breaking change).
- **expo-splash-screen (direct)** — moderate via `@expo/config-plugins`. Fix: `expo-splash-screen@55.0.21`.
- **@expo/cli, @expo/config, @expo/config-plugins, @expo/inline-modules, @expo/local-build-cache-provider, @expo/metro-config, @expo/prebuild-config, xcode** — transitive moderate findings all tied to the same chain. Fix available only via `expo` major version bump.

These are starter template advisories — typical on fresh scaffolds where the starter pins a working-but-not-latest version. None are CRITICAL or HIGH; no action required to start building.

#### LOW / INFO findings

None.

## Hints recorded but not acted on

| Hint                    | Value               |
| ----------------------- | ------------------- |
| bootstrapper_confidence | verified            |
| quality_override        | false               |
| path_taken              | standard            |
| self_check_answers      | null                |
| team_size               | solo                |
| deployment_target       | appstore-via-eas    |
| ci_provider             | github-actions      |
| ci_default_flow         | auto-deploy-on-merge|
| has_auth                | true                |
| has_payments            | false               |
| has_realtime            | false               |
| has_ai                  | false               |
| has_background_jobs     | false               |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Review the `.claude.scaffold/` directory — it contains a `settings.json` from the Expo starter template. Compare against your existing `.claude/` and decide if you want to merge anything.
- The scaffold also ships its own `CLAUDE.md` (content: `@AGENTS.md`) and `AGENTS.md` (Expo versioned docs reminder). These are now in your project root. Review them — the bootstrapper chain will add richer agent context via a future skill.
- Address audit findings per your project's risk tolerance — the full breakdown is in this log. The 11 moderate findings are all tied to the `xcode`/`uuid` chain in Expo tooling; they are advisory and do not block development.
