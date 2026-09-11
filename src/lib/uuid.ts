/**
 * A UUID, preferring `expo-crypto` and falling back to `Math.random` when its native module is
 * absent.
 *
 * `expo-crypto` is required lazily rather than imported at module scope on purpose. A top-level
 * import evaluates `requireNativeModule('ExpoCrypto')` at load time, which throws on a dev build
 * that predates the dependency — and because this helper sits in the chain
 * `install-id → api/client → api/index → use-me-query → account.tsx`, that throw took every route
 * screen's default export with it and the app failed to boot with an error naming neither the
 * module nor the feature. See `context/foundation/lessons.md`.
 *
 * The fallback's weaker randomness is acceptable for the two ids minted today, and only because
 * neither is a secret:
 * - the install id is a quota partition key — guessing another install's id gains an attacker
 *   nothing but a share of someone else's spent allowance;
 * - a saved route's `clientRouteId` is an idempotency key the server scopes per owner — a
 *   collision with another rider's id creates a separate row, never a shared one.
 *
 * Do not use this helper for anything that must be unpredictable.
 */
export function randomUUID(): string {
  try {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    return (require('expo-crypto') as typeof import('expo-crypto')).randomUUID();
  } catch {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
      const value = (Math.random() * 16) | 0;
      return (char === 'x' ? value : (value & 0x3) | 0x8).toString(16);
    });
  }
}
