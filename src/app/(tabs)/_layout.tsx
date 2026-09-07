import AppTabs from '@/components/app-tabs';

/**
 * Layout for the `(tabs)` route group — renders the native tab navigator. This group sits under
 * the root `<Stack>` (src/app/_layout.tsx), so non-tab routes like `/result` can be pushed over
 * the tabs. `(tabs)` is not part of the URL: `/` still maps to index, `/explore` to explore.
 */
export default function TabsLayout() {
  return <AppTabs />;
}
