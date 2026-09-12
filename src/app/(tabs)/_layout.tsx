import { StyleSheet } from 'react-native';

import AppTabs from '@/components/app-tabs';
import { ThemedView } from '@/components/themed-view';
import { useSession } from '@/hooks/use-session';

/**
 * Layout for the `(tabs)` route group — renders the native tab navigator. This group sits under
 * the root `<Stack>` (src/app/_layout.tsx), so non-tab routes like `/result` can be pushed over
 * the tabs. `(tabs)` is not part of the URL: `/` still maps to index, `/saved-routes` to the
 * saved-routes list.
 *
 * The two guards below both exist for one reason: `AppTabs` hides the Saved trigger from a
 * signed-out rider, and changing a trigger set is not free on Android. react-native-screens queues
 * a pending selected-tab update, then flushes it from `TabsContainer.onAttachedToWindow` — which,
 * if that happens while the activity is resuming, commits a fragment transaction from inside the
 * fragment transaction already running and throws `FragmentManager is already executing
 * transactions`, taking the app down on launch.
 *
 * So the trigger set is settled *before* each navigator instance mounts, which is what the SDK 56
 * native-tabs docs ask for, rather than mutated underneath an attached one.
 */
export default function TabsLayout() {
  const { session, isRestoring } = useSession();

  // Restoring the persisted session is async, and it lands within a few frames of launch — right on
  // top of the container attaching. Holding the navigator back until the answer is in means the
  // first trigger set is also the final one. The splash overlay covers this.
  if (isRestoring) {
    return <ThemedView style={styles.placeholder} />;
  }

  // Signing in or out still changes the trigger set. Keying on it swaps in a whole new navigator
  // instead of mutating the attached one — and the docs note that hiding a tab remounts the
  // navigator and resets its state anyway, so this gives up nothing that was not already given up.
  return <AppTabs key={session === null ? 'signed-out' : 'signed-in'} />;
}

const styles = StyleSheet.create({
  placeholder: {
    flex: 1,
  },
});
