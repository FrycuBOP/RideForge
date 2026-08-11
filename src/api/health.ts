import { request } from './client';

/** Shape of `GET /health` on the RideForge backend. */
export type HealthResponse = {
  status: string;
  service: string;
};

/** Health checks should fail fast rather than sit on the full generation budget. */
const HEALTH_TIMEOUT_MS = 8_000;

/** Ping the backend health endpoint. Used to prove connectivity (F-01). */
export function getHealth(): Promise<HealthResponse> {
  return request<HealthResponse>('/health', { timeoutMs: HEALTH_TIMEOUT_MS });
}
