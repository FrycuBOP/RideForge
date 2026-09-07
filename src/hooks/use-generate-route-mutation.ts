import { useMutation } from '@tanstack/react-query';
import { router } from 'expo-router';

import { ApiError } from '@/api';
import { generateRoute, type GeneratedRoute, type GenerateRequest } from '@/api/route';
import { setLastRoute } from '@/lib/route-result-store';

/**
 * Mutation that generates a route from a start + distance. Modeled as a mutation (not a query)
 * because it is a user-triggered POST with a side-effecting, billed provider call. `error` is typed
 * as {@link ApiError} so the screen can branch on `error.kind` for the FR-005 error UI. On success
 * it stashes the route for the results screen and navigates to `/result`.
 */
export function useGenerateRouteMutation() {
  return useMutation<GeneratedRoute, ApiError, GenerateRequest>({
    mutationFn: generateRoute,
    onSuccess: (route) => {
      setLastRoute(route);
      router.push('/result');
    },
  });
}
