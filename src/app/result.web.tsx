import { StyleSheet } from 'react-native';

import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';

/**
 * Web fallback for the results screen. react-native-maps has no web support — it calls
 * `codegenNativeComponent`, which react-native-web doesn't implement — so importing it in the
 * web bundle crashes the app. This sibling keeps web building; the native `result.tsx` owns the
 * real map. Phase 4 fleshes this out (ride stats / a static map image).
 */
export default function ResultScreenWeb() {
  return (
    <ThemedView style={styles.container}>
      <ThemedText type="subtitle">Map preview is available in the mobile app.</ThemedText>
    </ThemedView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
  },
});
