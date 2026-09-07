import type { GeneratedRoute } from '@/api/route';

/**
 * Tiny in-memory hand-off for the last generated route. The generate mutation writes it and the
 * results screen reads it, so the (dozens–hundreds of points) geometry never has to be serialized
 * into a navigation URL. Session-only: it is intentionally lost on reload, and the results screen
 * shows an empty state when there is nothing here (e.g. a deep link or refresh).
 */
let lastRoute: GeneratedRoute | null = null;

export function setLastRoute(route: GeneratedRoute): void {
  lastRoute = route;
}

export function getLastRoute(): GeneratedRoute | null {
  return lastRoute;
}
