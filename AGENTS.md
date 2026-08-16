# Repository Guidelines

RideForge is a motorcycle ride planner that generates curviness-aware routes from rider-specified parameters and exports GPX files. Built on Expo SDK 56 (React Native + TypeScript), with file-based routing via expo-router, targeting iOS, Android, and web from a single codebase.

## Hard rules

- **Expo docs version.** SDK is pinned in `@package.json`. Read the matching versioned docs at `https://docs.expo.dev/versions/v<sdk>/` before writing any code — v56 APIs differ from previous versions in Router, UI, and tab behaviour.
- **`app.json` is not yet renamed.** `name`, `slug`, and `scheme` still read `.bootstrap-scaffold`. Update all three to `rideforge` before any EAS build or store submission.
- **`NativeTabs` is an unstable API.** It comes from `expo-router/unstable-native-tabs`. Check the SDK changelog before updating or extending tab behaviour.
- **No manual `useMemo` / `useCallback`.** `reactCompiler: true` is enabled in `app.json`; the compiler handles memoization. Add them only if profiling shows a regression.

## Project structure

Screens live in `src/app/` (Expo Router file-based routing). Shared UI in `src/components/`; low-level primitives in `src/components/ui/`. Design tokens in `src/constants/theme.ts` — Colors, Fonts, Spacing, BottomTabInset, MaxContentWidth. Hooks in `src/hooks/`. Static assets outside `src/` under `assets/`.

Path aliases: `@/*` resolves to `src/`, `@/assets/*` to `assets/` — see `@tsconfig.json`.

Prefer a `.web.` sibling over `Platform.OS` branching when a clean split is possible.

## Commands

- `npm start` — Expo dev server (choose platform interactively)
- `npm run android` / `npm run ios` / `npm run web` — open directly on a platform
- `npm run lint` — run ESLint via `expo lint`
- `npm run reset-project` — move starter demo code to `app-example/`, leave a blank `app/`

No test runner is configured. See `@CLAUDE.md` for the Expo guide on adding one.

## Coding conventions

- **Colours:** use `useTheme()` from `@/hooks/use-theme`; never access `Colors` directly in a component.
- **Spacing:** use `Spacing.{half|one|two|three|four|five|six}` from `@/constants/theme` (4-px-unit scale); no raw numbers in `StyleSheet.create()` calls.
- **Screens:** default export, `StyleSheet.create()` at the bottom of the file. See `@src/app/explore.tsx` as reference shape.
- **Components:** named exports; props extend the native type. See `@src/components/themed-text.tsx` as reference shape.
- **No `any` or unsafe type assertions** (`as unknown as T`, `@ts-ignore`). `strict: true` is in `@tsconfig.json` and `expo lint` will catch violations.
