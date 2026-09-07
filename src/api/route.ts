import { request } from './client';

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
export function generateRoute(req: GenerateRequest): Promise<GeneratedRoute> {
  return request<GeneratedRoute>('/route/generate', { method: 'POST', body: req });
}
