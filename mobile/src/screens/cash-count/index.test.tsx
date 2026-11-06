import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useApi } from '@/auth/session-provider';
import { ApiError, createApiClient } from '@/api/client';
import { createEndpoints } from '@/api/endpoints';
import { CashCountEditor, CashCountScreen } from './index';
import { CashCountForm } from '@/api/contracts';
import { Alert } from 'react-native';
import { router } from 'expo-router';

jest.mock('@/auth/session-provider', () => ({ useApi: jest.fn() }));
jest.mock('expo-router/stack', () => ({ Stack: { Screen: () => null } }));

function createApi() {
  return createEndpoints(
    createApiClient({
      baseUrl: 'https://agenti.example/api',
      getToken: () => null,
      onUnauthorized: async () => undefined,
    }),
  );
}

const initial: CashCountForm = {
  cashCountId: null,
  cashSessionId: null,
  isOpening: true,
  isReadOnly: false,
  countDate: '2026-11-06',
  explanation: null,
  previousClosingDate: null,
  previousClosingTotal: null,
  walletEntries: [
    {
      walletId: 1,
      walletName: 'Bank',
      walletTypeName: 'Bank',
      supportsDenominations: false,
      countedAmount: 0,
      expectedBalance: 0,
      denominations: null,
      observedAt: null,
    },
  ],
};

test('failed cash count save preserves the draft and a deliberate retry persists it', async () => {
  const api = createApi();
  const saveCount = jest
    .spyOn(api, 'saveCount')
    .mockRejectedValueOnce(new ApiError('Server is offline.'))
    .mockResolvedValueOnce({ success: true, cashCountId: 5, cashSessionId: 6 });
  jest.mocked(useApi).mockReturnValue(api);
  await render(
    <QueryClientProvider client={new QueryClient()}>
      <CashCountEditor initial={initial} />
    </QueryClientProvider>,
  );
  await fireEvent.changeText(screen.getByLabelText('Bank amount'), '250.50');
  await fireEvent.changeText(
    screen.getByLabelText('Explanation / discrepancy notes'),
    'Verified the bank balance',
  );
  await fireEvent.press(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByText('Server is offline.')).toBeTruthy();
  expect(screen.getByDisplayValue('250.50')).toBeTruthy();
  expect(screen.getByDisplayValue('Verified the bank balance')).toBeTruthy();
  await fireEvent.press(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByText('Draft saved')).toBeTruthy();
  expect(saveCount).toHaveBeenLastCalledWith(
    expect.objectContaining({
      explanation: 'Verified the bank balance',
      walletEntries: [expect.objectContaining({ walletId: 1, countedAmount: 250.5 })],
    }),
  );
  await fireEvent.changeText(
    screen.getByLabelText('Explanation / discrepancy notes'),
    'Revised explanation, not saved yet',
  );
  expect(screen.queryByText('Draft saved')).toBeNull();
});

test('failed submission keeps the saved count ID for retry and navigates only after success', async () => {
  const api = createApi();
  const save = jest
    .spyOn(api, 'saveCount')
    .mockResolvedValue({ success: true, cashCountId: 5, cashSessionId: 6 });
  const submit = jest
    .spyOn(api, 'submitCount')
    .mockRejectedValueOnce(new ApiError('Submission unavailable.'))
    .mockResolvedValueOnce({ success: true, cashCountId: 5, cashSessionId: 6 });
  jest.mocked(useApi).mockReturnValue(api);
  const navigate = jest.spyOn(router, 'replace').mockImplementation(() => undefined);
  const confirm = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
    buttons?.find((button) => button.text === 'Submit' || button.text === 'Retry')?.onPress?.();
  });
  try {
    await render(
      <QueryClientProvider client={new QueryClient()}>
        <CashCountEditor initial={initial} />
      </QueryClientProvider>,
    );
    await fireEvent.press(screen.getByRole('button', { name: 'Save and submit' }));
    expect(await screen.findByText('Submission unavailable.')).toBeTruthy();
    expect(navigate).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByRole('button', { name: 'Retry submission' }));
    expect(save).toHaveBeenCalledTimes(1);
    expect(save).toHaveBeenCalledWith(
      expect.objectContaining({ cashCountId: 5, cashSessionId: 6 }),
    );
    expect(submit).toHaveBeenCalledTimes(2);
    expect(submit).toHaveBeenLastCalledWith(5);
    expect(navigate).toHaveBeenCalledWith('/counts');
  } finally {
    navigate.mockRestore();
    confirm.mockRestore();
  }
});

test('reopening a cached count fetches the latest server form', async () => {
  const api = createApi();
  const count = jest.spyOn(api, 'count').mockResolvedValue(initial);
  jest.mocked(useApi).mockReturnValue(api);
  const queries = new QueryClient();
  const renderScreen = () =>
    render(
      <QueryClientProvider client={queries}>
        <CashCountScreen id={5} isOpening />
      </QueryClientProvider>,
    );

  const firstVisit = renderScreen();
  await waitFor(() => expect(count).toHaveBeenCalledTimes(1));
  firstVisit.unmount();
  renderScreen();
  await waitFor(() => expect(count).toHaveBeenCalledTimes(2));
});
