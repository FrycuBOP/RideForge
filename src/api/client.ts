import { getInstallId } from '@/lib/install-id';
import { supabase } from '@/lib/supabase';

import { API_BASE_URL, DEFAULT_TIMEOUT_MS } from './config';
import { ApiError, normalizeError } from './errors';

export type RequestOptions = {
  method?: string;
  body?: unknown;
  timeoutMs?: number;
  /** Optional caller-owned signal; aborting it aborts the request. */
  signal?: AbortSignal;
  /**
   * Attach the signed-in rider's access token as `Authorization: Bearer …`. Defaults to `false`,
   * so every existing call site keeps sending anonymous requests unchanged.
   *
   * When there is no session the request still goes out, unauthenticated, and the backend answers
   * 401. That keeps one failure path — a stale token and a missing token look the same to the
   * caller — instead of a client-side throw the screens would have to branch on separately.
   */
  auth?: boolean;
};

/**
 * Perform a JSON request against the backend. Applies an `AbortController` timeout,
 * throws a normalized {@link ApiError} on any failure (timeout, network, non-2xx, bad
 * JSON), and parses the response body as `T` on success. This is the single place that
 * owns transport, timeout, and error normalization — endpoint functions and query hooks
 * build on top of it.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, timeoutMs = DEFAULT_TIMEOUT_MS, signal, auth = false } = options;

  const headers: Record<string, string> = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  // Sent on every request, authenticated or not: it is what lets the backend count an anonymous
  // rider's generations against this install rather than against a shared carrier IP. The existing
  // CORS policy allows any header, so the web build needs no backend change to send it.
  headers['X-RideForge-Install'] = await getInstallId();

  if (auth) {
    // Read the session rather than the provider's React state: `request` is called from query
    // functions and mutations, which can outlive the render that started them.
    const { data } = await supabase.auth.getSession();
    const accessToken = data.session?.access_token;
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  }

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
      headers,
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
