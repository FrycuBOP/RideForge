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

## 10xDevs AI Toolkit - Module 2, Lesson 4

Prepare for a harder implementation stream with the **research-backed planning chain**:

```
internal research (/10x-research) + external research (exa.ai, Context7) -> /10x-plan -> /10x-implement -> success
```

The lesson focus is distinguishing internal from external research and using evidence to back planning decisions.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Internal research (lesson focus)** | |
| `/10x-research <change-id>` | You need evidence from the existing codebase — patterns, conventions, integration points, or existing implementations. Runs parallel sub-agents over the repo and writes structured findings to `research.md`. |
| **External research (lesson focus)** | |
| exa.ai | You need AI-native web search for library comparisons, best practices, or ecosystem context that the codebase cannot answer. |
| Context7 (`resolve-library-id` → `get-library-docs`) | You need live, current documentation for a specific library or framework. Resolves a library ID first, then fetches relevant doc pages. |
| **Framing spare wheel** | |
| `/10x-frame <change-id>` | The plan won't converge, the plan doesn't deliver expected results, or persistent drift keeps breaking the implementation. Use as an escape hatch on a separate problem (demonstrated on Space Explorers example), not as pre-research ritual. |
| **Planning and execution** | |
| `/10x-plan <change-id>` / `/10x-implement <change-id> phase <n>` | Use the same planning and execution chain from Lesson 2, now with upstream research evidence feeding the plan. |

### Research discipline

- Internal research (`/10x-research`) answers "what does our codebase already do?" — patterns, schemas, conventions, integration points.
- External research (exa.ai, Context7) answers "what should we do?" — library capabilities, API docs, ecosystem best practices.
- Combine both as evidence-backed input to `/10x-plan`. A plan without research evidence on a non-trivial stream is a guess.
- Agent-friendly docs (`llms.txt`, markdown-for-agents, `/md` endpoints) are a quality signal for library selection — libraries that publish agent-readable docs integrate faster.

### `/10x-frame` as spare wheel

Three triggers for reaching for `/10x-frame`:
1. The plan won't converge — research keeps opening more questions instead of narrowing to a contract.
2. The plan doesn't deliver — implementation repeatedly fails to meet success criteria.
3. Persistent drift — the implementation keeps diverging from the plan in ways that suggest the problem was mis-framed.

Demonstrated on a Space Explorers example, not the SRS path. It is an escape hatch, not a mandatory step.

### Paths used by this lesson

- `context/changes/<change-id>/research.md` - internal research output
- `context/changes/<change-id>/frame.md` - framing output when needed
- `context/changes/<change-id>/plan.md` - evidence-backed implementation contract
- `context/foundation/lessons.md` - recurring rules and pitfalls

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
