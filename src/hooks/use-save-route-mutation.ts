import { useMutation } from '@tanstack/react-query';

import {
  ApiError,
  MAX_START_LABEL_LENGTH,
  saveRoute,
  type SavedRoute,
  type SaveRouteRequest,
} from '@/api';
import { markLastRouteSaved, type Ride } from '@/lib/route-result-store';

import { useSession } from './use-session';

/**
 * Cut a start label to the server's column limit without splitting a surrogate pair — a lone
 * surrogate is not valid UTF-16 and would turn a long-but-fine label into a failed save.
 */
function fitStartLabel(label: string | null): string | null {
  if (label === null || label.length <= MAX_START_LABEL_LENGTH) return label;
  const cut = label.slice(0, MAX_START_LABEL_LENGTH);
  const last = cut.charCodeAt(cut.length - 1);
  return last >= 0xd800 && last <= 0xdbff ? cut.slice(0, -1) : cut;
}

/** Build the `POST /saved-routes` body for a ride held in the result store. */
export function toSaveRouteRequest(ride: Ride): SaveRouteRequest {
  return {
    clientRouteId: ride.clientRouteId,
    start: ride.request.start,
    startLabel: fitStartLabel(ride.startLabel),
    requestedDistanceKm: ride.request.distanceKm,
    geometry: ride.route.geometry,
    distanceMeters: ride.route.distanceMeters,
    durationSeconds: ride.route.durationSeconds,
  };
}

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
