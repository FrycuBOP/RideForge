import { StyleSheet } from 'react-native';
import MapView from 'react-native-maps';

/**
 * Phase 1 smoke screen: proves react-native-maps renders on a dev build (Apple Maps on iOS,
 * Google Maps on Android). Superseded by the full results screen in Phase 4 (map + route
 * polyline + ride stats).
 */
export default function ResultScreen() {
  return (
    <MapView
      style={styles.map}
      initialRegion={{
        latitude: 50.0647,
        longitude: 19.945,
        latitudeDelta: 0.2,
        longitudeDelta: 0.2,
      }}
    />
  );
}

const styles = StyleSheet.create({
  map: {
    flex: 1,
  },
});
