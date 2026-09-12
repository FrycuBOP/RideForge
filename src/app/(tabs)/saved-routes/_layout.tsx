import { Stack } from 'expo-router';

/**
 * Stack inside the Saved tab: the list, with one ride pushed over it. Nesting it here rather than
 * at the root is what keeps `/saved-routes` and `/saved-routes/{id}` in one route subtree — declared
 * in two places they compete for the `saved-routes` segment and the list silently loses.
 *
 * The list draws its own title, so it takes no header; the detail takes one for the back button and
 * the ride's name. The tab bar stays visible on both, which is why the detail screen pads its
 * overlay for it.
 */
export default function SavedRoutesLayout() {
  return (
    <Stack>
      <Stack.Screen name="index" options={{ headerShown: false }} />
      <Stack.Screen name="[id]" options={{ title: 'Saved ride' }} />
    </Stack>
  );
}
