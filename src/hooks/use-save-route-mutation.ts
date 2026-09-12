import { useMutation, useQueryClient } from '@tanstack/react-query';

import { ApiError, saveRoute, type SavedRoute, type SaveRouteRequest } from '@/api';
import { savedRoutesKey } from '@/lib/query-keys';
import { markLastRouteSaved } from '@/lib/route-result-store';

import { useSession } from './use-session';

/**
 * Mutation that saves a ride to the signed-in rider's account. On success it marks the ride saved
 * *by that rider* in the result store, which is what the Save action renders "Saved ✓" from, and
 * invalidates that rider's saved-routes list: a save is the one event that can make the list wrong,
 * so it is the event that refreshes it.
 *
 * The rider is captured when the save starts (`onMutate`), not when it lands: if the session
 * changed in between, the row still belongs to whoever's token went out with the request, and the
 * next rider must not inherit their "Saved ✓" — nor have their list invalidated in place of the
 * rider whose route was actually saved. The callbacks live on the mutation options rather than on
 * `mutate()` so they still run if the Save action unmounts mid-save.
 */
export function useSaveRouteMutation() {
  const { user } = useSession();
  const queryClient = useQueryClient();

  return useMutation<SavedRoute, ApiError, SaveRouteRequest, { userId: string | null }>({
    mutationFn: saveRoute,
    onMutate: () => ({ userId: user?.id ?? null }),
    onSuccess: (_saved, { clientRouteId }, { userId }) => {
      if (userId === null) return;
      markLastRouteSaved(clientRouteId, userId);
      queryClient.invalidateQueries({ queryKey: savedRoutesKey(userId) });
    },
  });
}
