import { request } from './client';
import { ApiError } from './errors';
import type { GeneratedRoute, GenerateRequest, GeoPoint } from './route';

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
 * A generated ride, as much of it as saving needs. Structural on purpose: the result store's `Ride`
 * satisfies it, without this layer having to know that the store exists.
 */
export type SaveRouteInput = {
  clientRouteId: string;
  request: GenerateRequest;
  startLabel: string | null;
  route: GeneratedRoute;
};

/**
 * Cut a start label to the server's column limit without splitting a surrogate pair — a lone
 * surrogate is not valid UTF-16 and would turn a long-but-fine label into a failed save.
 */
function fitStartLabel(label: string | null): string | null {
  if (label === null || label.length <= MAX_START_LABEL_LENGTH) return label;
  const cut = label.slice(0, MAX_START_LABEL_LENGTH);
  const last = cut.charCodeAt(cut.length - 1);
  return last >= 0xd800 && last <= 0xdbff ? cut.slice(0, -1) : cut;
}

/** Build the `POST /saved-routes` body for a generated ride. */
export function toSaveRouteRequest(ride: SaveRouteInput): SaveRouteRequest {
  return {
    clientRouteId: ride.clientRouteId,
    start: ride.request.start,
    startLabel: fitStartLabel(ride.startLabel),
    requestedDistanceKm: ride.request.distanceKm,
    geometry: ride.route.geometry,
    distanceMeters: ride.route.distanceMeters,
    durationSeconds: ride.route.durationSeconds,
  };
}

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
