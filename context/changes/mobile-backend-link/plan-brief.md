# Mobile ↔ backend link (F-01) — Plan Brief

> Full plan: `context/changes/mobile-backend-link/plan.md`

## What & Why

Wire the Expo app to the live Railway backend. It's a foundation with no user-facing feature —
its value is the reusable transport layer and the loading/error convention (FR-005) that S-01
(route generation) and every later backend-calling slice will inherit. Without it, each slice
would reinvent fetching, base-URL config, and error handling.

## Starting Point

The Plan screen already exists (origin/destination, curviness, location detection), but its
"Plan Route" button only `console.log`s — nothing calls the backend. There is no API layer,
no base-URL config, and no shared error model in `src/`. The backend is live and exposes only
`GET /health`.

## Desired End State

The app boots inside a TanStack Query provider. A typed `getHealth()` call routed through a shared
`src/api/` client reaches Railway, and a dev-only status indicator visibly goes loading → connected,
and shows a normalized error when the backend is unreachable. S-01 then adds `generateRoute()` beside
`getHealth()`, reusing the same client, error model, and provider.

## Key Decisions Made

| Decision            | Choice                                   | Why (1 sentence)                                              | Source |
| ------------------- | ---------------------------------------- | ------------------------------------------------------------ | ------ |
| Base URL config     | `EXPO_PUBLIC_API_URL` + prod fallback    | Expo SDK 56-native, works web+native, zero-config default    | Plan   |
| Loading/error model | TanStack Query (react-query)             | User choice; shared caching/retry/loading-error surface      | Plan   |
| Error shape         | Normalized `ApiError` union              | Stable contract for user messages + retry; timeout ↔ NFR-01  | Plan   |
| API module layout   | `src/api/` client + typed endpoint fns   | react-query owns state; client owns transport+errors+timeout | Plan   |
| F-01 verification   | `/health` round-trip, no generation wiring | Keeps foundation minimal; S-01 owns the real call          | Plan   |

## Scope

**In scope:** `src/api/` (config, client, `ApiError`, `getHealth`); `@tanstack/react-query` +
`QueryClientProvider`; `useHealthQuery`; a dev-only connectivity indicator; `.env.example`.

**Out of scope:** route generation / wiring the "Plan Route" button (S-01); any backend change;
auth; persistence; caching tuning; a test runner.

## Architecture / Approach

`src/api/` is the transport layer: `config.ts` (base URL), `errors.ts` (`ApiError` + `normalizeError`),
`client.ts` (`request<T>` with `AbortController` timeout + JSON parse), `health.ts` (`getHealth`).
TanStack Query sits on top: a shared `QueryClient`, a `QueryClientProvider` in `_layout.tsx`, and a
`useHealthQuery` hook whose `error` is `ApiError`. A `__DEV__`-gated pill in the Plan screen header
renders the query's loading/ok/error state as the runtime proof.

## Phases at a Glance

| Phase                                    | What it delivers                                   | Key risk                                             |
| ---------------------------------------- | -------------------------------------------------- | ---------------------------------------------------- |
| 1. API transport layer                   | `src/api/` client, `ApiError`, `getHealth`, config | Env-var inlining behavior; getting the error mapping right |
| 2. TanStack Query + connectivity proof   | Provider, `useHealthQuery`, dev status indicator   | react-query compatibility with RN 0.85 / React 19.2 / Expo 56 |

**Prerequisites:** none (F-02 is parallel; backend `/health` already live).
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- **react-query v5 compatibility** with RN 0.85 / React 19.2 / Expo 56 must be verified before relying
  on it; fallback is a lightweight `useApiRequest` hook if incompatible.
- `EXPO_PUBLIC_*` values are build-time inlined (public) — fine for a URL; switching dev/prod needs a
  dev-server restart with `--clear`.
- `reactCompiler: true` is enabled — assumed compatible with react-query hooks (verify on boot).

## Success Criteria (Summary)

- The app boots with the query provider and no redbox on web + one native target.
- The dev indicator reaches "connected" against live Railway, and shows a normalized error kind
  (network/timeout) against a bad URL.
- The existing Plan form and "Plan Route" button are unchanged.
