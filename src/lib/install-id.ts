import AsyncStorage from '@react-native-async-storage/async-storage';

const STORAGE_KEY = 'rideforge.install-id';

/**
 * A UUID, preferring `expo-crypto` and falling back to `Math.random` when its native module is
 * absent.
 *
 * `expo-crypto` is required lazily rather than imported at module scope on purpose. A top-level
 * import evaluates `requireNativeModule('ExpoCrypto')` at load time, which throws on a dev build
 * that predates the dependency — and because this module sits in the chain
 * `install-id → api/client → api/index → use-me-query → account.tsx`, that throw took every route
 * screen's default export with it and the app failed to boot with an error naming neither the
 * module nor the feature. See `context/foundation/lessons.md`.
 *
 * The fallback's weaker randomness is acceptable *here specifically*: this value is a partition
 * key, not a secret. Guessing another install's id gains an attacker nothing but a share of
 * someone else's spent allowance. Do not reuse this helper for anything that must be
 * unpredictable.
 */
function randomUUID(): string {
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

/**
 * In-flight or resolved identifier. Held as the promise, not the value, so concurrent first calls
 * share one storage round-trip instead of racing to generate two different ids and each writing
 * its own — the loser's requests would then be counted against a partition nothing else uses.
 */
let cached: Promise<string> | null = null;

async function resolveInstallId(): Promise<string> {
  try {
    const stored = await AsyncStorage.getItem(STORAGE_KEY);
    if (stored) return stored;

    const created = randomUUID();
    await AsyncStorage.setItem(STORAGE_KEY, created);
    return created;
  } catch {
    // Storage can fail (quota, a corrupt store, a web build with site data blocked). A per-process
    // id still partitions this rider away from everyone else for the life of the app; it just does
    // not survive a restart. That is strictly better than sending nothing, which drops the caller
    // onto the shared IP fallback.
    return randomUUID();
  }
}

/**
 * A stable per-install identifier, used by the backend to count anonymous generations against one
 * rider rather than one IP (see `GenerationQuotaOptions`). Generated on first call and persisted;
 * cached in memory thereafter.
 *
 * This is deliberately not a security control — a rider who reinstalls or clears app data gets a
 * fresh allowance. It is a speed bump that keeps honest riders off each other's counters.
 */
export function getInstallId(): Promise<string> {
  // Drop the memo if it rejects. Caching a rejected promise would make one transient failure fail
  // every later request for the lifetime of the process — and since `request()` awaits this before
  // anything else, that means every API call, not just the quota.
  cached ??= resolveInstallId().catch((error) => {
    cached = null;
    throw error;
  });

  return cached;
}
