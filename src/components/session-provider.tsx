import type { Session } from '@supabase/supabase-js';
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
  const [session, setSession] = useState<Session | null>(null);
  const [isRestoring, setIsRestoring] = useState(true);

  useEffect(() => {
    let cancelled = false;

    supabase.auth.getSession().then(({ data }) => {
      // A later onAuthStateChange may already have delivered a fresher session; don't clobber it.
      if (cancelled) return;
      setSession(data.session);
      setIsRestoring(false);
    });

    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((_event, nextSession) => {
      setSession(nextSession);
      setIsRestoring(false);
    });

    return () => {
      cancelled = true;
      subscription.unsubscribe();
    };
  }, []);

  return (
    <SessionContext.Provider value={{ session, user: session?.user ?? null, isRestoring }}>
      {children}
    </SessionContext.Provider>
  );
}
