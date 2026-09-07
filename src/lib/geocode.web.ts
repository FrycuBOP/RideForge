import type { GeoPoint } from '@/api/route';

type NominatimSearchResult = { lat: string; lon: string };

/**
 * Forward-geocode a typed address to coordinates (web). expo-location has no web support, so use
 * Nominatim search — the same provider the Plan screen's web reverse-geocode already uses. Kept to
 * a single low-rate lookup per Nominatim's usage policy. Returns <c>null</c> when nothing matches.
 */
export async function geocodeAddress(query: string): Promise<GeoPoint | null> {
  const trimmed = query.trim();
  if (trimmed.length === 0) return null;

  try {
    const response = await fetch(
      `https://nominatim.openstreetmap.org/search?format=json&limit=1&q=${encodeURIComponent(trimmed)}`
    );
    const data = (await response.json()) as NominatimSearchResult[];
    const first = data[0];
    return first ? { lat: parseFloat(first.lat), lng: parseFloat(first.lon) } : null;
  } catch {
    return null;
  }
}
