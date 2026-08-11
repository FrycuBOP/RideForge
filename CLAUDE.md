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

## 10xDevs AI Toolkit - Module 2, Lesson 1

Move from sprint-zero setup to project orchestration with the **roadmap chain**:

```
(Module 1 foundation docs) -> /10x-roadmap -> backlog-ready roadmap items
```

`/10x-roadmap` is the lesson focus. `/10x-new` is intentionally introduced in Module 2, Lesson 2, when a selected roadmap item becomes an implementation change folder.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Roadmap (lesson focus)** | |
| `/10x-roadmap` | You have `context/foundation/prd.md` and a scaffolded project baseline, and you need a vertical-first MVP roadmap. The skill reads the PRD, inspects the code baseline, uses available foundation docs such as `tech-stack.md`, `infrastructure.md`, and `deploy-plan.md`, then writes `context/foundation/roadmap.md`. Use it BEFORE creating per-change folders or implementation plans. |
| **Re-run upstream if needed** | |
| `/10x-shape` / `/10x-prd` / `/10x-tech-stack-selector` / `/10x-bootstrapper` / `/10x-agents-md` / `/10x-infra-research` | Bundled from Module 1 so foundation contracts can be fixed before roadmap sequencing. If roadmap generation exposes a PRD gap, repair the PRD before pretending the backlog is ready. |

### How the chain hands off

- `/10x-roadmap` bridges product and implementation. It does not choose frameworks, design schemas, or write a per-change implementation plan.
- The output is `context/foundation/roadmap.md`: ordered milestones, vertical slices, bounded foundations, dependencies, unknowns, risk, and backlog handoff fields.
- Roadmap items should receive stable human-readable identifiers in backlog tools. The actual `context/changes/<change-id>/` folder is created in Lesson 2 with `/10x-new`.

### Roadmap boundaries

- Default to vertical slices: user-visible outcomes that cross UI, data, business logic, and integrations.
- Horizontal work is allowed only as a bounded enabler that names the downstream vertical milestone it unlocks.
- Avoid orphan horizontal work such as "build the whole database", "build all API endpoints", or "design the whole UI" before the first user-visible flow.
- Roadmap is not a calendar estimate. Do not invent dates, story points, or sprint velocity unless the user explicitly asks for a separate planning artifact.

### Foundation paths used by this lesson

- `context/foundation/prd.md` - input
- `context/foundation/tech-stack.md` - optional input
- `context/foundation/infrastructure.md` - optional input
- `context/deployment/deploy-plan.md` - optional input
- `context/foundation/roadmap.md` - output
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
