import { useEffect, useState, type ReactNode } from 'react';
import { ActivityIndicator, Pressable, StyleSheet } from 'react-native';

import { Link } from 'expo-router';

import { ApiError, type SavedRouteDetail } from '@/api';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { Spacing } from '@/constants/theme';
import { useSavedRouteQuery } from '@/hooks/use-saved-route-query';
import { useSession } from '@/hooks/use-session';
import { useTheme } from '@/hooks/use-theme';

/**
 * How long a missing route param may stay missing before the screen stops waiting. Long enough that
 * a slow first render is never mistaken for a dead link, short enough that a dead link is not an
 * indefinite spinner.
 */
const MISSING_ID_GRACE_MS = 1_000;

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

export type SavedRouteLoaderProps = {
  /** Route id from the URL. Missing on the first render of a deep link, which reads as loading. */
  id: string | undefined;
  /** Rendered once the ride is in hand. Everything before that is this component's business. */
  children: (route: SavedRouteDetail) => ReactNode;
};

/**
 * Every state the revisit screen can be in except the one worth looking at. `/saved-routes/[id]`
 * has a native and a web sibling that differ only in how a loaded ride is drawn, so the restoring,
 * signed-out, loading and failure branches live here once rather than in both — a copy change or a
 * new failure branch that landed in only one of them would be a bug nothing catches.
 */
export function SavedRouteLoader({ id, children }: SavedRouteLoaderProps) {
  const theme = useTheme();
  const { session, isRestoring } = useSession();
  const route = useSavedRouteQuery(id);
  const hasId = id !== undefined && id.length > 0;
  // A route param can be missing on the very first render of a deep link, and a frame of that
  // correctly reads as loading. A param that never arrives is a different thing: the query stays
  // disabled, react-query keeps reporting `isPending`, and this component would sit on a spinner
  // with no error branch and no timeout. So give the param a grace period, then stop waiting.
  const [gaveUpOnId, setGaveUpOnId] = useState(false);
  useEffect(() => {
    if (hasId) return;
    const timer = setTimeout(() => setGaveUpOnId(true), MISSING_ID_GRACE_MS);
    return () => clearTimeout(timer);
  }, [hasId]);

  // Restoring the persisted session is async. Rendering either branch here would flash the
  // signed-out prompt at an already signed-in rider on every cold start.
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

  // No id once mount has settled: nothing to load and nothing to retry, so say what the 404 branch
  // says rather than spinning forever.
  if (!hasId && gaveUpOnId) {
    return (
      <ThemedView style={styles.container}>
        <ThemedView type="backgroundElement" style={styles.errorCard}>
          <ThemedText type="small" style={styles.errorText}>
            This route is no longer available.
          </ThemedText>
        </ThemedView>
        <Link href="/saved-routes" replace>
          <ThemedText type="linkPrimary">Back to your saved routes</ThemedText>
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

  return children(route.data);
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: Spacing.two,
    padding: Spacing.four,
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
