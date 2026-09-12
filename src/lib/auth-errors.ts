import {
  isAuthRetryableFetchError,
  isAuthWeakPasswordError,
  type AuthError,
} from '@supabase/supabase-js';

/**
 * Turn a Supabase `AuthError` into rider-facing copy (FR-005), mirroring `planErrorMessage` in
 * src/app/(tabs)/index.tsx.
 *
 * The branches key on `error.code` — Supabase's stable, documented identifier — never on
 * `error.message`, which is prose the provider is free to reword. Anything unmatched falls back to
 * a generic line rather than surfacing the raw `AuthApiError` string, which names internals the
 * rider cannot act on.
 *
 * @see https://supabase.com/docs/guides/auth/debugging/error-codes
 */
export function authErrorMessage(error: AuthError): string {
  // A dead connection has no error code — it never reached the auth server.
  if (isAuthRetryableFetchError(error)) {
    return 'Can’t reach the account server. Check your connection and try again.';
  }

  // `reasons` is one of 'length' | 'characters' | 'pwned' — each needs different advice, so a
  // single "weak password" line would leave the rider guessing what to change.
  if (isAuthWeakPasswordError(error)) {
    if (error.reasons.includes('length')) {
      return 'That password is too short. Use at least 6 characters.';
    }
    if (error.reasons.includes('pwned')) {
      return 'That password has appeared in a data breach. Choose a different one.';
    }
    return 'That password is too easy to guess. Mix in letters, numbers, and symbols.';
  }

  switch (error.code) {
    case 'invalid_credentials':
      return 'That email and password don’t match an account. Check them and try again.';
    case 'email_exists':
    case 'user_already_exists':
      return 'An account already uses that email. Sign in instead.';
    case 'weak_password':
      // Reachable when the server reports weak_password without the structured reasons payload.
      return 'That password is too weak. Use at least 6 characters.';
    case 'email_address_invalid':
    case 'validation_failed':
      return 'That doesn’t look like a valid email address.';
    case 'over_request_rate_limit':
      return 'Too many attempts. Wait a few minutes and try again.';
    case 'email_not_confirmed':
      return 'This account still needs its email confirmed before signing in.';
    case 'signup_disabled':
    case 'email_provider_disabled':
      return 'New accounts aren’t being accepted right now. Try again later.';
    case 'user_banned':
      return 'This account is suspended.';
    default:
      return 'Something went wrong with your account. Please try again.';
  }
}
