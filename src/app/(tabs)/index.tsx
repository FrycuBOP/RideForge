import { Fragment, useEffect, useState } from 'react';
import { Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import * as Location from 'expo-location';
import { Link } from 'expo-router';

import { ApiError } from '@/api';
import { BackendStatus } from '@/components/backend-status';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { BottomTabInset, MaxContentWidth, Spacing } from '@/constants/theme';
import { useGenerateRouteMutation } from '@/hooks/use-generate-route-mutation';
import { useTheme } from '@/hooks/use-theme';
import { geocodeAddress, reverseGeocode, type GeocodeFailureReason } from '@/lib/geocode';

type CurvinessLevel = 1 | 2 | 3 | 4 | 5;

const CURVINESS_OPTIONS: { level: CurvinessLevel; label: string }[] = [
  { level: 1, label: 'Highways' },
  { level: 2, label: 'Mixed' },
  { level: 3, label: 'Curvy' },
  { level: 4, label: 'Very Curvy' },
  { level: 5, label: 'Extreme' },
];

/** Map a geocode failure to advice that matches the actual cause (FR-005). */
function geocodeErrorMessage(reason: GeocodeFailureReason): string {
  switch (reason) {
    case 'permission':
      return 'Location access is off, so this device can’t look up addresses. Turn it on in Settings, then try again.';
    case 'unavailable':
      return 'Address lookup isn’t available right now. Check your connection and try again.';
    default:
      return 'Couldn’t find that starting point. Try a more specific address.';
  }
}

/** Did this failure hit the anonymous generation quota? Signing in is the only fix. */
function isQuotaError(error: ApiError): boolean {
  return error.kind === 'http' && error.status === 429;
}

/** Map a normalized API failure to a rider-facing message (FR-005). */
function planErrorMessage(error: ApiError): string {
  switch (error.kind) {
    case 'timeout':
      return 'Generating took too long. Please try again.';
    case 'network':
      return 'Can’t reach the server. Check your connection and try again.';
    case 'parse':
      return 'Got an unexpected response from the server.';
    case 'http':
      if (error.status === 429) {
        return 'You’ve used your free rides for this hour. Sign in to plan as many as you like.';
      }
      if (error.status === 422) {
        return 'Couldn’t build a route from there. Try a different start or distance.';
      }
      if (error.status === 400) {
        return 'That request wasn’t valid. Check the distance and try again.';
      }
      return 'The server had a problem generating the route. Please try again.';
    default:
      return 'Something went wrong. Please try again.';
  }
}

export default function PlanScreen() {
  const theme = useTheme();
  const safeAreaInsets = useSafeAreaInsets();
  const [origin, setOrigin] = useState('');
  const [destination, setDestination] = useState('');
  const [distanceKm, setDistanceKm] = useState('');
  const [curviness, setCurviness] = useState<CurvinessLevel>(3);
  const [locating, setLocating] = useState(true);
  const [geocodeError, setGeocodeError] = useState<GeocodeFailureReason | null>(null);
  const [geocoding, setGeocoding] = useState(false);

  const generate = useGenerateRouteMutation();

  useEffect(() => {
    let cancelled = false;

    async function detectLocation() {
      try {
        const { status } = await Location.requestForegroundPermissionsAsync();
        if (status !== 'granted' || cancelled) return;

        const position = await Location.getCurrentPositionAsync({
          accuracy: Location.Accuracy.High,
        });
        if (cancelled) return;

        const address = await reverseGeocode({
          lat: position.coords.latitude,
          lng: position.coords.longitude,
        });
        if (!cancelled && address) setOrigin(address);
      } catch {
        // permission denied or location unavailable — leave field empty
      } finally {
        if (!cancelled) setLocating(false);
      }
    }

    detectLocation();
    return () => {
      cancelled = true;
    };
  }, []);

  const parsedDistance = parseFloat(distanceKm);
  const distanceValid = !Number.isNaN(parsedDistance) && parsedDistance > 0;
  // `geocoding` matters as much as `isPending`: the geocode await happens before the mutation starts,
  // so without it the button stays live for seconds and a double-tap fires two generations.
  const busy = geocoding || generate.isPending;
  const canPlan = origin.trim().length > 0 && distanceValid && !busy;

  async function handlePlan() {
    setGeocodeError(null);
    generate.reset();
    setGeocoding(true);

    try {
      const result = await geocodeAddress(origin);
      if (!result.ok) {
        setGeocodeError(result.reason);
        return;
      }
      generate.mutate({
        request: { start: result.point, distanceKm: parsedDistance },
        startLabel: origin.trim(),
      });
    } finally {
      setGeocoding(false);
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
          <ThemedText type="title">Plan your{'\n'}ride</ThemedText>
          <BackendStatus />
        </ThemedView>

        <ThemedView style={styles.form}>
          <ThemedView style={styles.section}>
            <ThemedText type="smallBold" themeColor="textSecondary" style={styles.sectionLabel}>
              ROUTE
            </ThemedText>
            <ThemedView type="backgroundElement" style={styles.card}>
              <TextInput
                style={[styles.input, { color: theme.text }]}
                placeholder={locating ? 'Detecting location…' : 'Starting point'}
                placeholderTextColor={theme.textSecondary}
                value={origin}
                onChangeText={setOrigin}
                autoCorrect={false}
                autoCapitalize="words"
                returnKeyType="next"
              />
              <View style={[styles.divider, { backgroundColor: theme.backgroundSelected }]} />
              <TextInput
                style={[styles.input, { color: theme.text }]}
                placeholder="Destination — leave blank for a loop"
                placeholderTextColor={theme.textSecondary}
                value={destination}
                onChangeText={setDestination}
                autoCorrect={false}
                autoCapitalize="words"
                returnKeyType="done"
              />
            </ThemedView>
          </ThemedView>

          <ThemedView style={styles.section}>
            <ThemedText type="smallBold" themeColor="textSecondary" style={styles.sectionLabel}>
              DISTANCE
            </ThemedText>
            <ThemedView type="backgroundElement" style={styles.card}>
              <View style={styles.distanceRow}>
                <TextInput
                  style={[styles.input, styles.distanceInput, { color: theme.text }]}
                  placeholder="e.g. 40"
                  placeholderTextColor={theme.textSecondary}
                  value={distanceKm}
                  onChangeText={setDistanceKm}
                  keyboardType="decimal-pad"
                  returnKeyType="done"
                  accessibilityLabel="Ride distance in kilometres"
                />
                <ThemedText type="default" themeColor="textSecondary" style={styles.distanceUnit}>
                  km
                </ThemedText>
              </View>
            </ThemedView>
          </ThemedView>

          <ThemedView style={styles.section}>
            <View style={styles.sectionLabelRow}>
              <ThemedText type="smallBold" themeColor="textSecondary" style={styles.sectionLabel}>
                CURVINESS
              </ThemedText>
              <ThemedText type="small" themeColor="textSecondary">
                Coming soon
              </ThemedText>
            </View>
            {/* Inactive for S-01 — curviness shaping lands in S-02. Shown as a preview, not wired. */}
            <View pointerEvents="none" style={styles.disabled}>
              <ThemedView type="backgroundElement" style={styles.card}>
                {CURVINESS_OPTIONS.map(({ level, label }, index) => {
                  const selected = curviness === level;
                  return (
                    <Fragment key={level}>
                      <Pressable
                        style={[
                          styles.curvinessRow,
                          selected && { backgroundColor: theme.backgroundSelected },
                        ]}
                        onPress={() => setCurviness(level)}
                        accessibilityRole="radio"
                        accessibilityLabel={label}
                        accessibilityState={{ checked: selected, disabled: true }}>
                        <ThemedText type="default" themeColor={selected ? 'text' : 'textSecondary'}>
                          {label}
                        </ThemedText>
                      </Pressable>
                      {index < CURVINESS_OPTIONS.length - 1 && (
                        <View
                          style={[styles.divider, { backgroundColor: theme.backgroundSelected }]}
                        />
                      )}
                    </Fragment>
                  );
                })}
              </ThemedView>
            </View>
          </ThemedView>

          {(geocodeError !== null || generate.isError) && (
            <ThemedView style={styles.section}>
              <ThemedText type="small" style={styles.errorText}>
                {geocodeError !== null
                  ? geocodeErrorMessage(geocodeError)
                  : planErrorMessage(generate.error!)}
              </ThemedText>
              {/* The quota message names an action, so give the rider that action here rather than
                  leaving them to work out that "sign in" means the Account tab. */}
              {geocodeError === null && generate.isError && isQuotaError(generate.error!) && (
                <Link href="/account">
                  <ThemedText type="linkPrimary">Go to Account</ThemedText>
                </Link>
              )}
            </ThemedView>
          )}

          <Pressable
            style={({ pressed }) => [
              styles.planButton,
              { opacity: canPlan ? (pressed ? 0.8 : 1) : 0.4 },
            ]}
            onPress={handlePlan}
            disabled={!canPlan}
            accessibilityRole="button"
            accessibilityLabel="Plan Route"
            accessibilityState={{ disabled: !canPlan, busy }}>
            <Text style={styles.planButtonLabel}>
              {geocoding ? 'Finding start…' : generate.isPending ? 'Planning…' : 'Plan Route'}
            </Text>
          </Pressable>
        </ThemedView>
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
  section: {
    gap: Spacing.two,
  },
  sectionLabel: {
    letterSpacing: 0.5,
  },
  sectionLabelRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  card: {
    borderRadius: Spacing.three,
    overflow: 'hidden',
  },
  disabled: {
    opacity: 0.45,
  },
  input: {
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
    fontSize: 16,
    lineHeight: 24,
  },
  distanceRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  distanceInput: {
    flex: 1,
  },
  distanceUnit: {
    paddingRight: Spacing.three,
  },
  divider: {
    height: StyleSheet.hairlineWidth,
    marginHorizontal: Spacing.three,
  },
  curvinessRow: {
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
  },
  errorText: {
    color: '#E5484D',
  },
  planButton: {
    backgroundColor: '#208AEF',
    borderRadius: Spacing.five,
    paddingVertical: Spacing.three,
    alignItems: 'center',
  },
  planButtonLabel: {
    color: '#ffffff',
    fontSize: 16,
    lineHeight: 24,
    fontWeight: '600',
  },
});
