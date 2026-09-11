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

/**
 * One row of `GET /saved-routes`, mirroring the backend's `SavedRouteSummaryDto`. Deliberately
 * geometry-free: the server never reads that column for a list, so the whole response stays a few
 * KB no matter how many rides a rider has saved.
 */
export type SavedRouteSummary = {
  id: string;
  name: string;
  distanceMeters: number;
  durationSeconds: number;
  /** ISO-8601, as the server wrote it. Formatted for riders by `formatSavedAt`. */
  createdAt: string;
};

/**
 * One saved ride in full, mirroring the backend's `SavedRouteDetailDto` — geometry included, which
 * is what makes it drawable again (FR-010).
 */
export type SavedRouteDetail = {
  id: string;
  name: string;
  start: GeoPoint;
  startLabel: string | null;
  requestedDistanceKm: number;
  distanceMeters: number;
  durationSeconds: number;
  geometry: GeoPoint[];
  createdAt: string;
};

/**
 * The list is metadata for at most 50 rows — a few KB. A shorter budget than the save's: there is
 * no large body to push, so anything slower than this is a dead network, not a big response.
 */
const LIST_TIMEOUT_MS = 10_000;

/**
 * One ride's geometry is up to ~600 KB, so the detail read gets the same budget as the save that
 * wrote it rather than the 30s generation default — it is a transfer, not a provider call.
 */
const DETAIL_TIMEOUT_MS = SAVE_TIMEOUT_MS;

/**
 * List the signed-in rider's saved routes, newest first (FR-010). The server caps and orders; this
 * only unwraps the envelope. A missing or stale token surfaces as an `http` 401.
 */
export async function listSavedRoutes(): Promise<SavedRouteSummary[]> {
  const body = await request<unknown>('/saved-routes', {
    auth: true,
    timeoutMs: LIST_TIMEOUT_MS,
  });

  // Same guard as `saveRoute`: `request` casts without checking, so a 2xx carrying the wrong shape
  // would otherwise reach the list screen and crash it on a missing field.
  if (!isSavedRouteListResponse(body)) {
    throw new ApiError('parse', 'Saved routes response did not match the expected shape');
  }

  return body.items;
}

/**
 * Fetch one saved ride by id, geometry included, so the revisit screen can draw it. A route that
 * does not exist *or* belongs to another rider both answer `http` 404 — the server deliberately
 * gives one answer for both, so this layer cannot tell them apart either.
 */
export async function getSavedRoute(id: string): Promise<SavedRouteDetail> {
  const body = await request<unknown>(`/saved-routes/${encodeURIComponent(id)}`, {
    auth: true,
    timeoutMs: DETAIL_TIMEOUT_MS,
  });

  if (!isSavedRouteDetail(body)) {
    throw new ApiError('parse', 'Saved route response did not match the expected shape');
  }

  return body;
}

function isGeoPoint(value: unknown): value is GeoPoint {
  if (typeof value !== 'object' || value === null) return false;
  const { lat, lng } = value as Partial<GeoPoint>;
  return Number.isFinite(lat) && Number.isFinite(lng);
}

function isSavedRouteSummary(value: unknown): value is SavedRouteSummary {
  if (typeof value !== 'object' || value === null) return false;
  const { id, name, distanceMeters, durationSeconds, createdAt } =
    value as Partial<SavedRouteSummary>;
  return (
    typeof id === 'string' &&
    typeof name === 'string' &&
    Number.isFinite(distanceMeters) &&
    Number.isFinite(durationSeconds) &&
    typeof createdAt === 'string'
  );
}

/**
 * The envelope check is the point of the envelope: a bare array would leave nowhere to add a
 * `cursor` later without this guard rejecting the very response it was meant to accept.
 */
function isSavedRouteListResponse(value: unknown): value is { items: SavedRouteSummary[] } {
  if (typeof value !== 'object' || value === null) return false;
  const { items } = value as { items?: unknown };
  return Array.isArray(items) && items.every(isSavedRouteSummary);
}

function isSavedRouteDetail(value: unknown): value is SavedRouteDetail {
  if (typeof value !== 'object' || value === null) return false;
  const route = value as Partial<SavedRouteDetail>;
  return (
    typeof route.id === 'string' &&
    typeof route.name === 'string' &&
    isGeoPoint(route.start) &&
    (route.startLabel === null || typeof route.startLabel === 'string') &&
    Number.isFinite(route.requestedDistanceKm) &&
    Number.isFinite(route.distanceMeters) &&
    Number.isFinite(route.durationSeconds) &&
    Array.isArray(route.geometry) &&
    route.geometry.every(isGeoPoint) &&
    typeof route.createdAt === 'string'
  );
}
