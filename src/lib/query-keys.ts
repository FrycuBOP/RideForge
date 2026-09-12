/**
 * Query keys for the authenticated surfaces of the app.
 *
 * They live here rather than beside the hooks because `SessionProvider` needs the prefix to sweep
 * the cache on sign-out, and importing a hook from the provider would close an import cycle
 * (`use-saved-routes-query` → `use-session` → `session-provider`). A module with no imports of its
 * own cannot.
 *
 * Every key carries the rider id. That is what makes a cross-rider cache bleed structurally
 * impossible instead of dependent on the sign-out sweep firing: two riders can never collide on one
 * key, so there is nothing for the next rider on this device to inherit. Contrast `['me']`, which is
 * keyed by endpoint and therefore *does* depend on that sweep.
 */

/**
 * The `/me` lookup. Keyed by endpoint rather than by rider, which is exactly why the sign-out sweep
 * has to drop it by hand — see the contrast drawn above. It lives here so the sweep and the hook
 * cannot disagree about the spelling.
 */
export const ME_KEY = 'me' as const;

export function meKey() {
  return [ME_KEY] as const;
}

/** Root segment shared by every saved-routes key — what the sign-out sweep removes. */
export const SAVED_ROUTES_KEY = 'saved-routes' as const;

/** One rider's list. Also the prefix the save mutation invalidates when a new route lands. */
export function savedRoutesKey(userId: string) {
  return [SAVED_ROUTES_KEY, userId] as const;
}

/** One saved route, for one rider. Nested under the list key so invalidating the list covers it. */
export function savedRouteKey(userId: string, routeId: string) {
  return [SAVED_ROUTES_KEY, userId, routeId] as const;
}
