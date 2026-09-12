import type { ReactNode } from 'react';
import { ActivityIndicator, FlatList, Platform, Pressable, StyleSheet } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { Link } from 'expo-router';

import { ApiError, type SavedRouteSummary } from '@/api';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { BottomTabInset, MaxContentWidth, Spacing } from '@/constants/theme';
import { useSavedRoutesQuery } from '@/hooks/use-saved-routes-query';
import { useSession } from '@/hooks/use-session';
import { useTheme } from '@/hooks/use-theme';
import { formatDistanceKm, formatDuration, formatSavedAt } from '@/lib/format-ride';

/** Did the server reject the token? Signing in again is the only fix. */
function isSessionError(error: ApiError): boolean {
  return error.kind === 'http' && error.status === 401;
}

/** Map a failed list read to rider-facing copy (FR-005), in the `saveErrorMessage` shape. */
function listErrorMessage(error: ApiError): string {
  switch (error.kind) {
    case 'timeout':
      return 'Loading your routes took too long. Please try again.';
    case 'network':
      return 'Can’t reach the server. Check your connection and try again.';
    case 'parse':
      return 'Got an unexpected response from the server.';
    case 'http':
      if (error.status === 401) {
        return 'Your session has expired. Sign in again to see your saved routes.';
      }
      return 'Couldn’t load your saved routes right now. Please try again.';
    default:
      return 'Something went wrong loading your saved routes.';
  }
}

/** One saved ride: what it was called, how far, how long, and when it was saved. */
function SavedRouteRow({ route }: { route: SavedRouteSummary }) {
  return (
    <Link href={`/saved-routes/${route.id}`} asChild>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`${route.name}, ${formatDistanceKm(route.distanceMeters)}, ${formatDuration(route.durationSeconds)}, saved ${formatSavedAt(route.createdAt)}`}
        style={({ pressed }) => [{ opacity: pressed ? 0.7 : 1 }]}>
        <ThemedView type="backgroundElement" style={styles.row}>
          <ThemedText type="default" numberOfLines={1}>
            {route.name}
          </ThemedText>
          <ThemedText type="small" themeColor="textSecondary">
            {formatDistanceKm(route.distanceMeters)} · {formatDuration(route.durationSeconds)} ·
            saved {formatSavedAt(route.createdAt)}
          </ThemedText>
        </ThemedView>
      </Pressable>
    </Link>
  );
}

/**
 * Every state that is not a list of rides: centred copy, held clear of the tab bar. As a tab screen
 * the bar floats over the content, so the inset cannot be left to the safe area alone.
 */
function CenteredState({ bottomInset, children }: { bottomInset: number; children: ReactNode }) {
  return (
    <ThemedView style={[styles.centeredContainer, { paddingBottom: bottomInset }]}>
      {children}
    </ThemedView>
  );
}

/**
 * The rider's saved rides, newest first (FR-010) — the second half of the save loop S-06 opened.
 *
 * A tab, in the slot the scaffold's Explore screen used to hold. The tab itself is shown only to a
 * rider holding a session (see `app-tabs.tsx`), but every signed-out and failing state is still
 * handled here: on web the URL is reachable directly, and a session can expire while the screen sits
 * open. The screen renders its own title because its stack (`saved-routes/_layout.tsx`) gives it no
 * header — the detail pushed over it is the screen that needs one.
 */
