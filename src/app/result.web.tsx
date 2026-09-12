import { useState } from 'react';
import { StyleSheet } from 'react-native';

import { Link } from 'expo-router';

import { RideStats } from '@/components/ride-stats';
import { RouteMap } from '@/components/route-map';
import { SaveRouteAction } from '@/components/save-route-action';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { MaxContentWidth, Spacing } from '@/constants/theme';
import { getLastRoute } from '@/lib/route-result-store';

/**
 * Web fallback for the results screen. react-native-maps has no web support — it calls
 * `codegenNativeComponent`, which react-native-web doesn't implement — so a screen built around a
 * full-bleed map has nothing to build around here. This sibling lays the ride out as a centred card
 * instead; `RouteMap` resolves to its own web sibling and says where the preview lives.
 */
export default function ResultScreenWeb() {
  const [ride] = useState(getLastRoute);

  return (
    <ThemedView style={styles.container}>
      <ThemedView style={styles.content}>
        {ride && ride.route.geometry.length > 0 ? (
          <>
            <RideStats
              distanceMeters={ride.route.distanceMeters}
              durationSeconds={ride.route.durationSeconds}
            />
            <SaveRouteAction ride={ride} />
            <RouteMap geometry={ride.route.geometry} />
          </>
        ) : (
          <>
            <ThemedText type="subtitle" style={styles.centered}>
              No route yet
            </ThemedText>
            <ThemedText type="default" themeColor="textSecondary" style={styles.centered}>
              Plan a ride to see its stats here.
            </ThemedText>
          </>
        )}
        <Link href="/" replace style={styles.centered}>
          <ThemedText type="linkPrimary">Back to Plan</ThemedText>
        </Link>
      </ThemedView>
    </ThemedView>
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
