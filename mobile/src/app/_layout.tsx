import { ActivityIndicator, Text, useColorScheme } from 'react-native';
import { Button, Host } from '@expo/ui';
import { Stack } from 'expo-router/stack';
import { DarkTheme, DefaultTheme, ThemeProvider } from 'expo-router/react-navigation';
import { StatusBar } from 'expo-status-bar';
import { normalizeApiUrl } from '@/api/client';
import { SessionProvider, useSession } from '@/auth/session-provider';
import { ErrorNotice, Message, Screen } from '@/components/content';
import { type } from '@/theme';

export { ErrorBoundary } from 'expo-router';

function Routes() {
  const session = useSession();
  if (session.status === 'restoring')
    return (
      <Screen>
        <ActivityIndicator accessibilityLabel="Restoring session" />
        <Text style={type.body}>Checking your secure session...</Text>
      </Screen>
    );
  if (session.status === 'restoreError')
    return (
      <Screen>
        <Message title="Session unavailable">
          <ErrorNotice
            error={session.error}
            onRetry={() => {
              if (session.retry === 'clear') void session.controller.signOut();
              else void session.controller.restore();
            }}
          />
        </Message>
        <Host matchContents>
          <Button
            label="Clear saved session"
            variant="outlined"
            onPress={() => {
              void session.controller.signOut();
            }}
          />
        </Host>
      </Screen>
    );
  return (
    <Stack>
      <Stack.Protected guard={session.status === 'signedIn'}>
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
        <Stack.Screen name="agent/[id]" options={{ title: 'Agent' }} />
        <Stack.Screen name="session/[id]" options={{ title: 'Cash session' }} />
        <Stack.Screen name="cash-count" options={{ title: 'Cash count' }} />
        <Stack.Protected
          guard={session.user?.role === 'Admin' || session.user?.role === 'Supervisor'}
        >
          <Stack.Screen name="vault" options={{ title: 'Branch vault' }} />
        </Stack.Protected>
      </Stack.Protected>
      <Stack.Protected guard={session.status === 'signedOut'}>
        <Stack.Screen name="sign-in" options={{ title: 'Sign in to Agenti' }} />
      </Stack.Protected>
    </Stack>
  );
}

export default function RootLayout() {
  const scheme = useColorScheme();
  let apiUrl: string;
  try {
    apiUrl = normalizeApiUrl(process.env.EXPO_PUBLIC_API_URL, __DEV__);
  } catch (error) {
    return (
      <Screen>
        <Message title="Configure your Agenti API">
          <Text selectable style={type.body}>
            {error instanceof Error ? error.message : 'Invalid API configuration.'}
          </Text>
          <Text style={type.body}>
            Copy .env.example to .env, set your backend URL, then restart Expo.
          </Text>
        </Message>
      </Screen>
    );
  }
  return (
    <ThemeProvider value={scheme === 'dark' ? DarkTheme : DefaultTheme}>
      <StatusBar style="auto" />
      <SessionProvider apiUrl={apiUrl}>
        <Routes />
      </SessionProvider>
    </ThemeProvider>
  );
}
