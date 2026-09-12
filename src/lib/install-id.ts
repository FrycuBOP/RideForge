import AsyncStorage from '@react-native-async-storage/async-storage';

import { randomUUID } from '@/lib/uuid';

const STORAGE_KEY = 'rideforge.install-id';

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
