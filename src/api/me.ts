import { request } from './client';

/**
 * Shape of `GET /me` on the RideForge backend — the identity the API resolved from the bearer
 * token, not the one the client happens to hold. `email` is nullable because the claim is absent
 * for identities created without one.
 */
export type MeResponse = {
  id: string;
  email: string | null;
};

/**
 * An identity check is a small authenticated GET, not a generation call — it must not sit on the
 * 30s NFR-01 budget when the backend is unreachable. Same reasoning as `HEALTH_TIMEOUT_MS`.
 */
const ME_TIMEOUT_MS = 8_000;

/**
 * Fetch the signed-in rider's identity as the backend sees it. This is the proof the JWT actually
 * validated server-side; a missing or stale token surfaces as an `http` 401.
 */
export function getMe(): Promise<MeResponse> {
  return request<MeResponse>('/me', { auth: true, timeoutMs: ME_TIMEOUT_MS });
}
