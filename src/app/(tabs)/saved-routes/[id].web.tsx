import { StyleSheet } from 'react-native';

import { Link, Stack, useLocalSearchParams } from 'expo-router';

import { RideStats } from '@/components/ride-stats';
import { RouteMap } from '@/components/route-map';
import { SavedRouteLoader } from '@/components/saved-route-loader';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { MaxContentWidth, Spacing } from '@/constants/theme';
import { formatSavedAt } from '@/lib/format-ride';

/**
 * Web sibling of the revisit screen. It exists for the same reason `result.web.tsx` does — there is
 * no map to build a screen around on web — so a loaded ride reads as a centred card of everything
 * that is not the map, matching how the results screen already presents a ride here.
 *
 * Every other state (restoring, signed out, loading, failed) comes from `SavedRouteLoader`, shared
 * with the native sibling so the two cannot drift apart.
 */
export default function SavedRouteScreenWeb() {
  const { id } = useLocalSearchParams<'/saved-routes/[id]'>();

  return (
    <SavedRouteLoader id={id}>
      {(route) => (
        <ThemedView style={styles.container}>
          {/* The rider named this ride when they saved it; the header is where they expect it. */}
          <Stack.Screen options={{ title: route.name }} />
          <ThemedView style={styles.content}>
            <ThemedText type="small" themeColor="textSecondary" style={styles.centered}>
              Saved {formatSavedAt(route.createdAt)}
            </ThemedText>
            <RideStats
              distanceMeters={route.distanceMeters}
              durationSeconds={route.durationSeconds}
            />
            <RouteMap geometry={route.geometry} />
            <Link href="/saved-routes" style={styles.centered}>
              <ThemedText type="linkPrimary">Back to your saved routes</ThemedText>
            </Link>
          </ThemedView>
        </ThemedView>
      )}
    </SavedRouteLoader>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: Spacing.four,
  },
  content: {
    width: '100%',
    maxWidth: MaxContentWidth,
    gap: Spacing.three,
  },
  centered: {
    textAlign: 'center',
  },
});
