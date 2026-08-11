# Mobile ↔ backend link (F-01) Implementation Plan

## Overview

Establish the connection between the Expo (React Native) app and the Railway-hosted
ASP.NET Core backend. This is a foundation: it delivers no user-facing product feature,
but it creates the reusable transport layer and the loading/error convention (FR-005)
that S-01 (`generate-route-preview`) and every later backend-calling slice depend on.

Concretely: a typed `fetch` client with an environment-based base URL and a normalized
`ApiError`, plus a TanStack Query provider and a `GET /health` round-trip surfaced in-app
to prove the device actually reaches Railway.

## Current State Analysis

- **No network/API layer exists** in `src/`. The only outbound HTTP is an inline `fetch`
  to Nominatim inside the Plan screen (`src/app/index.tsx:71`) — there is no shared client,
  no base-URL config, no typed request/response, no error normalization.
- **The Plan screen already exists and is real** (`src/app/index.tsx`): origin/destination
  inputs, a curviness selector, and `expo-location` detection. Its "Plan Route" button only
  `console.log`s (`src/app/index.tsx:205`) — nothing calls the backend yet.
- **Backend exposes only `GET /health`** (`api/Program.cs:8`) → `{"status":"ok","service":"rideforge-api"}`.
  It is live at `https://rideforge-api-production.up.railway.app` (see `context/deployment/deploy-plan.md`).
- **No config mechanism**: `app.json` has no `extra` block; `expo-constants` is a dependency
  but unused for config. No `.env` in the repo.
- **Conventions**: path alias `@/*` → `src/*` (and `@/assets/*`); the project pins **Expo SDK 56**
  (CLAUDE.md mandates reading the v56 docs before writing code); `reactCompiler: true` and
  `typedRoutes: true` are set in `app.json`. The existing `locating` state in `index.tsx` is the
  loading-state pattern to stay consistent with.

## Desired End State

The app boots with a `QueryClientProvider` in place. A typed `getHealth()` call routed through
a shared `src/api/` client succeeds against the live Railway URL, and a minimal in-app status
indicator (dev-only) visibly transitions loading → connected, and shows a normalized error
(`network`/`timeout`) when the backend is unreachable. S-01 can then add `generateRoute()` next
to `getHealth()` and a `useGenerateRoute()` mutation reusing the exact same client, error model,
and query provider.

### Key Discoveries:

- Base URL must work identically on web and native; the Railway URL is public, so an
  `EXPO_PUBLIC_*` env var (build-time inlined) is the idiomatic Expo SDK 56 mechanism.
- The 30s generation budget (NFR-01) belongs to the client's timeout contract — the client
  should expose a configurable timeout via `AbortController`, defaulting generously, while the
  health check uses a short timeout.
- No test runner is configured (CLAUDE.md). Automated verification is limited to type-checking
  and lint; runtime proof is manual (in-app indicator).

## What We're NOT Doing

- **No route-generation call and no wiring of the "Plan Route" button** — that is S-01. The button
  keeps its current `console.log` behavior.
- **No backend changes.** F-01 verifies against the existing `/health`; no new endpoints, no echo/stub.
- **No auth** (S-05), no persistence (S-06).
- **No caching/offline strategy tuning, no query persistence.** Default TanStack Query behavior only.
- **No test-runner setup.** None exists; adding one is out of scope for this foundation.
- **No production UI surface for the status indicator** — it is gated behind `__DEV__`.

## Implementation Approach

Two phases. Phase 1 builds the transport layer (`src/api/`) in isolation — config, client,
error model, and the typed `getHealth()` — so it type-checks and lints on its own. Phase 2
integrates TanStack Query (the chosen loading/error convention), wraps the app in a provider,
and adds a dev-only connectivity indicator that exercises the loading/ok/error states end-to-end
against live Railway.

## Critical Implementation Details

- **Timing & lifecycle** — the health `useQuery` fires on mount. Gate the indicator component
  behind `__DEV__` so the query (and its network call) never mounts in production builds; this
  keeps the foundation zero-overhead for real users.
- **Performance constraints** — `EXPO_PUBLIC_*` values are inlined at build time. Switching
  `EXPO_PUBLIC_API_URL` (e.g. dev → localhost) requires restarting the dev server with cache
  clear (`npx expo start --clear`); document this so it isn't mistaken for a bug.

## Phase 1: API transport layer

