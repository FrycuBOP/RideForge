import { useRef } from 'react';
import { StyleSheet } from 'react-native';

import MapView, { Marker, Polyline, type LatLng, type Region } from 'react-native-maps';

import type { GeoPoint } from '@/api';

export type RouteMapProps = {
  /** The loop to draw. An empty geometry renders nothing — callers show their own empty state. */
  geometry: GeoPoint[];
  /**
   * Space kept clear at the bottom of the fitted route, so the part of the map the screen's overlay
   * covers is never where the polyline is drawn. The default clears a stats-only overlay; a screen
   * stacking more on top of the stats passes its own.
   */
  fitBottomPadding?: number;
};

/** Padding kept around the fitted route so the polyline never touches the screen edge. */
const FIT_EDGE_PADDING = { top: 80, right: 60, left: 60 };

/** Clears a stats card plus the safe-area inset beneath it. */
const DEFAULT_FIT_BOTTOM_PADDING = 200;

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
 * A route loop drawn on a map, with its start marked — shared by the results screen (the ride just
 * generated) and the revisit screen (a ride read back from the account). It fills its parent, so a
 * screen stacks its own overlay on top rather than passing children.
 *
 * Web has its own sibling: react-native-maps calls `codegenNativeComponent`, which
 * react-native-web does not implement, so importing this file into the web bundle crashes the app.
 */
export function RouteMap({ geometry, fitBottomPadding }: RouteMapProps) {
  const mapRef = useRef<MapView | null>(null);

  if (geometry.length === 0) return null;

  const coordinates: LatLng[] = geometry.map(({ lat, lng }) => ({
    latitude: lat,
    longitude: lng,
  }));

  return (
    <MapView
      ref={mapRef}
      style={styles.map}
      initialRegion={boundingRegion(coordinates)}
      // Android throws if fitToCoordinates runs during mount — onLayout is the safe hook.
      onLayout={() =>
        mapRef.current?.fitToCoordinates(coordinates, {
          edgePadding: {
            ...FIT_EDGE_PADDING,
            bottom: fitBottomPadding ?? DEFAULT_FIT_BOTTOM_PADDING,
          },
          animated: false,
        })
      }>
      <Polyline coordinates={coordinates} strokeColor="#208AEF" strokeWidth={5} />
      <Marker coordinate={coordinates[0]} title="Start" />
    </MapView>
  );
}

const styles = StyleSheet.create({
  map: {
    flex: 1,
  },
});
