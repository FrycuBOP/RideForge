import { request } from './client';
import { ApiError } from './errors';
import type { GeoPoint } from './route';

/**
 * Request body for `POST /saved-routes`, mirroring the backend's `SaveRouteRequestDto`. There is
 * deliberately no owner field: the server takes the owner from the verified token, never the body.
 */
export type SaveRouteRequest = {
  /** Idempotency key: repeats with the same id return the rider's existing row. */
  clientRouteId: string;
  start: GeoPoint;
  /** At most {@link MAX_START_LABEL_LENGTH} UTF-16 code units, or the server answers 400. */
  startLabel: string | null;
  requestedDistanceKm: number;
  geometry: GeoPoint[];
  distanceMeters: number;
  durationSeconds: number;
};

/** Response body for both a fresh save (201) and a repeat that found the existing row (200). */
export type SavedRoute = {
  id: string;
  name: string;
  createdAt: string;
};

/** Matches the backend's `SavedRouteValidation.MaxStartLabelLength` (`start_label varchar(200)`). */
export const MAX_START_LABEL_LENGTH = 200;

/**
 * A save is one insert, not a generation — it must not sit on the 30s NFR-01 budget. Long enough
 * for a cold pooler connection, short enough that a dead network surfaces as a retryable timeout.
 */
const SAVE_TIMEOUT_MS = 15_000;

/**
 * Save a generated route to the signed-in rider's account (FR-009). Idempotent per
 * `clientRouteId`: 201 for a new row and 200 for an existing one are both success. A missing or
 * stale token surfaces as an `http` 401.
 */
export async function saveRoute(req: SaveRouteRequest): Promise<SavedRoute> {
  const body = await request<SavedRoute>('/saved-routes', {
    method: 'POST',
    body: req,
    auth: true,
    timeoutMs: SAVE_TIMEOUT_MS,
  });

  // Same guard as `generateRoute`: `request` casts without checking, and a 2xx carrying the wrong
  // shape would otherwise read as a successful save.
  if (!isSavedRoute(body)) {
    throw new ApiError('parse', 'Saved route response did not match the expected shape');
  }

  return body;
}

function isSavedRoute(value: unknown): value is SavedRoute {
  if (typeof value !== 'object' || value === null) return false;
  const { id, name, createdAt } = value as Partial<SavedRoute>;
  return typeof id === 'string' && typeof name === 'string' && typeof createdAt === 'string';
}
