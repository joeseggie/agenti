import { ApiFetch } from '@/api/client';
import { z } from 'zod';
import { login, response, user } from '@/api/test-helpers';
import { SessionController, SessionStorage } from './session';

function setup() {
  const storage: jest.Mocked<SessionStorage> = {
    read: jest.fn().mockResolvedValue(null),
    write: jest.fn().mockResolvedValue(undefined),
    remove: jest.fn().mockResolvedValue(undefined),
  };
  const fetcher: jest.MockedFunction<ApiFetch> = jest.fn();
  const clear = jest.fn();
  let now = 1_000_000;
  const session = new SessionController(
    'https://agenti.example/api',
    storage,
    clear,
    fetcher,
    () => now,
  );
  return {
    session,
    storage,
    fetcher,
    clear,
    advance: (ms: number) => {
      now += ms;
    },
  };
}

test('sign-in persists only the token, expiry and API URL, not passwords or account data', async () => {
  const { session, fetcher, storage } = setup();
  fetcher.mockResolvedValue(response({ success: true, data: login }));
  await session.signIn('test@example.invalid', 'synthetic-password');
  const persisted = JSON.parse(storage.write.mock.calls[0]![0]);
  expect(persisted).toEqual({
    accessToken: login.accessToken,
    expiresAt: 4_600_000,
    apiUrl: 'https://agenti.example/api',
  });
  expect(JSON.stringify(persisted)).not.toContain('password');
  expect(session.getSnapshot()).toMatchObject({ status: 'signedIn', user });
});

test('restoration validates the saved session with the backend before showing protected data', async () => {
  const { session, storage, fetcher } = setup();
  storage.read.mockResolvedValue(
    JSON.stringify({ accessToken: 'saved-token', expiresAt: 4_000_000, apiUrl: session.apiUrl }),
  );
  fetcher.mockResolvedValue(response({ success: true, data: user }));
  const promise = session.restore();
  expect(session.getSnapshot().status).toBe('restoring');
  await promise;
  expect(fetcher).toHaveBeenCalledWith(
    `${session.apiUrl}/auth/me`,
    expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer saved-token' }),
    }),
  );
  expect(session.getSnapshot().status).toBe('signedIn');
});

test.each(['expired', 'different-server', 'malformed'])(
  'discards %s stored sessions without sending their token',
  async (kind) => {
    const { session, storage, fetcher } = setup();
    storage.read.mockResolvedValue(
      kind === 'malformed'
        ? '{'
        : JSON.stringify({
            accessToken: 'saved-token',
            expiresAt: kind === 'expired' ? 1 : 4_000_000,
            apiUrl: kind === 'different-server' ? 'https://other.example/api' : session.apiUrl,
          }),
    );
    await session.restore();
    expect(session.getSnapshot().status).toBe('signedOut');
    expect(storage.remove).toHaveBeenCalled();
    expect(fetcher).not.toHaveBeenCalled();
  },
);

test('offline restore blocks protected content but permits retry without deleting a valid token', async () => {
  const { session, storage, fetcher } = setup();
  storage.read.mockResolvedValue(
    JSON.stringify({ accessToken: 'saved-token', expiresAt: 4_000_000, apiUrl: session.apiUrl }),
  );
  fetcher
    .mockRejectedValueOnce(new Error('offline'))
    .mockResolvedValueOnce(response({ success: true, data: user }));
  await session.restore();
  expect(session.getSnapshot().status).toBe('restoreError');
  expect(storage.remove).not.toHaveBeenCalled();
  await session.restore();
  expect(session.getSnapshot().status).toBe('signedIn');
});

test('invalid server session clears secure storage and query cache', async () => {
  const { session, storage, fetcher, clear } = setup();
  storage.read.mockResolvedValue(
    JSON.stringify({ accessToken: 'saved-token', expiresAt: 4_000_000, apiUrl: session.apiUrl }),
  );
  fetcher.mockResolvedValue(response(null, 401));
  await session.restore();
  expect(session.getSnapshot().status).toBe('signedOut');
  expect(storage.remove).toHaveBeenCalled();
  expect(clear).toHaveBeenCalled();
});

test('expiry and sign-out clear both the session and private cache', async () => {
  const { session, storage, fetcher, clear, advance } = setup();
  fetcher.mockResolvedValue(response({ success: true, data: login }));
  await session.signIn('test', 'synthetic-password');
  advance(3_600_001);
  await session.checkExpiry();
  expect(session.getSnapshot()).toMatchObject({ status: 'signedOut' });
  expect(storage.remove).toHaveBeenCalledTimes(1);
  expect(clear).toHaveBeenCalledTimes(2);
});

