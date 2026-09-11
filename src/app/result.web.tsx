import { useState } from 'react';
import { StyleSheet } from 'react-native';

import { Link } from 'expo-router';

import { RideStats } from '@/components/ride-stats';
import { SaveRouteAction } from '@/components/save-route-action';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { MaxContentWidth, Spacing } from '@/constants/theme';
import { getLastRoute } from '@/lib/route-result-store';

/**
 * Web fallback for the results screen. react-native-maps has no web support — it calls
 * `codegenNativeComponent`, which react-native-web doesn't implement — so importing it in the web
 * bundle crashes the app. This sibling shows the same ride stats and points at the mobile app for
 * the map itself; the native `result.tsx` owns the real preview.
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
            <ThemedText type="small" themeColor="textSecondary" style={styles.centered}>
              The map preview is available in the mobile app.
            </ThemedText>
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