### Overview

Create `src/api/` — the environment-based base URL, the normalized `ApiError`, the base `fetch`
client with timeout, and the typed `getHealth()` endpoint function. No React, no UI.

### Changes Required:

#### 1. Base URL config

**File**: `src/api/config.ts`

**Intent**: Resolve the backend base URL once, from `EXPO_PUBLIC_API_URL`, falling back to the
live Railway production URL so the app works with zero config.

**Contract**: Export `API_BASE_URL: string` = `process.env.EXPO_PUBLIC_API_URL ?? 'https://rideforge-api-production.up.railway.app'`. Also export a `DEFAULT_TIMEOUT_MS` constant.

#### 2. Normalized error model

**File**: `src/api/errors.ts`

**Intent**: Give the whole app one machine-distinguishable error type so screens can map failures
to user-facing messages and retry decisions.

**Contract**: `ApiError` (class extending `Error`) with `kind: 'network' | 'timeout' | 'http' | 'parse'`
and optional `status?: number`. Provide a `normalizeError(e: unknown): ApiError` helper mapping
`AbortError` → `timeout`, network `TypeError` → `network`, non-`ok` responses → `http` (+status),
JSON parse failure → `parse`.

#### 3. Base fetch client

**File**: `src/api/client.ts`

**Intent**: One place that owns transport: build the URL, apply an `AbortController` timeout, parse
JSON, and throw a normalized `ApiError` on any failure.

**Contract**: `request<T>(path: string, options?: { method?; body?; timeoutMs?; signal? }): Promise<T>`.
Wraps `fetch(API_BASE_URL + path, …)`; on `!res.ok` throws `ApiError('http', status)`; on abort throws
`ApiError('timeout')`; on JSON failure throws `ApiError('parse')`. The `timeoutMs` defaults to
`DEFAULT_TIMEOUT_MS`.

#### 4. Health endpoint function + barrel

**File**: `src/api/health.ts`, `src/api/index.ts`

**Intent**: Provide the first typed endpoint and a single import surface for the module.

**Contract**: `type HealthResponse = { status: string; service: string }`; `getHealth(): Promise<HealthResponse>`
= `request<HealthResponse>('/health', { timeoutMs: <short, e.g. 8000> })`. `src/api/index.ts` re-exports
`request`, `getHealth`, `ApiError`, types, and `API_BASE_URL`.

#### 5. Dev env example

**File**: `.env.example`

**Intent**: Document the one env var without committing a real `.env`.

