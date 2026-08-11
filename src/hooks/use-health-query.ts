import { useQuery } from '@tanstack/react-query';

import { ApiError, getHealth, type HealthResponse } from '@/api';

/**
 * Query the backend health endpoint. Its `error` is typed as {@link ApiError} so consumers
 * can branch on `error.kind`. First query hook — the template S-01 copies for generation.
 */
export function useHealthQuery() {
  return useQuery<HealthResponse, ApiError>({
    queryKey: ['health'],
    queryFn: getHealth,
  });
}
