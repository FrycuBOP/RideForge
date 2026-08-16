import { QueryClient } from '@tanstack/react-query';

/**
 * Shared query client. MVP defaults: retry once (ride out a transient blip), and a modest
 * staleTime so screen re-focus doesn't hammer the backend. No persistence.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
    },
  },
});
