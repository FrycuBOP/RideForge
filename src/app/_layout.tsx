import { QueryClientProvider } from '@tanstack/react-query';
import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from 'expo-router';

import { queryClient } from '@/api/query-client';
import { AnimatedSplashOverlay } from '@/components/animated-icon';
import { SessionProvider } from '@/components/session-provider';
// Not react-native's hook: the .web sibling defers to the client snapshot so a static web render
// doesn't hydrate with the wrong theme (app.json sets output: "static").
import { useColorScheme } from '@/hooks/use-color-scheme';

/**
 * Root layout. Providers (react-query, session, theme, splash) wrap a root `<Stack>` so non-tab
 * routes (e.g. `/result`) can be pushed over the tab navigator. The `(tabs)` group owns the tabs
 * and hides the stack header to avoid a double header; `result` is a pushable sibling route.
 *
 * `SessionProvider` sits inside `QueryClientProvider` because authenticated queries read the
 * session from inside a query, and outside `ThemeProvider` because auth state has nothing to do
 * with theming.
 */
export default function RootLayout() {
  const colorScheme = useColorScheme();
  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <ThemeProvider value={colorScheme === 'dark' ? DarkTheme : DefaultTheme}>
          <AnimatedSplashOverlay />
          <Stack>
            <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
            <Stack.Screen name="result" options={{ title: 'Your ride' }} />
            {/* Pushed over the tabs like `result`, which is why `saved-routes/` has no _layout. */}
            <Stack.Screen name="saved-routes/index" options={{ title: 'Saved routes' }} />
            <Stack.Screen name="saved-routes/[id]" options={{ title: 'Saved ride' }} />
          </Stack>
        </ThemeProvider>
      </SessionProvider>
    </QueryClientProvider>
  );
}
