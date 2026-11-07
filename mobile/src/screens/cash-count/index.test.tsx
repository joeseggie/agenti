import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
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
    expect(screen.getByLabelText('Bank amount')).toBeDisabled();
    expect(screen.getByLabelText('Explanation / discrepancy notes')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Observed Bank now' })).toBeDisabled();
    await fireEvent.press(screen.getByRole('button', { name: 'Retry submission' }));
    expect(save).toHaveBeenCalledTimes(1);
    expect(save).toHaveBeenCalledWith(
      expect.objectContaining({ cashCountId: null, cashSessionId: null }),
    );
    expect(submit).toHaveBeenCalledTimes(2);
    expect(submit).toHaveBeenLastCalledWith(5);
    expect(navigate).toHaveBeenCalledWith('/counts');
  } finally {
    navigate.mockRestore();
    confirm.mockRestore();
  }
});

test('submission validation errors allow correcting and saving the retained draft', async () => {
  const api = createApi();
  const save = jest
    .spyOn(api, 'saveCount')
    .mockResolvedValue({ success: true, cashCountId: 5, cashSessionId: 6 });
  const submit = jest
    .spyOn(api, 'submitCount')
    .mockRejectedValueOnce(new ApiError('Please provide an explanation for the discrepancy.', 400))
    .mockResolvedValueOnce({ success: true, cashCountId: 5, cashSessionId: 6 });
  jest.mocked(useApi).mockReturnValue(api);
  const navigate = jest.spyOn(router, 'replace').mockImplementation(() => undefined);
  const confirm = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
    buttons?.find((button) => button.text === 'Submit')?.onPress?.();
  });
  try {
    await render(
      <QueryClientProvider client={new QueryClient()}>
        <CashCountEditor initial={initial} />
      </QueryClientProvider>,
    );
    await fireEvent.changeText(screen.getByLabelText('Bank amount'), '250.50');
    await fireEvent.press(screen.getByRole('button', { name: 'Save and submit' }));
    expect(
      await screen.findByText('Please provide an explanation for the discrepancy.'),
    ).toBeTruthy();
    expect(screen.getByLabelText('Explanation / discrepancy notes')).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Save draft' })).toBeEnabled();
    expect(screen.getByDisplayValue('250.50')).toBeTruthy();
    await fireEvent.changeText(
      screen.getByLabelText('Explanation / discrepancy notes'),
      'Difference verified against the bank statement',
    );
    await fireEvent.press(screen.getByRole('button', { name: 'Save and submit' }));
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/counts'));
    expect(save).toHaveBeenCalledTimes(2);
    expect(save).toHaveBeenLastCalledWith(
      expect.objectContaining({
        cashCountId: 5,
        cashSessionId: 6,
        explanation: 'Difference verified against the bank statement',
        walletEntries: [expect.objectContaining({ countedAmount: 250.5 })],
      }),
    );
    expect(submit).toHaveBeenLastCalledWith(5);
  } finally {
    navigate.mockRestore();
    confirm.mockRestore();
  }
});

test.each([false, true])(
  'a cached count waits for fresh values and read-only state (%s)',
  async (isReadOnly) => {
    const api = createApi();
    const request = Promise.withResolvers<CashCountForm>();
    jest.spyOn(api, 'count').mockReturnValue(request.promise);
    jest.mocked(useApi).mockReturnValue(api);
    const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queries.setQueryData(['cash-count', 5, true], {
      ...initial,
      cashCountId: 5,
      explanation: 'Old cached explanation',
    });
    const visit = await render(
      <QueryClientProvider client={queries}>
        <CashCountScreen id={5} isOpening />
      </QueryClientProvider>,
    );
    try {
      expect(screen.queryByLabelText('Bank amount')).toBeNull();
      expect(screen.queryByRole('button', { name: 'Save draft' })).toBeNull();
      await act(async () =>
        request.resolve({
          ...initial,
          cashCountId: 5,
          isReadOnly,
          explanation: 'Latest server explanation',
          walletEntries: initial.walletEntries.map((wallet) => ({ ...wallet, countedAmount: 250 })),
        }),
      );
      expect(await screen.findByDisplayValue('250')).toBeTruthy();
      expect(screen.getByDisplayValue('Latest server explanation')).toBeTruthy();
      if (isReadOnly) {
        expect(screen.getByText('Read-only count')).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Save draft' })).toBeNull();
      } else {
        expect(screen.getByRole('button', { name: 'Save draft' })).toBeEnabled();
      }
    } finally {
      await visit.unmount();
      queries.clear();
    }
  },
);

test('a failed opening refresh cannot expose the cached editor and can be retried', async () => {
  const api = createApi();
  jest
    .spyOn(api, 'count')
    .mockRejectedValueOnce(new ApiError('Cannot refresh this count.'))
    .mockResolvedValueOnce({
      ...initial,
      explanation: 'Fresh after retry',
    });
  jest.mocked(useApi).mockReturnValue(api);
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queries.setQueryData(['cash-count', 5, true], initial);
  const visit = await render(
    <QueryClientProvider client={queries}>
      <CashCountScreen id={5} isOpening />
    </QueryClientProvider>,
  );
  try {
    expect(await screen.findByText('Cannot refresh this count.')).toBeTruthy();
    expect(screen.queryByLabelText('Bank amount')).toBeNull();
    await fireEvent.press(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByDisplayValue('Fresh after retry')).toBeTruthy();
  } finally {
    await visit.unmount();
    queries.clear();
  }
});

test('an active editing visit retains unsaved input when the cached form refreshes', async () => {
  const api = createApi();
  const count = jest
    .spyOn(api, 'count')
    .mockResolvedValueOnce(initial)
    .mockResolvedValueOnce({ ...initial, explanation: 'Server response during editing' });
  jest.mocked(useApi).mockReturnValue(api);
  const queries = new QueryClient();
  const visit = await render(
    <QueryClientProvider client={queries}>
      <CashCountScreen id={5} isOpening />
    </QueryClientProvider>,
  );
  try {
    await screen.findByLabelText('Bank amount');
    await fireEvent.changeText(screen.getByLabelText('Bank amount'), '100.25');
    await fireEvent.changeText(
      screen.getByLabelText('Explanation / discrepancy notes'),
      'Unsaved notes',
    );
    await act(async () => {
      await queries.invalidateQueries({ queryKey: ['cash-count'] });
    });
    expect(count).toHaveBeenCalledTimes(2);
    expect(screen.getByDisplayValue('100.25')).toBeTruthy();
    expect(screen.getByDisplayValue('Unsaved notes')).toBeTruthy();
  } finally {
    await visit.unmount();
    queries.clear();
  }
});
