import { useSyncExternalStore } from 'react';

import type { GeneratedRoute, GenerateRequest } from '@/api/route';
import { randomUUID } from '@/lib/uuid';

/** Everything the rider needs to save a ride: the route plus the inputs that produced it. */
export type Ride = {
  /**
   * Idempotency key for `POST /saved-routes`, minted once per generation. Every save of this ride —
   * a double tap, a retry after a timeout — carries the same id, so the server answers with the
   * row it already has instead of creating a second one.
   */
  clientRouteId: string;
  request: GenerateRequest;
  /** What the rider typed as the start (trimmed), used for the saved route's name. */
  startLabel: string | null;
  route: GeneratedRoute;
  /**
   * Supabase user id of the rider who saved this ride, or `null` while unsaved. "Saved" belongs to
   * a rider, not to the device: a different rider signing in on the same device must be offered
   * Save again, and their save is a genuinely new row on their own account.
   */
  savedBy: string | null;
};

/**
 * Tiny in-memory hand-off for the last generated ride. The generate mutation writes it and the
 * results screen reads it, so the (dozens–hundreds of points) geometry never has to be serialized
 * into a navigation URL. Session-only: it is intentionally lost on reload, and the results screen
 * shows an empty state when there is nothing here (e.g. a deep link or refresh).
 *
 * Each write replaces the object rather than mutating it, so the value doubles as a stable
 * `useSyncExternalStore` snapshot.
 */
let lastRide: Ride | null = null;
const listeners = new Set<() => void>();

function emit(): void {
  listeners.forEach((listener) => listener());
}

/** Store a freshly generated ride, minting its `clientRouteId`. Replaces any previous ride. */
export function setLastRoute(ride: Omit<Ride, 'clientRouteId' | 'savedBy'>): void {
  lastRide = { ...ride, clientRouteId: randomUUID(), savedBy: null };
  emit();
}

/**
 * Record that `userId` saved the ride identified by `clientRouteId`. A no-op when the store has
 * since moved on to a newer ride — a slow save must never mark a route the rider did not save.
 */
export function markLastRouteSaved(clientRouteId: string, userId: string): void {
  if (lastRide?.clientRouteId !== clientRouteId) return;
  lastRide = { ...lastRide, savedBy: userId };
  emit();
}

export function getLastRoute(): Ride | null {
  return lastRide;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The current ride, re-rendering the caller whenever it is replaced or marked saved. */
export function useLastRoute(): Ride | null {
  return useSyncExternalStore(subscribe, getLastRoute, getLastRoute);
}
