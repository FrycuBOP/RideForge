export { API_BASE_URL, DEFAULT_TIMEOUT_MS } from './config';
export { ApiError, normalizeError } from './errors';
export type { ApiErrorKind } from './errors';
export { request } from './client';
export type { RequestOptions } from './client';
export { getHealth } from './health';
export type { HealthResponse } from './health';