test('secure-storage failures cannot silently create or restore a logged-in session', async () => {
  const { session, storage, fetcher } = setup();
  fetcher.mockResolvedValue(response({ success: true, data: login }));
  storage.write.mockRejectedValue(new Error('locked'));
  await expect(session.signIn('test', 'synthetic')).rejects.toThrow('securely save');
  expect(session.getSnapshot().status).not.toBe('signedIn');
  storage.remove.mockRejectedValueOnce(new Error('locked'));
  await session.signOut();
  expect(session.getSnapshot()).toMatchObject({
    status: 'restoreError',
    user: null,
    retry: 'clear',
  });
  await session.signOut();
  expect(session.getSnapshot().status).toBe('signedOut');
});

test('sign-out during sign-in prevents the late result from restoring authentication', async () => {
  const { session, fetcher, storage } = setup();
  let complete: ((value: Awaited<ReturnType<ApiFetch>>) => void) | undefined;
  fetcher.mockImplementation(
    () =>
      new Promise((resolve) => {
        complete = resolve;
      }),
  );
  const pending = session.signIn('test', 'synthetic');
  await session.signOut();
  complete!(response({ success: true, data: login }));
  await pending;
  expect(session.getSnapshot().status).toBe('signedOut');
  expect(storage.write).not.toHaveBeenCalled();
});

test('a late unauthorized response from an old session cannot clear a newer login', async () => {
  const { session, fetcher, storage } = setup();
  fetcher.mockResolvedValueOnce(response({ success: true, data: login }));
  await session.signIn('test', 'synthetic');
  let complete: ((value: Awaited<ReturnType<ApiFetch>>) => void) | undefined;
  fetcher.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        complete = resolve;
      }),
  );
  const pending = session.client.request('/dashboard', z.unknown());
  await session.signOut();
  fetcher.mockResolvedValueOnce(
    response({ success: true, data: { ...login, accessToken: 'new-synthetic-token' } }),
  );
  await session.signIn('test', 'synthetic');
  const rejected = expect(pending).rejects.toMatchObject({ status: 401 });
  complete!(response(null, 401));
  await rejected;
  expect(session.getSnapshot().status).toBe('signedIn');
  expect(storage.remove).toHaveBeenCalledTimes(1);
});

test('foreground revalidation updates assignments and clears private data only when scope changes', async () => {
  const { session, fetcher, clear } = setup();
  fetcher.mockResolvedValueOnce(response({ success: true, data: login }));
  await session.signIn('test', 'synthetic');
  clear.mockClear();
  fetcher.mockResolvedValueOnce(
    response({ success: true, data: { ...user, fullName: 'Updated display name' } }),
  );
  await session.revalidate();
  expect(clear).not.toHaveBeenCalled();
  expect(session.getSnapshot()).toMatchObject({
    status: 'signedIn',
    user: { fullName: 'Updated display name' },
  });

  fetcher.mockResolvedValueOnce(
    response({ success: true, data: { ...user, branchId: 7, agentId: 9 } }),
  );
  await session.revalidate();
  expect(fetcher).toHaveBeenLastCalledWith(
    `${session.apiUrl}/auth/me`,
    expect.objectContaining({
      headers: expect.objectContaining({
        Authorization: `Bearer ${login.accessToken}`,
      }),
    }),
  );
  expect(clear).toHaveBeenCalledTimes(1);
  expect(session.getSnapshot()).toMatchObject({
    status: 'signedIn',
    user: { branchId: 7, agentId: 9 },
  });
});

test('failed foreground verification blocks cached data without discarding the saved token', async () => {
  const { session, storage, fetcher, clear } = setup();
  fetcher.mockResolvedValueOnce(response({ success: true, data: login }));
  await session.signIn('test', 'synthetic');
  clear.mockClear();
  fetcher.mockRejectedValueOnce(new Error('offline'));
  await session.revalidate();
  expect(clear).toHaveBeenCalledTimes(1);
  expect(storage.remove).not.toHaveBeenCalled();
  expect(session.getSnapshot()).toMatchObject({
    status: 'restoreError',
    user: null,
    retry: 'restore',
  });
});

test('foreground revalidation never revives a signed-out or newer session', async () => {
  const { session, fetcher } = setup();
  fetcher.mockResolvedValueOnce(response({ success: true, data: login }));
  await session.signIn('test', 'synthetic');
  const old = Promise.withResolvers<Awaited<ReturnType<ApiFetch>>>();
  fetcher.mockReturnValueOnce(old.promise);
  const pending = session.revalidate();
  await Promise.resolve();
  await session.signOut();
  fetcher.mockResolvedValueOnce(
    response({
      success: true,
      data: {
        ...login,
        userId: 'another-user',
        accessToken: 'another-synthetic-token',
      },
    }),
  );
  await session.signIn('another', 'synthetic');
  old.resolve(response({ success: true, data: { ...user, branchId: 99 } }));
  await pending;
  expect(session.getSnapshot()).toMatchObject({
    status: 'signedIn',
    user: { userId: 'another-user', branchId: user.branchId },
  });
});
