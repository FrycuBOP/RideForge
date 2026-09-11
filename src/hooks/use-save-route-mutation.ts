import { useMutation } from '@tanstack/react-query';

import { ApiError, saveRoute, type SavedRoute, type SaveRouteRequest } from '@/api';
import { markLastRouteSaved } from '@/lib/route-result-store';

import { useSession } from './use-session';

/**
 * Mutation that saves a ride to the signed-in rider's account. On success it marks the ride saved
 * *by that rider* in the result store, which is what the Save action renders "Saved ✓" from.
 *
 * The rider is captured when the save starts (`onMutate`), not when it lands: if the session
 * changed in between, the row still belongs to whoever's token went out with the request, and the
 * next rider must not inherit their "Saved ✓". The callbacks live on the mutation options rather
 * than on `mutate()` so they still run if the Save action unmounts mid-save.
 */
export function useSaveRouteMutation() {
  const { user } = useSession();

  return useMutation<SavedRoute, ApiError, SaveRouteRequest, { userId: string | null }>({
    mutationFn: saveRoute,
    onMutate: () => ({ userId: user?.id ?? null }),
    onSuccess: (_saved, { clientRouteId }, { userId }) => {
      if (userId !== null) markLastRouteSaved(clientRouteId, userId);
    },
  });
}
