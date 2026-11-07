import { Alert } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ApiError, createApiClient } from '@/api/client';
import { createEndpoints } from '@/api/endpoints';
import { user } from '@/api/test-helpers';
import { SessionController, SessionState } from '@/auth/session';
import { useApi, useSession } from '@/auth/session-provider';
import { VaultScreen } from './vault';

jest.mock('@/auth/session-provider', () => ({ useApi: jest.fn(), useSession: jest.fn() }));

test('an ambiguous vault request blocks a later visit until fresh activity is checked', async () => {
  const entries = new Map<string, string>();
  const read = jest
    .spyOn(SecureStore, 'getItemAsync')
    .mockImplementation(
      async (_key, options) => entries.get(options?.keychainService ?? '') ?? null,
    );
  const write = jest
    .spyOn(SecureStore, 'setItemAsync')
    .mockImplementation(async (_key, value, options) => {
      entries.set(options?.keychainService ?? '', value);
    });
  const remove = jest
    .spyOn(SecureStore, 'deleteItemAsync')
    .mockImplementation(async (_key, options) => {
      entries.delete(options?.keychainService ?? '');
    });
  const controller = new SessionController(
    'https://agenti.example/api',
    {
      read: async () => null,
      write: async () => undefined,
      remove: async () => undefined,
    },
    () => undefined,
  );
  const state: SessionState = {
    status: 'signedIn',
    user: { ...user, role: 'Supervisor' },
    expiresAt: Date.now() + 60_000,
    error: null,
  };
  jest.spyOn(controller, 'getSnapshot').mockReturnValue(state);
  jest.mocked(useSession).mockReturnValue({ ...state, controller });
  const api = createEndpoints(
    createApiClient({
      baseUrl: controller.apiUrl,
      getToken: () => 'synthetic-token',
      onUnauthorized: async () => undefined,
    }),
  );
  jest.spyOn(api, 'vault').mockResolvedValue({
    id: 1,
    branchId: user.branchId,
    branchName: 'Test branch',
    currentBalance: 1000,
  });
  const history = jest.spyOn(api, 'vaultTransactions').mockResolvedValue([]);
  const send = jest
    .spyOn(api, 'requestAdjustment')
    .mockRejectedValue(new ApiError('The request timed out.'));
  jest.mocked(useApi).mockReturnValue(api);
  const confirm = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
    buttons?.find((button) => button.text === 'Request')?.onPress?.();
  });
  const firstQueries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const secondQueries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let firstVisit: Awaited<ReturnType<typeof render>> | undefined;
  let secondVisit: Awaited<ReturnType<typeof render>> | undefined;
  try {
    firstVisit = await render(
      <QueryClientProvider client={firstQueries}>
        <VaultScreen />
      </QueryClientProvider>,
    );
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Request deposit' })).toBeEnabled(),
    );
    await fireEvent.changeText(screen.getByLabelText('Adjustment amount (UGX)'), '100');
    await fireEvent.changeText(screen.getByLabelText('Audit notes'), 'Verified synthetic deposit');
    await fireEvent.press(screen.getByRole('button', { name: 'Request deposit' }));
    await screen.findByText('Check activity before retrying');
    expect(send).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Request deposit' })).toBeDisabled();
    await firstVisit.unmount();
    firstVisit = undefined;
    firstQueries.clear();

    secondVisit = await render(
      <QueryClientProvider client={secondQueries}>
        <VaultScreen />
      </QueryClientProvider>,
    );
    await screen.findByText('Check activity before retrying');
    expect(screen.getByRole('button', { name: 'Request deposit' })).toBeDisabled();
    expect(
      screen.getByRole('button', { name: 'I checked activity; allow another request' }),
    ).toBeDisabled();
    history.mockRejectedValueOnce(new ApiError('Activity is unavailable.'));
    await fireEvent.press(screen.getByRole('button', { name: 'Refresh activity' }));
    await screen.findByText(/Activity is unavailable/);
    expect(
      screen.getByRole('button', { name: 'I checked activity; allow another request' }),
    ).toBeDisabled();
    await fireEvent.press(screen.getByRole('button', { name: 'Refresh activity' }));
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'I checked activity; allow another request' }),
      ).toBeEnabled(),
    );
    await fireEvent.press(
      screen.getByRole('button', { name: 'I checked activity; allow another request' }),
    );
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Request deposit' })).toBeEnabled(),
    );
    expect(send).toHaveBeenCalledTimes(1);
    expect(entries.size).toBe(0);
  } finally {
    await firstVisit?.unmount();
    await secondVisit?.unmount();
    firstQueries.clear();
    secondQueries.clear();
    confirm.mockRestore();
    read.mockRestore();
    write.mockRestore();
    remove.mockRestore();
  }
});
