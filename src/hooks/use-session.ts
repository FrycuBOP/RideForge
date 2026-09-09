import { useContext } from 'react';

import { SessionContext, type SessionState } from '@/components/session-provider';

/**
 * Read the current auth state. Throws when used outside `<SessionProvider>` (mounted in
 * src/app/_layout.tsx) rather than returning a plausible signed-out state, which would look like
 * a logged-out rider instead of a wiring bug.
 */
export function useSession(): SessionState {
  const state = useContext(SessionContext);

  if (state === null) {
    throw new Error('useSession must be used inside <SessionProvider>.');
  }

  return state;
}
