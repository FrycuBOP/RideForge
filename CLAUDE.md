# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Expo docs version

**Read https://docs.expo.dev/versions/v56.0.0/ before writing any code.** This project pins Expo SDK 56; the docs for that exact version differ from the latest in places that matter (Router, UI, unstable APIs).

## Commands

```bash
npm start            # Start Expo dev server (choose platform interactively)
npm run android      # Start on Android emulator
npm run ios          # Start on iOS simulator (macOS only)
npm run web          # Start in browser
npm run lint         # ESLint via expo lint
npm run reset-project  # Move starter code to app-example/, create blank app/
```

No test runner is configured yet. To add one: https://docs.expo.dev/develop/unit-testing/

## Architecture

### File-based routing

All screens live in `src/app/`. Expo Router maps the file tree to routes:
- `src/app/_layout.tsx` — root layout; wraps the whole app in `ThemeProvider` + `AnimatedSplashOverlay`, then renders `AppTabs`
- `src/app/index.tsx` → `/` (Home tab)
- `src/app/explore.tsx` → `/explore` (Explore tab)

Adding a new screen = adding a new file under `src/app/`. Nested layouts follow expo-router conventions. `typedRoutes` is enabled in `app.json` — use typed `<Link>` and `router.push()` from `expo-router`.

### Path aliases

`@/*` resolves to `src/*`. `@/assets/*` resolves to `assets/*` (outside `src/`).

```ts
import { useTheme } from '@/hooks/use-theme';   // src/hooks/use-theme.ts
import icon from '@/assets/images/icon.png';    // assets/images/icon.png
```

### Theme system

Two layers:

1. **`src/constants/theme.ts`** — exports `Colors` (light/dark), `Fonts` (platform-selected system fonts), `Spacing` (4-unit scale: `half=2, one=4, two=8, three=16, four=24, five=32, six=64`), `BottomTabInset`, `MaxContentWidth`.
2. **`src/hooks/use-theme.ts`** — `useTheme()` returns the correct `Colors[scheme]` object. Use this hook in components rather than reading `Colors` directly.

Screens use `StyleSheet.create` with `Spacing` constants. The `ThemedView` and `ThemedText` components in `src/components/` consume `useTheme()` internally.

### Platform-specific files

Expo resolves `.web.tsx` / `.web.ts` over `.tsx` / `.ts` on web:
- `src/components/animated-icon.web.tsx` — web variant of `animated-icon.tsx`
- `src/components/app-tabs.web.tsx` — web variant of `app-tabs.tsx`
- `src/hooks/use-color-scheme.web.ts` — web variant of `use-color-scheme.ts`

When a component needs different behaviour on web, add a `.web.` sibling instead of branching on `Platform.OS` in the same file.

### Navigation tabs

`AppTabs` (`src/components/app-tabs.tsx`) uses `NativeTabs` from `expo-router/unstable-native-tabs` — this is an unstable API. Check the v56 changelog before upgrading or modifying tab behaviour.

### React compiler

`reactCompiler: true` is set in `app.json`. Avoid manual `useMemo`/`useCallback` unless profiling reveals a specific regression — the compiler handles most memoisation automatically.

## First things to update

`app.json` still has `name` and `slug` set to `.bootstrap-scaffold`. Update both to `rideforge` (and update `scheme` from `bootstrapscaffold` to `rideforge`) before publishing or configuring EAS.

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 2, Lesson 2

Turn one roadmap item into the first implementation cycle with the **change planning chain**:

```
/10x-roadmap -> /10x-new -> /10x-plan -> /10x-plan-review -> /10x-implement
```

`/10x-new`, `/10x-plan`, `/10x-plan-review`, and `/10x-implement` are the lesson focus. `/10x-frame` and `/10x-research` are not required rituals here; they are escalation paths introduced in the next lesson.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Change setup (lesson focus)** | |
| `/10x-new <change-id>` | You selected a roadmap item and need a stable change folder. Creates `context/changes/<change-id>/change.md` so planning, implementation, progress, commits, and later review all share one identity. Use AFTER roadmap selection, BEFORE `/10x-plan`. |
| **Planning (lesson focus)** | |
| `/10x-plan <change-id>` | You have a change folder and need a reviewable implementation plan. Reads roadmap context, foundation docs, codebase evidence, and any existing change notes; writes `plan.md` and `plan-brief.md` with phases, file contracts, success criteria, and `## Progress`. |
| **Plan readiness (lesson focus)** | |
| `/10x-plan-review <change-id>` | You have `plan.md` and need a light pre-code readiness check. Use it to catch missing end state, weak contracts, malformed progress, scope drift, or blind spots before code changes begin. |
| **Implementation (lesson focus)** | |
| `/10x-implement <change-id> phase <n>` | You have an approved plan and want to execute one phase with verification, manual gate, commit ritual, and SHA write-back to `## Progress`. |
| **Lifecycle closure** | |
| `/10x-archive <change-id>` | A change is merged or intentionally closed. Move it out of active `context/changes/` into archive state. |

### How the chain hands off

- `/10x-new` creates the durable change identity.
- `/10x-plan` turns that identity into an implementation contract.
- `/10x-plan-review` checks the plan before the agent mutates code.
- `/10x-implement` executes one planned phase, verifies, asks for manual confirmation when needed, commits, and records progress.

### Lesson boundaries

- Plan is the default router after roadmap selection. Start with `/10x-plan` unless the problem is unclear or external evidence is blocking.
- Do not run `/10x-frame + /10x-research` as ceremony for every change.
- Do not turn this lesson into a full end-to-end product build. A checkpoint with a planned and partially or fully implemented stream is valid.
- Code review of the implemented diff belongs to Lesson 3 via `/10x-impl-review`.
- Lifecycle closure via `/10x-archive` after a change is merged or intentionally closed.

### Paths used by this lesson

- `context/foundation/roadmap.md` - upstream roadmap
- `context/changes/<change-id>/change.md` - change identity
- `context/changes/<change-id>/plan.md` - implementation contract
- `context/changes/<change-id>/plan-brief.md` - compressed handoff
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
