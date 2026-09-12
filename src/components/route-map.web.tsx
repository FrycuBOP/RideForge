import { StyleSheet } from 'react-native';

import type { GeoPoint } from '@/api';
import { ThemedText } from '@/components/themed-text';

/** Mirrors `route-map.tsx` — platform siblings must expose the same shape. */
export type RouteMapProps = {
  geometry: GeoPoint[];
  fitBottomPadding?: number;
};

/**
 * Web sibling of the route map. react-native-maps has no web support — it calls
 * `codegenNativeComponent`, which react-native-web doesn't implement — so this stands in for the
 * map and points at the mobile app for the preview itself. Both props are accepted and ignored:
 * there is nothing to draw and nothing to fit.
 *
 * It renders a line of copy rather than a map-shaped placeholder, because the screens that use it
 * on web lay their content out as a centred card, not as a full-bleed map with an overlay.
 */
export function RouteMap(_props: RouteMapProps) {
  return (
    <ThemedText type="small" themeColor="textSecondary" style={styles.notice}>
      The map preview is available in the mobile app.
    </ThemedText>
  );
}

const styles = StyleSheet.create({
  notice: {
    textAlign: 'center',
  },
});
