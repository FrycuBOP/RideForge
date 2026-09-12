import { useQuery } from '@tanstack/react-query';

import { ApiError, getMe, type MeResponse } from '@/api';
import { meKey } from '@/lib/query-keys';

import { useSession } from './use-session';

/**
 * Query the backend for the identity it resolved from the current token. Follows the
 * `useHealthQuery` template, with one addition: it runs only while a session exists, so a
 * signed-out rider never fires a request that can only ever come back 401.
 */
export function useMeQuery() {
  const { session } = useSession();

  return useQuery<MeResponse, ApiError>({
    queryKey: meKey(),
    queryFn: getMe,
    enabled: session !== null,
  });
}
