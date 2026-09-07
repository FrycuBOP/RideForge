import { Fragment, useEffect, useState } from 'react';
import { Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import * as Location from 'expo-location';
import { router } from 'expo-router';

import { BackendStatus } from '@/components/backend-status';
import { ThemedText } from '@/components/themed-text';
import { ThemedView } from '@/components/themed-view';
import { BottomTabInset, MaxContentWidth, Spacing } from '@/constants/theme';
import { useTheme } from '@/hooks/use-theme';

type CurvinessLevel = 1 | 2 | 3 | 4 | 5;

type NominatimAddress = {
  house_number?: string;
  road?: string;
  quarter?: string;
  suburb?: string;
  city?: string;
  town?: string;
  village?: string;
  municipality?: string;
  county?: string;
};

type NominatimResponse = {
  address: NominatimAddress;
};

const CURVINESS_OPTIONS: { level: CurvinessLevel; label: string }[] = [
  { level: 1, label: 'Highways' },
  { level: 2, label: 'Mixed' },
  { level: 3, label: 'Curvy' },
  { level: 4, label: 'Very Curvy' },
  { level: 5, label: 'Extreme' },
];

function formatAddress(place: Location.LocationGeocodedAddress): string {
  const street = [place.streetNumber, place.street]
    .filter((p): p is string => p !== null)
    .join(' ');
  const locality = place.city ?? place.district ?? place.subregion ?? null;
  return [street || place.name, locality]
    .filter((p): p is string => typeof p === 'string' && p.length > 0)
    .join(', ');
}

export default function PlanScreen() {
  const theme = useTheme();
  const safeAreaInsets = useSafeAreaInsets();
  const [origin, setOrigin] = useState('');
  const [destination, setDestination] = useState('');
  const [curviness, setCurviness] = useState<CurvinessLevel>(3);
  const [locating, setLocating] = useState(true);

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

        let address = '';
        if (Platform.OS === 'web') {
          const response = await fetch(
            `https://nominatim.openstreetmap.org/reverse?lat=${position.coords.latitude}&lon=${position.coords.longitude}&format=json&zoom=18&addressdetails=1`
          );
          const data = (await response.json()) as NominatimResponse;
          const street = [data.address.road, data.address.house_number]
            .filter((p): p is string => typeof p === 'string' && p.length > 0)
            .join(' ');
          const locality =
            data.address.city ??
            data.address.town ??
            data.address.village ??
            data.address.municipality ??
            data.address.county;
          address = [street, locality]
            .filter((p): p is string => typeof p === 'string' && p.length > 0)
            .join(', ');
        } else {
          const [place] = await Location.reverseGeocodeAsync(position.coords);
          if (place) address = formatAddress(place);
        }
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

  const canPlan = origin.trim().length > 0;

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
              CURVINESS
            </ThemedText>
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
                      accessibilityState={{ checked: selected }}>
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
          </ThemedView>

          <Pressable
            style={({ pressed }) => [
              styles.planButton,
              { opacity: canPlan ? (pressed ? 0.8 : 1) : 0.4 },
            ]}
            onPress={() => {
              const isLoop = destination.trim().length === 0;
              console.log('Plan route:', { origin, destination: isLoop ? origin : destination, curviness, isLoop });
              // TEMP (Phase 1 smoke): jump straight to the map results screen so the dev build
              // can verify react-native-maps renders. Phase 3 replaces this with the real flow
              // (geocode origin → generate route → navigate with the result).
              router.push('/result');
            }}
            disabled={!canPlan}
            accessibilityRole="button"
            accessibilityLabel="Plan Route"
            accessibilityState={{ disabled: !canPlan }}>
            <Text style={styles.planButtonLabel}>Plan Route</Text>
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
  card: {
    borderRadius: Spacing.three,
    overflow: 'hidden',
  },
  input: {
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
    fontSize: 16,
    lineHeight: 24,
  },
  divider: {
    height: StyleSheet.hairlineWidth,
    marginHorizontal: Spacing.three,
  },
  curvinessRow: {
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
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
