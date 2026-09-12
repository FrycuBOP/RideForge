import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { Link } from 'expo-router';

import { RideStats } from '@/components/ride-stats';
import { RouteMap } from '@/components/route-map';
import { SaveRouteAction } from '@/components/save-route-action';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { Spacing } from '@/constants/theme';
import { getLastRoute } from '@/lib/route-result-store';

/** This overlay carries the stats card *and* the Save action, so it needs more room than most. */
const FIT_BOTTOM_PADDING = 280;

/**
 * Results screen — the end of the north-star flow: the generated loop drawn on a map with its
 * ride stats. The route comes from the in-memory store the generate mutation wrote (geometry is
 * far too large to pass through a navigation URL), so a reload or deep link finds nothing and
 * gets the empty state instead of a crash.
 */
export default function ResultScreen() {
  const insets = useSafeAreaInsets();
  // Snapshot once: the store is module-level mutable state, and the screen should keep showing the
  // route it was navigated with even if a later generation overwrites the store.
  const [ride] = useState(getLastRoute);

  if (!ride || ride.route.geometry.length === 0) {
    return (
      <ThemedView style={styles.emptyContainer}>
        <ThemedText type="subtitle" style={styles.centered}>
          No route yet
        </ThemedText>
        <ThemedText type="default" themeColor="textSecondary" style={styles.centered}>
          Plan a ride to see it drawn here.
        </ThemedText>
        <Link href="/" replace>
          <ThemedText type="linkPrimary">Back to Plan</ThemedText>
        </Link>
      </ThemedView>
    );
  }

  const { route } = ride;

  return (
    <View style={styles.container}>
      <RouteMap geometry={route.geometry} fitBottomPadding={FIT_BOTTOM_PADDING} />

      <View style={[styles.overlay, { bottom: insets.bottom + Spacing.four }]}>
        <RideStats distanceMeters={route.distanceMeters} durationSeconds={route.durationSeconds} />
        <SaveRouteAction ride={ride} />
      </View>
    </View>
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
  emptyContainer: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: Spacing.two,
    padding: Spacing.four,
  },
  centered: {
    textAlign: 'center',
  },
});
