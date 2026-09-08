import { request } from './client';
import { ApiError } from './errors';

/** A single geographic point, matching the backend's camelCase <c>Coord</c> wire shape. */
export type GeoPoint = { lat: number; lng: number };

/** Request body for <c>POST /route/generate</c>: a start point + requested ride distance (km). */
export type GenerateRequest = {
  start: GeoPoint;
  distanceKm: number;
};

/** Success body of <c>POST /route/generate</c> — the shape the map results screen consumes. */
export type GeneratedRoute = {
  geometry: GeoPoint[];
  distanceMeters: number;
  durationSeconds: number;
};

/**
 * Generate a loop route from a start point + requested distance. A POST with a side-effecting,
 * billed provider call — drive it from a mutation, not a query. Uses the default 30s timeout
 * (NFR-01). Throws a normalized {@link import('./errors').ApiError} on any failure.
 */
export async function generateRoute(req: GenerateRequest): Promise<GeneratedRoute> {
  const body = await request<GeneratedRoute>('/route/generate', { method: 'POST', body: req });

  // `request` casts the parsed JSON to T without checking it, so a 200 carrying the wrong shape (a
  // proxy interstitial, a backend regression) would otherwise reach the map screen and crash it on
  // `geometry.length`. Convert that into the `parse` kind the error UI already renders.
  if (!isGeneratedRoute(body)) {
    throw new ApiError('parse', 'Route response did not match the expected shape');
  }

  return body;
}

function isGeneratedRoute(value: unknown): value is GeneratedRoute {
  if (typeof value !== 'object' || value === null) return false;
  const { geometry, distanceMeters, durationSeconds } = value as Partial<GeneratedRoute>;
  return (
    Array.isArray(geometry) &&
    geometry.every((p) => typeof p?.lat === 'number' && typeof p?.lng === 'number') &&
    Number.isFinite(distanceMeters) &&
    Number.isFinite(durationSeconds)
  );
}