export default function SavedRoutesScreen() {
  const theme = useTheme();
  const safeAreaInsets = useSafeAreaInsets();
  const { session, isRestoring } = useSession();
  const routes = useSavedRoutesQuery();

  // The tab-screen inset convention, as `index.tsx` and `account.tsx` apply it: iOS takes it through
  // `contentInset`, Android and web through content padding.
  const insets = {
    ...safeAreaInsets,
    bottom: safeAreaInsets.bottom + BottomTabInset + Spacing.three,
  };

  const contentPlatformStyle = Platform.select({
    android: {
      paddingTop: insets.top,
      paddingLeft: insets.left,
      paddingRight: insets.right,
      paddingBottom: insets.bottom,
    },
    web: {
      paddingTop: Spacing.six,
      paddingBottom: Spacing.four,
    },
  });

  // Restoring the persisted session is async. Rendering either branch here would flash the
  // signed-out prompt at an already signed-in rider on every cold start.
  if (isRestoring) {
    return (
      <CenteredState bottomInset={insets.bottom}>
        <ActivityIndicator color={theme.textSecondary} />
      </CenteredState>
    );
  }

  if (session === null) {
    return (
      <CenteredState bottomInset={insets.bottom}>
        <ThemedText type="subtitle" style={styles.centeredText}>
          Sign in first
        </ThemedText>
        <ThemedText type="default" themeColor="textSecondary" style={styles.centeredText}>
          Your saved routes live in your account.
        </ThemedText>
        <Link href="/account">
          <ThemedText type="linkPrimary">Go to Account</ThemedText>
        </Link>
      </CenteredState>
    );
  }

  if (routes.isPending) {
    return (
      <CenteredState bottomInset={insets.bottom}>
        <ActivityIndicator color={theme.textSecondary} />
      </CenteredState>
    );
  }

  if (routes.isError) {
    return (
      <CenteredState bottomInset={insets.bottom}>
        <ThemedView type="backgroundElement" style={styles.errorCard}>
          <ThemedText type="small" style={styles.errorText}>
            {listErrorMessage(routes.error)}
          </ThemedText>
          {isSessionError(routes.error) && (
            <Link href="/account">
              <ThemedText type="linkPrimary">Go to Account</ThemedText>
            </Link>
          )}
        </ThemedView>
        <Pressable
          onPress={() => routes.refetch()}
          disabled={routes.isFetching}
          accessibilityRole="button"
          accessibilityLabel="Retry loading your saved routes"
          accessibilityState={{ disabled: routes.isFetching, busy: routes.isFetching }}>
          <ThemedText type="small" themeColor="textSecondary">
            {routes.isFetching ? 'Retrying…' : 'Tap to try again'}
          </ThemedText>
        </Pressable>
      </CenteredState>
    );
  }

  if (routes.data.length === 0) {
    return (
      <CenteredState bottomInset={insets.bottom}>
        <ThemedText type="subtitle" style={styles.centeredText}>
          No saved routes yet
        </ThemedText>
        <ThemedText type="default" themeColor="textSecondary" style={styles.centeredText}>
          Plan a ride, then save it from the results screen to find it here.
        </ThemedText>
        <Link href="/">
          <ThemedText type="linkPrimary">Plan a ride</ThemedText>
        </Link>
      </CenteredState>
    );
  }

  return (
    <FlatList
      style={[styles.list, { backgroundColor: theme.background }]}
      // A rider's list is capped at 50 server-side, but FlatList still means only the visible rows
      // are mounted — the same shape holds if paging ever lifts that cap.
      data={routes.data}
      keyExtractor={(route) => route.id}
      renderItem={({ item }) => <SavedRouteRow route={item} />}
      // Scrolls with the rows rather than sitting above them, matching the other tab screens, whose
      // titles live inside the scroll content.
      ListHeaderComponent={
        <ThemedView style={styles.header}>
          <ThemedText type="title">Saved routes</ThemedText>
        </ThemedView>
      }
      contentInset={insets}
      contentContainerStyle={[
        styles.listContent,
        contentPlatformStyle,
        Platform.OS === 'web' && styles.listContentWeb,
      ]}
    />
  );
}

const styles = StyleSheet.create({
  list: {
    flex: 1,
  },
  listContent: {
    gap: Spacing.two,
    paddingHorizontal: Spacing.three,
  },
  // The web tab list spans the window, so the rows need their own width ceiling to line up with the
  // other tabbed screens rather than stretching across a desktop display.
  listContentWeb: {
    width: '100%',
    maxWidth: MaxContentWidth,
    alignSelf: 'center',
  },
  header: {
    paddingTop: Spacing.five,
    paddingBottom: Spacing.two,
  },
  row: {
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
    gap: Spacing.half,
  },
  centeredContainer: {
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
