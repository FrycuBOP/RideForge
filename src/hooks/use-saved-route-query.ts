import { useQuery } from '@tanstack/react-query';

import { ApiError, getSavedRoute, type SavedRouteDetail } from '@/api';
import { savedRouteKey } from '@/lib/query-keys';

import { useSession } from './use-session';

/**
 * Query one saved ride by id, geometry included, so the revisit screen can draw it again. Keyed by
 * rider for the same reason the list is (see `@/lib/query-keys`), and disabled while signed out or
 * before an id is known — a route param can be missing on the first render of a deep link, and a
 * request without an id can only fail.
 *
 * A route that does not exist and one owned by another rider both surface as `http` 404: the server
 * gives one answer for both so the endpoint cannot be used to probe for other riders' route ids.
 */
export function useSavedRouteQuery(routeId: string | undefined) {
  const { session, user } = useSession();
  const id = routeId ?? '';

  return useQuery<SavedRouteDetail, ApiError>({
    queryKey: savedRouteKey(user?.id ?? 'signed-out', id),
    queryFn: ({ signal }) => getSavedRoute(id, signal),
    enabled: session !== null && id.length > 0,
    // A saved ride is immutable: the runtime role holds no UPDATE grant, so nothing can change this
    // row once written. Re-fetching it on the global 30s staleTime would re-download ~600 KB of
    // geometry to arrive at the same bytes. The save mutation invalidates the list key, which is the
    // only event that can change what a rider has saved.
    staleTime: Infinity,
  });
}
