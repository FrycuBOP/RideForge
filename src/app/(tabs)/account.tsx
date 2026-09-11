import { useState } from 'react';
import {
  ActivityIndicator,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { Link } from 'expo-router';

import { ApiError } from '@/api';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { BottomTabInset, MaxContentWidth, Spacing } from '@/constants/theme';
import { useMeQuery } from '@/hooks/use-me-query';
import { useSession } from '@/hooks/use-session';
import { useTheme } from '@/hooks/use-theme';
import { authErrorMessage } from '@/lib/auth-errors';
import { useLastRoute } from '@/lib/route-result-store';
import { supabase } from '@/lib/supabase';

/**
 * Map a failed `/me` lookup to rider-facing copy. A 401 gets its own branch: it does not mean the
 * server is unwell, it means this device is holding a token the server no longer accepts, and the
 * only thing that fixes it is signing in again.
 */
function meErrorMessage(error: ApiError): string {
  switch (error.kind) {
    case 'timeout':
      return 'The server took too long to confirm your account.';
    case 'network':
      return 'Can’t reach the server to confirm your account. Check your connection.';
    case 'parse':
      return 'Got an unexpected response from the server.';
    case 'http':
      if (error.status === 401) {
        return 'Your session is no longer valid. Sign out, then sign in again.';
      }
      return 'The server had a problem confirming your account. Please try again.';
    default:
      return 'Something went wrong confirming your account.';
  }
}

export default function AccountScreen() {
  const theme = useTheme();
  const safeAreaInsets = useSafeAreaInsets();
  const { user, isRestoring } = useSession();
  // The identity the backend resolved from the token — the only proof the JWT actually validated
  // server-side. Idle (never fetched) while signed out; see useMeQuery's `enabled`.
  const me = useMeQuery();
  // Closes the "Sign in to save" loop: a rider who left an unsaved route to sign in gets a way back
  // to it. Hidden once this rider has saved it — there is nothing left to go back for.
  const lastRide = useLastRoute();
  const hasUnsavedRide = user !== null && lastRide !== null && lastRide.savedBy !== user.id;

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Not an error: the one success path that leaves the rider signed out and needing to act.
  const [notice, setNotice] = useState<string | null>(null);

  const credentialsValid = email.trim().length > 0 && password.length > 0;
  const canSubmit = credentialsValid && !pending;

  /**
   * Both sign-up and sign-in take the same path: disable the form, clear the previous error, run
   * the call, and surface a mapped message. On success the session provider's
   * `onAuthStateChange` subscription swaps the view — this screen never sets session state itself.
   */
  async function submit(action: 'signUp' | 'signIn') {
    setError(null);
    setNotice(null);
    setPending(true);

    try {
      const credentials = { email: email.trim(), password };
      const { data, error: authError } =
        action === 'signUp'
          ? await supabase.auth.signUp(credentials)
          : await supabase.auth.signInWithPassword(credentials);

      if (authError) {
        setError(authErrorMessage(authError));
        return;
      }

      // A sign-up with no session is a *success* that leaves the rider signed out: Supabase answers
      // this way when the project requires email confirmation. Without this branch the screen would
      // clear the password, render nothing, and look like a dead button.
      //
      // This project disables confirmations (see the plan's phase 1), so under the intended
      // configuration this never fires. It is here because nothing in this repo enforces that
      // dashboard setting — flipping it must not silently break sign-up.
      if (action === 'signUp' && data.session === null) {
        setNotice('Account created. Check your email to confirm it, then sign in.');
      }

      // Only clear on success: a rider who mistyped their password keeps the email they entered.
      setPassword('');
    } finally {
      setPending(false);
    }
  }

  async function handleSignOut() {
    setError(null);
    setNotice(null);
    setPending(true);

    try {
      const { error: authError } = await supabase.auth.signOut();
      if (authError) {
        setError(authErrorMessage(authError));
        return;
      }
      setEmail('');
      setPassword('');
    } finally {
      setPending(false);
    }
  }

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

  return (
    <ScrollView
      style={[styles.scrollView, { backgroundColor: theme.background }]}
      contentInset={insets}
      contentContainerStyle={[styles.contentContainer, contentPlatformStyle]}>
      <ThemedView style={styles.container}>
        <ThemedView style={styles.header}>
          <ThemedText type="title">Account</ThemedText>
        </ThemedView>

        {/* Restoring the persisted session is async. Rendering either branch here would flash a
            sign-in form at an already signed-in rider on every cold start. */}
        {isRestoring ? (
          <ThemedView style={styles.restoring}>
            <ActivityIndicator color={theme.textSecondary} />
          </ThemedView>
        ) : user ? (
          <ThemedView style={styles.form}>
            {hasUnsavedRide && (
              <Link href="/result">
                <ThemedText type="linkPrimary">Back to your route</ThemedText>
              </Link>
            )}

            {/* The way into the saved rides (FR-010). Signed-in only: the signed-out copy below
                already explains what an account buys, and the screen itself handles the deep-link
                case where a signed-out rider arrives anyway. */}
            <Link href="/saved-routes">
              <ThemedText type="linkPrimary">Your saved routes</ThemedText>
            </Link>

            <ThemedView style={styles.section}>
              <ThemedText type="smallBold" themeColor="textSecondary" style={styles.sectionLabel}>
                SIGNED IN AS
              </ThemedText>
              <ThemedView type="backgroundElement" style={styles.card}>
                {/* Deliberately the backend's answer, not `user.email`: the local session says who
                    this device believes it is, `/me` says who the server accepted the token as. */}
                {me.isPending ? (
                  <ThemedView type="backgroundElement" style={styles.cardLoading}>
                    <ActivityIndicator color={theme.textSecondary} />
                  </ThemedView>
                ) : me.isError ? (
                  <ThemedText type="default" style={[styles.cardText, styles.errorText]}>
                    {meErrorMessage(me.error)}
                  </ThemedText>
                ) : (
                  <ThemedText type="default" style={styles.cardText}>
                    {me.data.email ?? 'No email on record'}
                  </ThemedText>
                )}
              </ThemedView>
              {me.isError && (
                <Pressable
                  onPress={() => me.refetch()}
                  disabled={me.isFetching}
                  accessibilityRole="button"
                  accessibilityLabel="Retry confirming your account"
                  accessibilityState={{ disabled: me.isFetching, busy: me.isFetching }}>
                  <ThemedText type="small" themeColor="textSecondary">
                    {me.isFetching ? 'Retrying…' : 'Tap to try again'}
                  </ThemedText>
                </Pressable>
              )}
            </ThemedView>

            <ThemedText type="small" themeColor="textSecondary">
              Signed-in riders generate as many routes as they like — no hourly limit.
            </ThemedText>

            {error !== null && (
              <ThemedText type="small" style={styles.errorText}>
                {error}
              </ThemedText>
            )}

            <Pressable
              style={({ pressed }) => [
                styles.secondaryButton,
                {
                  borderColor: theme.backgroundSelected,
                  opacity: pending ? 0.4 : pressed ? 0.8 : 1,
                },
              ]}
              onPress={handleSignOut}
              disabled={pending}
              accessibilityRole="button"
              accessibilityLabel="Sign out"
              accessibilityState={{ disabled: pending, busy: pending }}>
              <ThemedText type="default">{pending ? 'Signing out…' : 'Sign out'}</ThemedText>
            </Pressable>
          </ThemedView>
        ) : (
          <ThemedView style={styles.form}>
            <ThemedText type="small" themeColor="textSecondary">
              Sign in to save your routes and lift the hourly limit on route generation.
            </ThemedText>

            <ThemedView style={styles.section}>
              <ThemedText type="smallBold" themeColor="textSecondary" style={styles.sectionLabel}>
                EMAIL
              </ThemedText>
              <ThemedView type="backgroundElement" style={styles.card}>
                <TextInput
                  style={[styles.input, { color: theme.text }]}
                  placeholder="you@example.com"
                  placeholderTextColor={theme.textSecondary}
                  value={email}
                  onChangeText={setEmail}
                  autoCorrect={false}
                  autoCapitalize="none"
                  keyboardType="email-address"
                  textContentType="emailAddress"
                  returnKeyType="next"
                  editable={!pending}
                  accessibilityLabel="Email address"
                />
              </ThemedView>
            </ThemedView>

            <ThemedView style={styles.section}>
              <ThemedText type="smallBold" themeColor="textSecondary" style={styles.sectionLabel}>
                PASSWORD
              </ThemedText>
              <ThemedView type="backgroundElement" style={styles.card}>
                <TextInput
                  style={[styles.input, { color: theme.text }]}
                  placeholder="At least 6 characters"
                  placeholderTextColor={theme.textSecondary}
                  value={password}
                  onChangeText={setPassword}
                  autoCorrect={false}
                  autoCapitalize="none"
                  secureTextEntry
                  textContentType="password"
                  returnKeyType="done"
                  editable={!pending}
                  accessibilityLabel="Password"
                />
              </ThemedView>
            </ThemedView>

            {error !== null && (
              <ThemedText type="small" style={styles.errorText}>
                {error}
              </ThemedText>
            )}

            {notice !== null && (
              <ThemedText type="small" themeColor="textSecondary">
                {notice}
              </ThemedText>
            )}

            <Pressable
              style={({ pressed }) => [
                styles.primaryButton,
                { opacity: canSubmit ? (pressed ? 0.8 : 1) : 0.4 },
              ]}
              onPress={() => submit('signIn')}
              disabled={!canSubmit}
              accessibilityRole="button"
              accessibilityLabel="Sign in"
              accessibilityState={{ disabled: !canSubmit, busy: pending }}>
              <Text style={styles.primaryButtonLabel}>{pending ? 'Working…' : 'Sign in'}</Text>
            </Pressable>

            <Pressable
              style={({ pressed }) => [
                styles.secondaryButton,
                {
                  borderColor: theme.backgroundSelected,
                  opacity: canSubmit ? (pressed ? 0.8 : 1) : 0.4,
                },
              ]}
              onPress={() => submit('signUp')}
              disabled={!canSubmit}
              accessibilityRole="button"
              accessibilityLabel="Create account"
              accessibilityState={{ disabled: !canSubmit, busy: pending }}>
              <ThemedText type="default">Create account</ThemedText>
            </Pressable>
          </ThemedView>
        )}
      </ThemedView>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scrollView: {
    flex: 1,
  },
  contentContainer: {
    flexDirection: 'row',
    justifyContent: 'center',
  },
  container: {
    maxWidth: MaxContentWidth,
    flexGrow: 1,
    paddingHorizontal: Spacing.four,
  },
  header: {
    paddingTop: Spacing.five,
    paddingBottom: Spacing.four,
  },
  form: {
    gap: Spacing.four,
    paddingBottom: Spacing.four,
  },
  restoring: {
    paddingVertical: Spacing.five,
  },
  section: {
    gap: Spacing.two,
  },
  sectionLabel: {
    letterSpacing: 0.5,
  },
  card: {
    borderRadius: Spacing.three,
    overflow: 'hidden',
  },
  cardText: {
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
  },
  // Matches cardText's vertical rhythm so the card doesn't resize when /me resolves.
  cardLoading: {
    paddingVertical: Spacing.three,
    alignItems: 'center',
  },
  input: {
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
    fontSize: 16,
    lineHeight: 24,
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
