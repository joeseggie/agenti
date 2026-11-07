import { Text, AppState, AppStateStatus } from 'react-native';
import { act, render, waitFor } from '@testing-library/react-native';
import { SessionController } from './session';
import { SessionProvider } from './session-provider';

test('returning to the foreground revalidates the server account instead of only checking expiry', async () => {
  const restore = jest.spyOn(SessionController.prototype, 'restore').mockResolvedValue(undefined);
  const revalidate = jest
    .spyOn(SessionController.prototype, 'revalidate')
    .mockResolvedValue(undefined);
  let onState: ((state: AppStateStatus) => void) | undefined;
  const remove = jest.fn();
  const subscribe = jest
    .spyOn(AppState, 'addEventListener')
    .mockImplementation((_type, listener) => {
      onState = listener;
      return { remove };
    });
  const view = await render(
    <SessionProvider apiUrl="https://agenti.example/api">
      <Text>Session test</Text>
    </SessionProvider>,
  );
  try {
    await act(async () => {
      onState?.('background');
    });
    expect(revalidate).not.toHaveBeenCalled();
    await act(async () => {
      onState?.('active');
    });
    await waitFor(() => expect(revalidate).toHaveBeenCalledTimes(1));
  } finally {
    await view.unmount();
    subscribe.mockRestore();
    revalidate.mockRestore();
    restore.mockRestore();
  }
});
