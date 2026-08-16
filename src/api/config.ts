/**
 * Backend connection config.
 *
 * The base URL comes from `EXPO_PUBLIC_API_URL` when set (e.g. a local dev or staging
 * backend), falling back to the live Railway production URL so the app works with zero
 * configuration. `EXPO_PUBLIC_*` values are inlined at build time, so changing
 * `EXPO_PUBLIC_API_URL` requires restarting the dev server with `--clear`.
 */
export const API_BASE_URL: string =
  process.env.EXPO_PUBLIC_API_URL ?? 'https://rideforge-api-production.up.railway.app';

/**
 * Default request timeout. Sized to the 30s route-generation budget (NFR-01); short-lived
 * calls (e.g. the health check) pass their own smaller `timeoutMs`.
 */
export const DEFAULT_TIMEOUT_MS = 30_000;
