import AsyncStorage from '@react-native-async-storage/async-storage';
import { createClient } from '@supabase/supabase-js';
import { AppState, Platform } from 'react-native';
import 'react-native-url-polyfill/auto';

/**
 * Supabase project config. `EXPO_PUBLIC_*` values are inlined at build time, so changing them
 * requires restarting the dev server with `--clear` (same rule as `EXPO_PUBLIC_API_URL`, see
 * src/api/config.ts).
 *
 * Unlike the API URL these get no fallback: a client pointed at a wrong or empty project fails
 * at request time with an opaque error, which is much harder to spot than a boot failure — the
 * same reasoning the backend applies to its stitching-provider config.
 */
const supabaseUrl = process.env.EXPO_PUBLIC_SUPABASE_URL;
const supabaseAnonKey = process.env.EXPO_PUBLIC_SUPABASE_ANON_KEY;

if (!supabaseUrl || !supabaseAnonKey) {
  throw new Error(
    'Missing Supabase configuration. Set EXPO_PUBLIC_SUPABASE_URL and ' +
      'EXPO_PUBLIC_SUPABASE_ANON_KEY (see .env.example), then restart with: npx expo start --clear'
  );
}

/**
 * The single shared Supabase client.
 *
 * `storage` is native-only — AsyncStorage has no place in a static web render, where supabase-js
 * falls back to its own browser storage. `detectSessionInUrl` stays off because this app has no
 * OAuth/magic-link redirect to parse.
 */
export const supabase = createClient(supabaseUrl, supabaseAnonKey, {
  auth: {
    ...(Platform.OS !== 'web' ? { storage: AsyncStorage } : {}),
    autoRefreshToken: true,
    persistSession: true,
    detectSessionInUrl: false,
  },
});

// React Native does not refresh tokens on its own: the timer has to be driven from AppState so it
// runs only in the foreground. Registered once, at module scope, and skipped on web where the
// browser tab already handles it.
if (Platform.OS !== 'web') {
  AppState.addEventListener('change', (state) => {
    if (state === 'active') {
      supabase.auth.startAutoRefresh();
    } else {
      supabase.auth.stopAutoRefresh();
    }
  });
}
