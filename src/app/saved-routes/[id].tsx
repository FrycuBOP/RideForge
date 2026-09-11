import { ActivityIndicator, Pressable, StyleSheet } from 'react-native';

import { Link, useLocalSearchParams } from 'expo-router';

import { ApiError } from '@/api';
import { RideStats } from '@/components/ride-stats';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { Spacing } from '@/constants/theme';
import { useSavedRouteQuery } from '@/hooks/use-saved-route-query';
import { useSession } from '@/hooks/use-session';
import { useTheme } from '@/hooks/use-theme';
import { formatSavedAt } from '@/lib/format-ride';

/** Map a failed detail read to rider-facing copy (FR-005), in the list screen's shape. */
function detailErrorMessage(error: ApiError): string {
  switch (error.kind) {
    case 'timeout':
      return 'Loading this route took too long. Please try again.';
    case 'network':
      return 'Can’t reach the server. Check your connection and try again.';
    case 'parse':
      return 'Got an unexpected response from the server.';
    case 'http':
      if (error.status === 401) {
        return 'Your session has expired. Sign in again to see this route.';
      }
      if (error.status === 404) {
        return 'This route is no longer available.';
      }
      return 'Couldn’t load this route right now. Please try again.';
    default:
      return 'Something went wrong loading this route.';
  }
}

/**
 * One saved ride, revisited (FR-010). It owns its data — fetched by id, not read from the result
 * store — so it is deep-linkable, survives a reload, and cannot be overwritten by a generation the
 * rider starts while it is open. There is deliberately no Save action: this route is already saved.
 *
 * Phase 3 of this slice draws the route on the shared map component; until then the screen shows
 * the stats it was saved with, which is what makes the list's rows tappable.
 */
export default function SavedRouteScreen() {
  const theme = useTheme();
  const { id } = useLocalSearchParams<'/saved-routes/[id]'>();
  const { session, isRestoring } = useSession();
  const route = useSavedRouteQuery(id);

  if (isRestoring) {
    return (
      <ThemedView style={styles.container}>
        <ActivityIndicator color={theme.textSecondary} />
      </ThemedView>
    );
  }

  if (session === null) {
    return (
      <ThemedView style={styles.container}>
        <ThemedText type="default" themeColor="textSecondary" style={styles.centeredText}>
          Sign in to see this route.
        </ThemedText>
        <Link href="/account">
          <ThemedText type="linkPrimary">Go to Account</ThemedText>
        </Link>
      </ThemedView>
    );
  }

  if (route.isPending) {
    return (
      <ThemedView style={styles.container}>
        <ActivityIndicator color={theme.textSecondary} />
      </ThemedView>
    );
  }

  if (route.isError) {
    // A 404 is final — the route is gone, or was never this rider's. Everything else is worth
    // another attempt, so only the transient branch offers one.
    const gone = route.error.kind === 'http' && route.error.status === 404;

    return (
      <ThemedView style={styles.container}>
        <ThemedView type="backgroundElement" style={styles.errorCard}>
          <ThemedText type="small" style={styles.errorText}>
            {detailErrorMessage(route.error)}
          </ThemedText>
          {route.error.kind === 'http' && route.error.status === 401 && (
            <Link href="/account">
              <ThemedText type="linkPrimary">Go to Account</ThemedText>
            </Link>
          )}
        </ThemedView>
        {gone ? (
          <Link href="/saved-routes" replace>
            <ThemedText type="linkPrimary">Back to your saved routes</ThemedText>
          </Link>
        ) : (
          <Pressable
            onPress={() => route.refetch()}
            disabled={route.isFetching}
            accessibilityRole="button"
            accessibilityLabel="Retry loading this route"
            accessibilityState={{ disabled: route.isFetching, busy: route.isFetching }}>
            <ThemedText type="small" themeColor="textSecondary">
              {route.isFetching ? 'Retrying…' : 'Tap to try again'}
            </ThemedText>
          </Pressable>
        )}
      </ThemedView>
    );
  }

  return (
    <ThemedView style={styles.loaded}>
      <ThemedText type="subtitle">{route.data.name}</ThemedText>
      <ThemedText type="small" themeColor="textSecondary">
        Saved {formatSavedAt(route.data.createdAt)}
      </ThemedText>
      <RideStats
        distanceMeters={route.data.distanceMeters}
        durationSeconds={route.data.durationSeconds}
      />
    </ThemedView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: Spacing.two,
    padding: Spacing.four,
  },
  loaded: {
    flex: 1,
    gap: Spacing.two,
    padding: Spacing.three,
  },
  centeredText: {
    textAlign: 'center',
  },
  errorCard: {
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    gap: Spacing.one,
    alignItems: 'center',
  },
  errorText: {
    color: '#E5484D',
    textAlign: 'center',
  },
});
