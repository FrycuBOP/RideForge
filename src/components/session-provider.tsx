import type { Session } from '@supabase/supabase-js';
import { useQueryClient } from '@tanstack/react-query';
import { createContext, useEffect, useState, type PropsWithChildren } from 'react';

import { supabase } from '@/lib/supabase';

export type SessionState = {
  /** The live Supabase session, or `null` when signed out. */
  session: Session | null;
  /** Convenience accessor for `session.user`. */
  user: Session['user'] | null;
  /**
   * `true` until the persisted session has been read back from storage. Screens must render
   * neither the signed-in nor the signed-out view while this is true — the restore is async, so a
   * signed-in rider would otherwise see a sign-in form flash on every cold start.
   */
  isRestoring: boolean;
};

export const SessionContext = createContext<SessionState | null>(null);

/**
 * Owns auth state for the whole app so no screen has to subscribe to Supabase itself. Seeds from
 * the persisted session, then follows `onAuthStateChange` (sign-in, sign-out, token refresh).
 */
export function SessionProvider({ children }: PropsWithChildren) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState<Session | null>(null);
  const [isRestoring, setIsRestoring] = useState(true);

  useEffect(() => {
    let cancelled = false;

    supabase.auth.getSession().then(({ data }) => {
      // Guards unmount, not ordering: this only skips setting state on a provider that is already
      // gone. It does NOT prevent a slow storage read from overwriting a fresher session that
      // `onAuthStateChange` delivered first — that race is left open deliberately, because both
      // paths await the same client initialisation and the Account screen renders only a spinner
      // while `isRestoring`. Making the ordering claim true would need a flag set by the
      // subscription below.
      if (cancelled) return;
      setSession(data.session);
      setIsRestoring(false);
    });

    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((event, nextSession) => {
      setSession(nextSession);
      setIsRestoring(false);

      // Authenticated query results are keyed by endpoint, not by rider, so they survive a sign-out
      // on their own. Drop them here or the next rider to sign in on this device sees the previous
      // rider's identity until the query happens to refetch.
      if (event === 'SIGNED_OUT') {
        queryClient.removeQueries({ queryKey: ['me'] });
      }
    });

    return () => {
      cancelled = true;
      subscription.unsubscribe();
    };
  }, [queryClient]);

  return (
    <SessionContext.Provider value={{ session, user: session?.user ?? null, isRestoring }}>
      {children}
    </SessionContext.Provider>
  );
}
