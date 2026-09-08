import { StyleSheet, View, type ViewProps } from 'react-native';

import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { Spacing } from '@/constants/theme';
import { formatDistanceKm, formatDuration } from '@/lib/format-ride';

export type RideStatsProps = ViewProps & {
  distanceMeters: number;
  durationSeconds: number;
};

/**
 * Distance + duration for a generated route. Distance is the stitched length (what the ±20%
 * acceptance criterion is judged on); duration is the provider's estimate, shown as context.
 */
export function RideStats({ distanceMeters, durationSeconds, style, ...rest }: RideStatsProps) {
  return (
    <ThemedView type="backgroundElement" style={[styles.card, style]} {...rest}>
      <View style={styles.stat}>
        <ThemedText type="smallBold" themeColor="textSecondary" style={styles.label}>
          DISTANCE
        </ThemedText>
        <ThemedText type="subtitle">{formatDistanceKm(distanceMeters)}</ThemedText>
      </View>
      <View style={styles.stat}>
        <ThemedText type="smallBold" themeColor="textSecondary" style={styles.label}>
          DURATION
        </ThemedText>
        <ThemedText type="subtitle">{formatDuration(durationSeconds)}</ThemedText>
      </View>
    </ThemedView>
  );
}

const styles = StyleSheet.create({
  card: {
    flexDirection: 'row',
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
    gap: Spacing.four,
  },
  stat: {
    gap: Spacing.half,
  },
  label: {
    letterSpacing: 0.5,
  },
});
