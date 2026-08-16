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

## 10xDevs AI Toolkit - Module 2, Lesson 3

Review AI-generated code before merge with the **implementation review chain**:

```
/10x-implement -> /10x-impl-review -> triage -> (/10x-lesson | fix | skip | disagree)
```

`/10x-impl-review` is the lesson focus. Review is a quality gate, not an instruction to fix every finding.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Code review (lesson focus)** | |
| `/10x-impl-review <change-id>` | You have implemented code and want a structured review before merge. The skill checks plan adherence, scope discipline, safety and quality, architecture, pattern consistency, and success criteria, then presents findings for triage. |
| **Recurring lesson outcome** | |
| `/10x-lesson` | A finding reveals a recurring project rule or agent failure pattern. Record it in `context/foundation/lessons.md` instead of treating it as a one-off note. |

### Triage discipline

- Severity says how bad the finding is. Impact says how much the decision matters now.
- Valid outcomes: fix now, fix differently, skip, accept as risk, record as recurring rule (`/10x-lesson`), disagree.
- Fix critical findings. Do not burn hours on low-impact observations just because the agent found them.
- Conscious skipping of low-impact findings is a valid review outcome, not negligence.
- If you disagree with a finding, record why. Wrong agent reasoning is also signal.

### Review boundaries

- This lesson reviews implemented code. It does not create the plan, execute new phases, or teach CI review.
- Testing strategy and quality gates are introduced in Module 3.
- Do not use `/10x-contract` as a triage outcome in this lesson.

### Paths used by this lesson

- `context/changes/<change-id>/plan.md` - expected implementation contract
- `context/changes/<change-id>/reviews/` - review output
- `context/foundation/lessons.md` - recurring lessons

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
