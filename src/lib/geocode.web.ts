import type { GeoPoint } from '@/api/route';

/** Mirrors `geocode.ts` — platform siblings must expose the same shape. */
export type GeocodeFailureReason = 'not-found' | 'permission' | 'unavailable';

/** Mirrors `geocode.ts`. `permission` never occurs on web: there is no geocoder permission. */
export type GeocodeResult =
  | { ok: true; point: GeoPoint }
  | { ok: false; reason: GeocodeFailureReason };

type NominatimSearchResult = { lat: string; lon: string };

type NominatimAddress = {
  house_number?: string;
  road?: string;
  city?: string;
  town?: string;
  village?: string;
  municipality?: string;
  county?: string;
};

/** Nominatim can be slow or silently stall; without this the Plan button would wait forever. */
const LOOKUP_TIMEOUT_MS = 10_000;

/**
 * Forward-geocode a typed address to coordinates (web). expo-location has no web support, so use
 * Nominatim search — the same provider the Plan screen's web reverse-geocode already uses. Kept to a
 * single low-rate lookup, which is the part of Nominatim's usage policy this code can honour: the
 * policy's identification requirement is satisfied by the browser's own `Referer`, since `User-Agent`
 * is a forbidden header name that `fetch` is not permitted to set.
 */
export async function geocodeAddress(query: string): Promise<GeocodeResult> {
  const trimmed = query.trim();
  if (trimmed.length === 0) return { ok: false, reason: 'not-found' };

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), LOOKUP_TIMEOUT_MS);

  try {
    const response = await fetch(
      `https://nominatim.openstreetmap.org/search?format=json&limit=1&q=${encodeURIComponent(trimmed)}`,
      { signal: controller.signal, headers: { Accept: 'application/json' } }
    );
    // Without this a 403/429 HTML body would throw inside .json() and be reported as "no such
    // address" — blaming the rider's input for a rate limit.
    if (!response.ok) return { ok: false, reason: 'unavailable' };

    const data = (await response.json()) as unknown;
    const first = Array.isArray(data) ? (data[0] as NominatimSearchResult | undefined) : undefined;
    if (!first) return { ok: false, reason: 'not-found' };

    const lat = parseFloat(first.lat);
    const lng = parseFloat(first.lon);
    // parseFloat yields NaN on a shape change; NaN serializes to JSON null, which fails binding into
    // the backend's non-nullable Coord and surfaces as a 400 about the distance field.
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) return { ok: false, reason: 'unavailable' };

    return { ok: true, point: { lat, lng } };
  } catch {
    return { ok: false, reason: 'unavailable' };
  } finally {
    clearTimeout(timeout);
  }
}

/**
 * Reverse-geocode coordinates to a short display address (web). Mirrors `geocode.ts`; shares this
 * module's timeout and `response.ok` handling instead of the bare fetch this replaced in the Plan
 * screen. Returns an empty string when nothing usable comes back.
 */
export async function reverseGeocode(point: GeoPoint): Promise<string> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), LOOKUP_TIMEOUT_MS);

  try {
    const response = await fetch(
      `https://nominatim.openstreetmap.org/reverse?lat=${point.lat}&lon=${point.lng}&format=json&zoom=18&addressdetails=1`,
      { signal: controller.signal, headers: { Accept: 'application/json' } }
    );
    if (!response.ok) return '';

    // Nominatim answers unresolvable coordinates with {"error": ...} and no `address`, so this must
    // be read defensively rather than asserted into a type.
    const data = (await response.json()) as { address?: NominatimAddress };
    const address = data.address;
    if (!address) return '';

    const street = [address.road, address.house_number].filter(isNonEmpty).join(' ');
    const locality =
      address.city ?? address.town ?? address.village ?? address.municipality ?? address.county;
    return [street, locality].filter(isNonEmpty).join(', ');
  } catch {
    return '';
  } finally {
    clearTimeout(timeout);
  }
}

function isNonEmpty(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.length > 0;
}
