import * as Location from 'expo-location';

import type { GeoPoint } from '@/api/route';

/** Why a lookup produced no coordinates. Each reason needs different advice for the rider. */
export type GeocodeFailureReason = 'not-found' | 'permission' | 'unavailable';

/**
 * Outcome of a forward-geocode. A discriminated result rather than `GeoPoint | null`: "no such
 * address" and "this device can't geocode at all" are different problems, and collapsing them sends
 * the rider off to edit an address that was fine. Mirrored in `geocode.web.ts`.
 */
export type GeocodeResult =
  | { ok: true; point: GeoPoint }
  | { ok: false; reason: GeocodeFailureReason };

/**
 * Forward-geocode a typed address to coordinates (native). Uses expo-location's on-device
 * geocoder — mirroring the reverse-geocode split the Plan screen already uses (native vs web).
 */
export async function geocodeAddress(query: string): Promise<GeocodeResult> {
  const trimmed = query.trim();
  if (trimmed.length === 0) return { ok: false, reason: 'not-found' };

  try {
    const [first] = await Location.geocodeAsync(trimmed);
    return first
      ? { ok: true, point: { lat: first.latitude, lng: first.longitude } }
      : { ok: false, reason: 'not-found' };
  } catch {
    // Classify by the actual permission state rather than the exception text: Android's geocoder
    // throws both when foreground permission is missing and when no geocoder is present on the
    // device (AOSP builds, many emulators), and those need opposite advice. Checking the permission
    // is deterministic where message matching would be guesswork.
    return { ok: false, reason: (await hasForegroundPermission()) ? 'unavailable' : 'permission' };
  }
}

/**
 * Reverse-geocode coordinates to a short display address (native). Returns an empty string when
 * nothing usable comes back — the caller just leaves the origin field for the rider to type.
 */
export async function reverseGeocode(point: GeoPoint): Promise<string> {
  try {
    const [place] = await Location.reverseGeocodeAsync({
      latitude: point.lat,
      longitude: point.lng,
    });
    return place ? formatAddress(place) : '';
  } catch {
    return '';
  }
}

function formatAddress(place: Location.LocationGeocodedAddress): string {
  const street = [place.streetNumber, place.street].filter(isNonEmpty).join(' ');
  const locality = place.city ?? place.district ?? place.subregion ?? null;
  return [street || place.name, locality].filter(isNonEmpty).join(', ');
}

function isNonEmpty(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.length > 0;
}

async function hasForegroundPermission(): Promise<boolean> {
  try {
    const { granted } = await Location.getForegroundPermissionsAsync();
    return granted;
  } catch {
    // Can't tell — report the less accusatory of the two.
    return true;
  }
}
