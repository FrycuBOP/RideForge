import { Pressable, StyleSheet, Text, View, type ViewProps } from 'react-native';

import { Link, router } from 'expo-router';

import { ApiError } from '@/api';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { Spacing } from '@/constants/theme';
import { toSaveRouteRequest, useSaveRouteMutation } from '@/hooks/use-save-route-mutation';
import { useSession } from '@/hooks/use-session';
import { useTheme } from '@/hooks/use-theme';
import { useLastRoute, type Ride } from '@/lib/route-result-store';

/** Did the server reject the token? Signing in again is the only fix. */
function isSessionError(error: ApiError): boolean {
  return error.kind === 'http' && error.status === 401;
}

/** Map a failed save to a rider-facing message (FR-005), in the `planErrorMessage` shape. */
function saveErrorMessage(error: ApiError): string {
  switch (error.kind) {
    case 'timeout':
      return 'Saving took too long. Please try again.';
    case 'network':
      return 'Can’t reach the server. Check your connection and try again.';
    case 'parse':
      return 'Got an unexpected response from the server.';
    case 'http':
      if (error.status === 401) {
        return 'Your session has expired. Sign in again to save this route.';
      }
      if (error.status === 400) {
        return 'This route can’t be saved.';
      }
      return 'Couldn’t save the route right now. Please try again.';
    default:
      return 'Something went wrong. Please try again.';
  }
}

export type SaveRouteActionProps = Pick<ViewProps, 'style'> & {
  /** The ride the screen is showing — its own snapshot, not whatever the store holds now. */
  ride: Ride;
};

/**
 * The Save control shared by both result screens (FR-009). Signed out it offers the way to an
 * account instead; while the persisted session is still being restored it renders nothing, so a
 * signed-in rider never sees "Sign in to save" flash on a cold start.
 */
export function SaveRouteAction({ ride, style }: SaveRouteActionProps) {
  const theme = useTheme();
  const { user, isRestoring } = useSession();

  if (isRestoring) return null;

  if (!user) {
    return (
      <View style={style}>
        <Pressable
          style={({ pressed }) => [
            styles.secondaryButton,
            {
              backgroundColor: theme.backgroundElement,
              borderColor: theme.backgroundSelected,
              opacity: pressed ? 0.8 : 1,
            },
          ]}
          // `navigate`, not `push`: the tabs are already under this screen, so this unwinds to them
          // instead of stacking a second tab navigator on top.
          onPress={() => router.navigate('/account')}
          accessibilityRole="button"
          accessibilityLabel="Sign in to save">
          <ThemedText type="default">Sign in to save</ThemedText>
        </Pressable>
      </View>
    );
  }

  // Keyed by rider so a sign-out → sign-in on this device starts from a fresh mutation: rider B
  // must not inherit rider A's pending, error, or success state.
  return <SignedInSaveAction key={user.id} ride={ride} userId={user.id} style={style} />;
}

function SignedInSaveAction({ ride, userId, style }: SaveRouteActionProps & { userId: string }) {
  const theme = useTheme();
  const current = useLastRoute();
  const save = useSaveRouteMutation();

  // The store may have moved on to a newer ride; only its record for *this* ride counts. A local
  // success covers that case — this component is keyed by rider, so its success is this rider's.
  const savedBy = current?.clientRouteId === ride.clientRouteId ? current.savedBy : null;
  const saved = savedBy === userId || save.isSuccess;

  if (saved) {
    return (
      <View style={style}>
        <ThemedView
          type="backgroundElement"
          style={[styles.secondaryButton, { borderColor: theme.backgroundSelected }]}
          accessibilityRole="text"
          accessibilityLabel="Route saved">
          <ThemedText type="default">Saved ✓</ThemedText>
        </ThemedView>
      </View>
    );
  }

  // `isPending` alone gates the button: the same clientRouteId goes out on every attempt, so even a
  // tap that slips through before the re-render is answered with the existing row, not a new one.
  const busy = save.isPending;

  return (
    <View style={[styles.container, style]}>
      {save.isError && (
        <ThemedView type="backgroundElement" style={styles.errorCard}>
          <ThemedText type="small" style={styles.errorText}>
            {saveErrorMessage(save.error)}
          </ThemedText>
          {isSessionError(save.error) && (
            <Link href="/account">
              <ThemedText type="linkPrimary">Go to Account</ThemedText>
            </Link>
          )}
        </ThemedView>
      )}

      <Pressable
        style={({ pressed }) => [
          styles.primaryButton,
          { opacity: busy ? 0.6 : pressed ? 0.8 : 1 },
        ]}
        onPress={() => save.mutate(toSaveRouteRequest(ride))}
        disabled={busy}
        accessibilityRole="button"
        accessibilityLabel={save.isError ? 'Try saving the route again' : 'Save route'}
        accessibilityState={{ disabled: busy, busy }}>
        <Text style={styles.primaryButtonLabel}>
          {busy ? 'Saving…' : save.isError ? 'Try again' : 'Save route'}
        </Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: Spacing.two,
  },
  errorCard: {
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    gap: Spacing.one,
  },
  errorText: {
    color: '#E5484D',
  },
  primaryButton: {
    backgroundColor: '#208AEF',
    borderRadius: Spacing.five,
    paddingVertical: Spacing.three,
    alignItems: 'center',
  },
  primaryButtonLabel: {
    color: '#ffffff',
    fontSize: 16,
    lineHeight: 24,
    fontWeight: '600',
  },
  secondaryButton: {
    borderRadius: Spacing.five,
    borderWidth: StyleSheet.hairlineWidth,
    paddingVertical: Spacing.three,
    alignItems: 'center',
  },
});
