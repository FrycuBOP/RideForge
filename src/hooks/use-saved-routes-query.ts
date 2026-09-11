import { useQuery } from '@tanstack/react-query';

import { ApiError, listSavedRoutes, type SavedRouteSummary } from '@/api';
import { savedRoutesKey } from '@/lib/query-keys';

import { useSession } from './use-session';

/**
 * Query the signed-in rider's saved routes (FR-010). Follows `useMeQuery`'s template — including
 * `enabled`, so a signed-out rider never fires a request that can only come back 401 — with one
 * difference that matters: the key carries the rider id. See `@/lib/query-keys`.
 *
 * Disabled while signed out, so `user` is non-null whenever the query function actually runs; the
 * placeholder key below is never fetched under.
 */
export function useSavedRoutesQuery() {
  const { session, user } = useSession();

  return useQuery<SavedRouteSummary[], ApiError>({
    queryKey: savedRoutesKey(user?.id ?? 'signed-out'),
    queryFn: listSavedRoutes,
    enabled: session !== null,
  });
}
