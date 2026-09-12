import { NativeTabs } from 'expo-router/unstable-native-tabs';
import { useColorScheme } from 'react-native';

import { Colors } from '@/constants/theme';
import { useSession } from '@/hooks/use-session';

export default function AppTabs() {
  const scheme = useColorScheme();
  const colors = Colors[scheme === 'unspecified' ? 'light' : scheme];
  // Saved routes are an account feature, so the tab only exists for a rider holding a session. This
  // is read once per navigator instance, never flipped underneath one: the layout holds this
  // component back until the session is known and remounts it when the session changes. See
  // `(tabs)/_layout.tsx` for why Android insists on that.
  const { session } = useSession();

  return (
    <NativeTabs
      backgroundColor={colors.background}
      indicatorColor={colors.backgroundElement}
      labelStyle={{ selected: { color: colors.text } }}>
      <NativeTabs.Trigger name="index">
        <NativeTabs.Trigger.Label>Plan</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon
          src={require('@/assets/images/tabIcons/home.png')}
          renderingMode="template"
        />
      </NativeTabs.Trigger>

      {/*
        Replaces the scaffold's Explore tab (FR-010). `hidden` rather than a conditional render so
        the route stays registered either way — a NativeTabs child route with no trigger is not a
        supported shape.

        This value is constant for the life of a navigator instance; the layout guarantees that.
        A hidden tab also cannot be navigated to at all, so on native a signed-out rider cannot
        reach `/saved-routes` even by deep link. The screen keeps its signed-out branch regardless —
        on web the URL is still reachable directly.

        Both awkward states were exercised on Android and neither misbehaves: signing out
        while this tab is the focused one, and a cold-start deep link to a saved ride while
        signed out. The guards cope; no redirect was needed.
      */}
      <NativeTabs.Trigger name="saved-routes" hidden={session === null}>
        <NativeTabs.Trigger.Label>Saved</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon
          sf={{ default: 'bookmark', selected: 'bookmark.fill' }}
          md={{ default: 'bookmark_border', selected: 'bookmark' }}
        />
      </NativeTabs.Trigger>

      <NativeTabs.Trigger name="account">
        <NativeTabs.Trigger.Label>Account</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon
          src={require('@/assets/images/tabIcons/account.png')}
          renderingMode="template"
        />
      </NativeTabs.Trigger>
    </NativeTabs>
  );
}
