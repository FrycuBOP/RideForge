import * as Location from 'expo-location';

import type { GeoPoint } from '@/api/route';

/**
 * Forward-geocode a typed address to coordinates (native). Uses expo-location's on-device
 * geocoder — mirroring the reverse-geocode split the Plan screen already uses (native vs web).
 * Returns <c>null</c> when nothing matches so the caller can show a friendly error.
 */
export async function geocodeAddress(query: string): Promise<GeoPoint | null> {
  const trimmed = query.trim();
  if (trimmed.length === 0) return null;

  try {
    const [first] = await Location.geocodeAsync(trimmed);
    return first ? { lat: first.latitude, lng: first.longitude } : null;
  } catch {
    return null;
  }
}
