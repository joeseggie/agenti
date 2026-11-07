import { ApiError, ApiFetch, createApiClient } from './client';
import { vaultOperationSchema } from './contracts';
import { response } from './test-helpers';
import { VaultPendingStorage, VaultRequestGuard, VaultRequestScope } from './vault-request-guard';

const scope: VaultRequestScope = {
  apiUrl: 'https://agenti.example/api',
  userId: 'test-user',
  branchId: 2,
};

function setup() {
  const entries = new Set<string>();
  const storage: jest.Mocked<VaultPendingStorage> = {
    read: jest.fn(async (value) => entries.has(JSON.stringify(value))),
    write: jest.fn(async (value) => {
      entries.add(JSON.stringify(value));
    }),
    remove: jest.fn(async (value) => {
      entries.delete(JSON.stringify(value));
    }),
  };
  return { storage, guard: new VaultRequestGuard(storage) };
}

test('an unresolved request survives a new guard instance and stays scoped to its account and server', async () => {
  const { storage, guard } = setup();
  const send = jest.fn().mockRejectedValue(new ApiError('Timed out.'));
  await expect(guard.execute(scope, send)).rejects.toThrow('Timed out');
  const restarted = new VaultRequestGuard(storage);
  expect(await restarted.isPending(scope)).toBe(true);
  await expect(restarted.execute(scope, send)).rejects.toThrow('Review the unresolved');
  expect(send).toHaveBeenCalledTimes(1);
  for (const other of [
    { ...scope, userId: 'other-user' },
    { ...scope, branchId: 3 },
    { ...scope, apiUrl: 'https://other.example/api' },
  ])
    expect(await restarted.isPending(other)).toBe(false);
  await restarted.acknowledge(scope);
  expect(await restarted.isPending(scope)).toBe(false);
});

test('the durable marker is written before sending and concurrent requests cannot bypass it', async () => {
  const { guard, storage } = setup();
  const reply = Promise.withResolvers<string>();
  const send = jest.fn(async () => {
    expect(await storage.read(scope)).toBe(true);
    return reply.promise;
  });
  const first = guard.execute(scope, send);
  await expect(guard.execute(scope, send)).rejects.toThrow('Review the unresolved');
  await expect(guard.acknowledge(scope)).rejects.toThrow('still in progress');
  reply.resolve('confirmed');
  await expect(first).resolves.toBe('confirmed');
  expect(send).toHaveBeenCalledTimes(1);
  expect(await guard.isPending(scope)).toBe(false);
});

test.each([400, 401, 403, 409, 429])(
  'a definitive %s rejection clears the marker',
  async (status) => {
    const { guard } = setup();
    await expect(
      guard.execute(scope, async () => {
        throw new ApiError('Rejected.', status);
      }),
    ).rejects.toThrow('Rejected');
    expect(await guard.isPending(scope)).toBe(false);
  },
);

test.each(['invalid-json', 'invalid-envelope', 'invalid-contract', 'server-failure'])(
  '%s after sending remains ambiguous and blocks another adjustment',
  async (kind) => {
    const { guard } = setup();
    const fetcher: jest.MockedFunction<ApiFetch> = jest
      .fn()
      .mockResolvedValue(
        kind === 'invalid-json'
          ? { ok: true, status: 200, text: async () => '<html>Unavailable</html>' }
          : kind === 'invalid-envelope'
            ? response({ result: 'unknown' })
            : kind === 'invalid-contract'
              ? response({ success: true, data: { success: true, transactionId: 'invalid' } })
              : response(null, 500),
      );
    const client = createApiClient({
      baseUrl: scope.apiUrl,
      fetcher,
      getToken: () => 'synthetic-token',
      onUnauthorized: async () => undefined,
    });
    const send = () =>
      client.request('/vault/2/adjustment', vaultOperationSchema, { method: 'POST', body: {} });
    await expect(guard.execute(scope, send)).rejects.toBeInstanceOf(ApiError);
    expect(await guard.isPending(scope)).toBe(true);
    await expect(guard.execute(scope, send)).rejects.toThrow('Review the unresolved');
    expect(fetcher).toHaveBeenCalledTimes(1);
  },
);

test('storage failures prevent sending and a failed clear never permits a duplicate', async () => {
  const { guard, storage } = setup();
  const send = jest.fn().mockResolvedValue('confirmed');
  storage.write.mockRejectedValueOnce(new Error('locked'));
  await expect(guard.execute(scope, send)).rejects.toThrow('locked');
  expect(send).not.toHaveBeenCalled();
  storage.remove.mockRejectedValueOnce(new Error('cannot clear'));
  await expect(guard.execute(scope, send)).rejects.toThrow('cannot clear');
  expect(await guard.isPending(scope)).toBe(true);
  await expect(guard.execute(scope, send)).rejects.toThrow('Review the unresolved');
  expect(send).toHaveBeenCalledTimes(1);
});
