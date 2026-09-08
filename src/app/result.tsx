import { useRef, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { Link } from 'expo-router';
import MapView, { Marker, Polyline, type LatLng, type Region } from 'react-native-maps';

import { RideStats } from '@/components/ride-stats';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { Spacing } from '@/constants/theme';
import { getLastRoute } from '@/lib/route-result-store';

/** Padding kept around the fitted route so the polyline never touches the screen edge. */
const FIT_EDGE_PADDING = { top: 80, right: 60, bottom: 200, left: 60 };

/** Fallback framing before `fitToCoordinates` runs, and the only framing iOS honours on mount. */
function boundingRegion(points: LatLng[]): Region {
  const lats = points.map((p) => p.latitude);
  const lngs = points.map((p) => p.longitude);
  const minLat = Math.min(...lats);
  const maxLat = Math.max(...lats);
  const minLng = Math.min(...lngs);
  const maxLng = Math.max(...lngs);
  return {
    latitude: (minLat + maxLat) / 2,
    longitude: (minLng + maxLng) / 2,
    // 1.4x the span leaves margin; the floor keeps a degenerate (near-zero span) route visible.
    latitudeDelta: Math.max((maxLat - minLat) * 1.4, 0.02),
    longitudeDelta: Math.max((maxLng - minLng) * 1.4, 0.02),
  };
}

/**
 * Results screen — the end of the north-star flow: the generated loop drawn on a map with its
 * ride stats. The route comes from the in-memory store the generate mutation wrote (geometry is
 * far too large to pass through a navigation URL), so a reload or deep link finds nothing and
 * gets the empty state instead of a crash.
 */
export default function ResultScreen() {
  const insets = useSafeAreaInsets();
  const mapRef = useRef<MapView | null>(null);
  // Snapshot once: the store is module-level mutable state, and the screen should keep showing the
  // route it was navigated with even if a later generation overwrites the store.
  const [route] = useState(getLastRoute);

  if (!route || route.geometry.length === 0) {
    return (
      <ThemedView style={styles.emptyContainer}>
        <ThemedText type="subtitle" style={styles.centered}>
          No route yet
        </ThemedText>
        <ThemedText type="default" themeColor="textSecondary" style={styles.centered}>
          Generated routes aren’t saved yet. Plan a ride to see it drawn here.
        </ThemedText>
        <Link href="/" replace>
          <ThemedText type="linkPrimary">Back to Plan</ThemedText>
        </Link>
      </ThemedView>
    );
  }

  const coordinates: LatLng[] = route.geometry.map(({ lat, lng }) => ({
    latitude: lat,
    longitude: lng,
  }));

  return (
    <View style={styles.container}>
      <MapView
        ref={mapRef}
        style={styles.map}
        initialRegion={boundingRegion(coordinates)}
        // Android throws if fitToCoordinates runs during mount — onLayout is the safe hook.
        onLayout={() =>
          mapRef.current?.fitToCoordinates(coordinates, {
            edgePadding: FIT_EDGE_PADDING,
            animated: false,
          })
        }>
        <Polyline coordinates={coordinates} strokeColor="#208AEF" strokeWidth={5} />
        <Marker coordinate={coordinates[0]} title="Start" />
      </MapView>

      <RideStats
        distanceMeters={route.distanceMeters}
        durationSeconds={route.durationSeconds}
        style={[styles.stats, { bottom: insets.bottom + Spacing.four }]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  map: {
    flex: 1,
  },
  stats: {
    position: 'absolute',
    left: Spacing.three,
    right: Spacing.three,
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
