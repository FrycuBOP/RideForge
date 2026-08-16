import { API_BASE_URL, DEFAULT_TIMEOUT_MS } from './config';
import { ApiError, normalizeError } from './errors';

export type RequestOptions = {
  method?: string;
  body?: unknown;
  timeoutMs?: number;
  /** Optional caller-owned signal; aborting it aborts the request. */
  signal?: AbortSignal;
};

/**
 * Perform a JSON request against the backend. Applies an `AbortController` timeout,
 * throws a normalized {@link ApiError} on any failure (timeout, network, non-2xx, bad
 * JSON), and parses the response body as `T` on success. This is the single place that
 * owns transport, timeout, and error normalization — endpoint functions and query hooks
 * build on top of it.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, timeoutMs = DEFAULT_TIMEOUT_MS, signal } = options;

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  // Chain a caller-provided signal into our controller so either can abort the request.
  if (signal) {
    if (signal.aborted) controller.abort();
    else signal.addEventListener('abort', () => controller.abort(), { once: true });
  }

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      signal: controller.signal,
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch (error) {
    throw normalizeError(error);
  } finally {
    clearTimeout(timeout);
  }

  if (!response.ok) {
    throw new ApiError('http', `Request failed with status ${response.status}`, response.status);
  }

  try {
    return (await response.json()) as T;
  } catch {
    throw new ApiError('parse', 'Failed to parse response body');
  }
}
