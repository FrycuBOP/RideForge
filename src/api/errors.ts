/** Machine-distinguishable failure modes for any backend request. */
export type ApiErrorKind = 'network' | 'timeout' | 'http' | 'parse';

/**
 * Normalized error thrown by the API client. Screens branch on `kind` to render
 * user-facing messages and decide whether to offer a retry. `status` is set only
 * for `http` failures.
 */
export class ApiError extends Error {
  readonly kind: ApiErrorKind;
  readonly status?: number;

  constructor(kind: ApiErrorKind, message: string, status?: number) {
    super(message);
    this.name = 'ApiError';
    this.kind = kind;
    this.status = status;
  }
}

/**
 * Map an unknown thrown value into an `ApiError`. Used by the client to wrap failures
 * from `fetch` itself: an `AbortError` (our timeout firing) becomes `timeout`, a fetch
 * network `TypeError` becomes `network`, and an existing `ApiError` passes through.
 * The `http` and `parse` kinds are constructed directly by the client, which has the
 * response context those need.
 */
export function normalizeError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;

  if (error instanceof Error) {
    if (error.name === 'AbortError') {
      return new ApiError('timeout', 'Request timed out');
    }
    // fetch rejects with a TypeError on network failure (DNS, offline, refused connection).
    if (error instanceof TypeError) {
      return new ApiError('network', error.message || 'Network request failed');
    }
    return new ApiError('network', error.message);
  }

  return new ApiError('network', 'Unknown network error');
}
