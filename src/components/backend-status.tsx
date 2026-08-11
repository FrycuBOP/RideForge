import { StyleSheet, View } from 'react-native';

import { ThemedText } from '@/components/themed-text';
import { useHealthQuery } from '@/hooks/use-health-query';
import { useTheme } from '@/hooks/use-theme';

/**
 * Dev-only connectivity indicator (F-01). Proves the app reaches the backend and shows the
 * FR-005 loading/error convention driven by react-query state. Renders nothing in production
 * builds — S-01 owns any user-facing surface.
 */
export function BackendStatus() {
  const theme = useTheme();
  const { status, error } = useHealthQuery();

  if (!__DEV__) return null;

  let label: string;
  let color: string;
  if (status === 'pending') {
    label = 'backend: checking…';
    color = theme.textSecondary;
  } else if (status === 'success') {
    label = 'backend: connected';
    color = theme.text;
  } else {
    label = `backend: ${error?.kind ?? 'error'}`;
    color = '#E5484D';
  }

  return (
    <View style={[styles.pill, { backgroundColor: theme.backgroundElement }]}>
      <View style={[styles.dot, { backgroundColor: color }]} />
      <ThemedText type="smallBold" style={{ color }}>
        {label}
      </ThemedText>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 999,
    marginTop: 8,
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
});
