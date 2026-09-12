import { StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { Stack, useLocalSearchParams } from 'expo-router';

import { RideStats } from '@/components/ride-stats';
import { RouteMap } from '@/components/route-map';
import { SavedRouteLoader } from '@/components/saved-route-loader';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { Spacing } from '@/constants/theme';
import { formatSavedAt } from '@/lib/format-ride';

/** The overlay here carries only the saved-at line and the stats card — no Save action. */
const FIT_BOTTOM_PADDING = 200;

/**
 * One saved ride, revisited (FR-010). It owns its data — fetched by id, not read from the result
 * store — so it is deep-linkable, survives a reload, and cannot be overwritten by a generation the
 * rider starts while it is open. There is deliberately no Save action: this route is already saved.
 */
export default function SavedRouteScreen() {
  const insets = useSafeAreaInsets();
  const { id } = useLocalSearchParams<'/saved-routes/[id]'>();

  return (
    <SavedRouteLoader id={id}>
      {(route) => (
        <View style={styles.container}>
          {/* The rider named this ride when they saved it; the header is where they expect it. */}
          <Stack.Screen options={{ title: route.name }} />
          <RouteMap geometry={route.geometry} fitBottomPadding={FIT_BOTTOM_PADDING} />

          <View style={[styles.overlay, { bottom: insets.bottom + Spacing.four }]}>
            <ThemedView type="backgroundElement" style={styles.savedAtCard}>
              <ThemedText type="small" themeColor="textSecondary">
                Saved {formatSavedAt(route.createdAt)}
              </ThemedText>
            </ThemedView>
            <RideStats
              distanceMeters={route.distanceMeters}
              durationSeconds={route.durationSeconds}
            />
          </View>
        </View>
      )}
    </SavedRouteLoader>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  overlay: {
    position: 'absolute',
    left: Spacing.three,
    right: Spacing.three,
    gap: Spacing.two,
  },
  savedAtCard: {
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    alignSelf: 'flex-start',
  },
});
