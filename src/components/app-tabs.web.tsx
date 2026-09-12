import type { PropsWithChildren } from 'react';
import { Tabs, TabList, TabTrigger, TabSlot, TabTriggerSlotProps } from 'expo-router/ui';
import { SymbolView } from 'expo-symbols';
import { Pressable, useColorScheme, View, StyleSheet } from 'react-native';

import { ExternalLink } from './external-link';
import { ThemedText } from './themed-text';
import { ThemedView } from './themed-view';

import { Colors, MaxContentWidth, Spacing } from '@/constants/theme';
import { useSession } from '@/hooks/use-session';

/**
 * Web tab bar. Split in two on purpose, following the custom-tabs pattern in the SDK 56 docs:
 * `TabList` is what registers the routes and is rendered but never displayed, while the bar the
 * rider actually sees is built from `TabTrigger`s outside it (which need no `href`).
 *
 * That split is what lets the Saved button come and go with the session without taking the route
 * with it. Declaring the trigger only when signed in — the obvious version — deregisters
 * `/saved-routes` entirely, and a signed-out rider opening the URL lands silently on Plan instead of
 * the screen's own "sign in first" state.
 */
export default function AppTabs() {
  // Saved routes are an account feature, so only a rider holding a session is offered the button.
  const { session } = useSession();

  return (
    <Tabs>
      <TabSlot style={{ height: '100%' }} />

      <VisibleTabBar>
        <TabTrigger name="home" asChild>
          <TabButton>Home</TabButton>
        </TabTrigger>
        {session !== null && (
          <TabTrigger name="saved-routes" asChild>
            <TabButton>Saved</TabButton>
          </TabTrigger>
        )}
        <TabTrigger name="account" asChild>
          <TabButton>Account</TabButton>
        </TabTrigger>
      </VisibleTabBar>

      {/* Route registration only — hidden, and always complete. */}
      <TabList style={styles.routeRegistry}>
        <TabTrigger name="home" href="/" />
        <TabTrigger name="saved-routes" href="/saved-routes" />
        <TabTrigger name="account" href="/account" />
      </TabList>
    </Tabs>
  );
}

export function TabButton({ children, isFocused, ...props }: TabTriggerSlotProps) {
  return (
    <Pressable {...props} style={({ pressed }) => pressed && styles.pressed}>
      <ThemedView
        type={isFocused ? 'backgroundSelected' : 'backgroundElement'}
        style={styles.tabButtonView}>
        <ThemedText type="small" themeColor={isFocused ? 'text' : 'textSecondary'}>
          {children}
        </ThemedText>
      </ThemedView>
    </Pressable>
  );
}

export function VisibleTabBar({ children }: PropsWithChildren) {
  const scheme = useColorScheme();
  const colors = Colors[scheme === 'unspecified' ? 'light' : scheme];

  return (
    <View style={styles.tabListContainer}>
      <ThemedView type="backgroundElement" style={styles.innerContainer}>
        <ThemedText type="smallBold" style={styles.brandText}>
          Expo Starter
        </ThemedText>

        {children}

        <ExternalLink href="https://docs.expo.dev" asChild>
          <Pressable style={styles.externalPressable}>
            <ThemedText type="link">Docs</ThemedText>
            <SymbolView
              tintColor={colors.text}
              name={{ ios: 'arrow.up.right.square', web: 'link' }}
              size={12}
            />
          </Pressable>
        </ExternalLink>
      </ThemedView>
    </View>
  );
}

const styles = StyleSheet.create({
  tabListContainer: {
    position: 'absolute',
    width: '100%',
    padding: Spacing.three,
    justifyContent: 'center',
    alignItems: 'center',
    flexDirection: 'row',
  },
  // Rendered so the routes exist; never seen.
  routeRegistry: {
    display: 'none',
  },
  innerContainer: {
    paddingVertical: Spacing.two,
    paddingHorizontal: Spacing.five,
    borderRadius: Spacing.five,
    flexDirection: 'row',
    alignItems: 'center',
    flexGrow: 1,
    gap: Spacing.two,
    maxWidth: MaxContentWidth,
  },
  brandText: {
    marginRight: 'auto',
  },
  pressed: {
    opacity: 0.7,
  },
  tabButtonView: {
    paddingVertical: Spacing.one,
    paddingHorizontal: Spacing.three,
    borderRadius: Spacing.three,
  },
  externalPressable: {
    flexDirection: 'row',
    justifyContent: 'center',
    alignItems: 'center',
    gap: Spacing.one,
    marginLeft: Spacing.three,
  },
});
