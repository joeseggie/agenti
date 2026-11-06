import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { useSession } from '@/auth/session-provider';
import { ApiError } from '@/api/client';
import { SignInScreen } from './sign-in';
import { SessionController } from '@/auth/session';

jest.mock('@/auth/session-provider', () => ({ useSession: jest.fn() }));

const controller = new SessionController(
  'https://agenti.example/api',
  {
    read: async () => null,
    write: async () => undefined,
    remove: async () => undefined,
  },
  () => undefined,
);
const signIn = jest.spyOn(controller, 'signIn');

beforeEach(() => {
  signIn.mockReset();
  jest.mocked(useSession).mockReturnValue({
    status: 'signedOut',
    user: null,
    error: null,
    controller,
  });
});

test('sign-in failure retains fields, shows a useful error and supports retry', async () => {
  signIn
    .mockRejectedValueOnce(new ApiError('Cannot reach Agenti.'))
    .mockResolvedValueOnce(undefined);
  await render(<SignInScreen />);
  await fireEvent.changeText(screen.getByLabelText('Email'), 'test@example.invalid');
  await fireEvent.changeText(screen.getByLabelText('Password'), 'synthetic-password');
  await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));
  expect(await screen.findByText('Cannot reach Agenti.')).toBeTruthy();
  expect(screen.getByDisplayValue('synthetic-password')).toBeTruthy();
  await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));
  expect(signIn).toHaveBeenCalledTimes(2);
  expect(screen.queryByDisplayValue('synthetic-password')).toBeNull();
});

test('a pending sign-in cannot be submitted twice', async () => {
  let finish: (() => void) | undefined;
  signIn.mockImplementation(
    () =>
      new Promise<void>((resolve) => {
        finish = resolve;
      }),
  );
  await render(<SignInScreen />);
  await fireEvent.changeText(screen.getByLabelText('Email'), 'test@example.invalid');
  await fireEvent.changeText(screen.getByLabelText('Password'), 'synthetic-password');
  await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));
  expect(screen.getByRole('button', { name: 'Signing in...' })).toBeDisabled();
  await fireEvent.press(screen.getByRole('button', { name: 'Signing in...' }));
  expect(signIn).toHaveBeenCalledTimes(1);
  await act(async () => {
    finish!();
  });
});
