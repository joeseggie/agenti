import {
  createContext,
  PropsWithChildren,
  use,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
} from 'react';
import { AppState } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import { focusManager, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createEndpoints } from '@/api/endpoints';
import { SessionController } from './session';

const key = 'agenti.session.v1';
const storageOptions = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };
const SessionContext = createContext<SessionController | null>(null);

export function SessionProvider({ apiUrl, children }: PropsWithChildren<{ apiUrl: string }>) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { retry: false, staleTime: 30_000 },
          mutations: { retry: false },
        },
      }),
  );
  const [session] = useState(
    () =>
      new SessionController(
        apiUrl,
        {
          read: () => SecureStore.getItemAsync(key, storageOptions),
          write: (value) => SecureStore.setItemAsync(key, value, storageOptions),
          remove: () => SecureStore.deleteItemAsync(key, storageOptions),
        },
        () => queryClient.clear(),
      ),
  );
  const state = useSyncExternalStore(session.subscribe, session.getSnapshot, session.getSnapshot);

  useEffect(() => {
    void session.restore();
  }, [session]);
  useEffect(() => {
    const listener = AppState.addEventListener('change', (next) => {
      if (next === 'active') void session.checkExpiry();
      focusManager.setFocused(next === 'active');
    });
    if (state.status !== 'signedIn') return () => listener.remove();
    const timeout = setTimeout(
      () => {
        void session.checkExpiry();
      },
      Math.min(Math.max(0, state.expiresAt - Date.now()), 2_147_483_647),
    );
    return () => {
      listener.remove();
      clearTimeout(timeout);
    };
  }, [session, state]);

  return (
    <SessionContext value={session}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </SessionContext>
  );
}

export function useSession() {
  const controller = use(SessionContext);
  if (!controller) throw new Error('SessionProvider is required.');
  const state = useSyncExternalStore(
    controller.subscribe,
    controller.getSnapshot,
    controller.getSnapshot,
  );
  return { controller, ...state };
}

export function useApi() {
  const { controller } = useSession();
  return useMemo(() => createEndpoints(controller.client), [controller]);
}
