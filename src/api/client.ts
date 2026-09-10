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
 * Build the outgoing headers. Split out of {@link request} so the whole preparation can be raced
 * against the request deadline as one unit.
 */
async function buildHeaders(body: unknown, auth: boolean): Promise<Record<string, string>> {
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

  return headers;
}

/** An error `normalizeError` maps to the `timeout` kind — it keys on the name, not the type. */
function timedOut(): Error {
  const error = new Error('Request timed out while preparing');
  error.name = 'AbortError';
  return error;
}

/**
 * Reject as soon as `signal` aborts, even if `work` has not settled.
 *
 * The underlying work is not cancelled — neither AsyncStorage nor supabase-js takes a signal, so
 * it runs to completion in the background and its result is dropped. What this bounds is how long
 * the *caller* waits, which is the part a stuck request screen depends on.
 */
function withDeadline<T>(work: Promise<T>, signal: AbortSignal): Promise<T> {
  if (signal.aborted) return Promise.reject(timedOut());

  return new Promise<T>((resolve, reject) => {
    const onAbort = () => reject(timedOut());
    signal.addEventListener('abort', onAbort, { once: true });
    work.then(resolve, reject).finally(() => signal.removeEventListener('abort', onAbort));
  });
}

/**
 * Perform a JSON request against the backend. Applies an `AbortController` timeout,
 * throws a normalized {@link ApiError} on any failure (timeout, network, non-2xx, bad
 * JSON), and parses the response body as `T` on success. This is the single place that
 * owns transport, timeout, and error normalization — endpoint functions and query hooks
 * build on top of it.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, timeoutMs = DEFAULT_TIMEOUT_MS, signal, auth = false } = options;

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  // Chain a caller-provided signal into our controller so either can abort the request. Named so
  // the `finally` can detach it: with an anonymous handler, a caller reusing one long-lived signal
  // across many requests accumulates listeners that each retain a dead controller.
  const forwardAbort = () => controller.abort();
  if (signal) {
    if (signal.aborted) controller.abort();
    else signal.addEventListener('abort', forwardAbort, { once: true });
  }

  let response: Response;
  try {
    // Header preparation belongs to the request's time budget, not to a free prelude before it.
    // Both reads can block: `getInstallId` hits storage, and `getSession` performs a *network*
    // token refresh when the access token has expired. Neither accepts an AbortSignal, so simply
    // moving them inside this try would put them under `normalizeError` without bounding them —
    // and an indefinite hang here strands the Plan screen on "Planning…" until the app restarts.
    const headers = await withDeadline(buildHeaders(body, auth), controller.signal);

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
    signal?.removeEventListener('abort', forwardAbort);
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