**Contract**: A single line `EXPO_PUBLIC_API_URL=https://rideforge-api-production.up.railway.app` with a
comment noting the prod fallback and the `--clear` restart requirement. Confirm `.env` is gitignored
(add it if not).

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npx tsc --noEmit`
- Linting passes: `npm run lint`

#### Manual Verification:

- `API_BASE_URL` resolves to the Railway URL with no `.env`, and to the overridden value when
  `EXPO_PUBLIC_API_URL` is set (verify via a temporary log or REPL).
- Live round-trip proof is deferred to Phase 2 (Phase 1 has no runtime surface).

**Implementation Note**: After completing this phase and all automated verification passes, pause for
manual confirmation before proceeding to Phase 2.

---

## Phase 2: TanStack Query integration + connectivity proof

### Overview

Add TanStack Query as the loading/error convention, wrap the app in a `QueryClientProvider`, expose a
`useHealthQuery` hook, and render a dev-only status indicator that visibly exercises loading/ok/error
against live Railway.

### Changes Required:

#### 1. Dependency

**File**: `package.json`

**Intent**: Add the chosen data-fetching library.

**Contract**: Add `@tanstack/react-query` (v5.x — the React 19-compatible line). Install and confirm it
resolves against React 19.2 / RN 0.85 / Expo 56 (see Open Risks).

#### 2. Query client

**File**: `src/api/query-client.ts`

**Intent**: A single shared `QueryClient` with MVP-appropriate defaults.

**Contract**: Export a configured `QueryClient` (e.g. `retry: 1`, a modest `staleTime`). No persistence.

#### 3. Provider wiring

**File**: `src/app/_layout.tsx`

**Intent**: Make queries available app-wide.

**Contract**: Wrap the existing tree (`AnimatedSplashOverlay` + `AppTabs`, currently inside `ThemeProvider`)
in `<QueryClientProvider client={queryClient}>`. No behavior change to theming/splash.

#### 4. Health hook

**File**: `src/hooks/use-health-query.ts`

**Intent**: The first query hook; the template S-01 copies for generation.

**Contract**: `useHealthQuery()` = `useQuery({ queryKey: ['health'], queryFn: getHealth })`. Its `error`
is typed as `ApiError`.

#### 5. Dev-only connectivity indicator

**File**: `src/components/backend-status.tsx`, consumed in `src/app/index.tsx`

**Intent**: Prove the pipe and demonstrate the FR-005 loading/error convention visibly.

**Contract**: A compact pill rendered only when `__DEV__` is true, placed in the Plan screen header.
Consumes `useHealthQuery()` and renders three states: pending → "checking…", success → "connected",
error → the `ApiError.kind` (e.g. "offline"/"timeout"). Uses `useTheme()` colors. Does not alter the
existing form or the "Plan Route" button.

### Success Criteria:

#### Automated Verification:

- Dependency installs cleanly: `npm install`
- `@tanstack/react-query` version present and single-instance: `npm ls @tanstack/react-query`
- Type checking passes: `npx tsc --noEmit`
- Linting passes: `npm run lint`

#### Manual Verification:

- App boots with no redbox on web (`npm run web`) and on a device/emulator (`npm run android`/`ios`).
- With the live backend reachable, the dev indicator transitions "checking…" → "connected".
- Pointing `EXPO_PUBLIC_API_URL` at an unreachable host (restart with `--clear`) shows the error state
  with `kind: network` (bad host) or `kind: timeout` (unroutable host after the timeout).
- The Plan screen form and "Plan Route" button behave exactly as before (no regression).

**Implementation Note**: After automated verification passes, pause for manual confirmation that the
connected/offline states were observed on a real client before considering F-01 done.

---

## Testing Strategy

### Unit Tests:

- None automated (no test runner configured). If a runner is later added, first targets:
  `normalizeError` mapping (abort→timeout, network→network, !ok→http+status, bad JSON→parse) and
  `getHealth()` shape.

### Integration Tests:

- Manual only (see below) — an end-to-end device→Railway round-trip.

### Manual Testing Steps:

1. Run `npm run web` (and one native target); confirm no redbox and the dev status pill appears.
2. Observe the pill go "checking…" → "connected" against live Railway.
3. Set `EXPO_PUBLIC_API_URL` to a bad URL, restart with `npx expo start --clear`; confirm the pill
   shows a normalized error kind.
4. Confirm the Plan form + button are unchanged.

## Performance Considerations

The health query is dev-only (`__DEV__`) so production users incur no extra network call. The client's
timeout is configurable so S-01 can align the generation call with the 30s NFR-01 budget without
changing the health check's short timeout.

## Migration Notes

None — additive only. No existing behavior changes; the inline Nominatim `fetch` in `index.tsx` is left
as-is (S-01/later cleanup may route it through the client, but that is out of scope here).

## References

- Roadmap item: `context/foundation/roadmap.md` → F-01 (`mobile-backend-link`)
- Backend + deploy facts: `context/deployment/deploy-plan.md`, `api/Program.cs:8`
- Existing Plan screen / fetch pattern: `src/app/index.tsx:71`, `src/app/index.tsx:205`
- Stack decisions: `context/foundation/tech-stack.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: API transport layer

#### Automated

- [x] 1.1 Type checking passes: `npx tsc --noEmit` — f541051
- [x] 1.2 Linting passes: `npm run lint` — f541051

#### Manual

- [x] 1.3 `API_BASE_URL` resolves from fallback and from `EXPO_PUBLIC_API_URL` override

### Phase 2: TanStack Query integration + connectivity proof

#### Automated

- [x] 2.1 Dependency installs cleanly: `npm install`
- [x] 2.2 Single `@tanstack/react-query` instance: `npm ls @tanstack/react-query`
- [x] 2.3 Type checking passes: `npx tsc --noEmit`
- [x] 2.4 Linting passes: `npm run lint`

#### Manual

- [x] 2.5 App boots with no redbox (web + one native target)
- [x] 2.6 Dev indicator transitions "checking…" → "connected" against live Railway
- [x] 2.7 Error state shows normalized `kind` (network/timeout) against a bad URL
- [x] 2.8 Plan form + "Plan Route" button unchanged (no regression)
