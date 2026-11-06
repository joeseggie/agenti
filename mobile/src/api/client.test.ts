import { z } from 'zod';
import { ApiError, ApiFetch, createApiClient, normalizeApiUrl } from './client';
import { response } from './test-helpers';

function setup() {
  const fetcher: jest.MockedFunction<ApiFetch> = jest.fn();
  const unauthorized = jest.fn<Promise<void>, [string]>().mockResolvedValue(undefined);
  const client = createApiClient({
    baseUrl: 'https://agenti.example/api',
    getToken: () => 'synthetic-token',
    onUnauthorized: unauthorized,
    fetcher,
    timeoutMs: 50,
  });
  return { fetcher, unauthorized, client };
}

test('normalizes the API URL and rejects insecure non-local destinations', () => {
  expect(normalizeApiUrl(' https://agenti.example/api/ ')).toBe('https://agenti.example/api');
  expect(normalizeApiUrl('http://127.0.0.1:5113/api', true)).toBe('http://127.0.0.1:5113/api');
  for (const url of [
    'http://agenti.example/api',
    'http://127.0.0.1/api',
    'https://user:password@example.com/api',
    'https://example.com/api?token=x',
    'https://example.com',
    'invalid',
  ]) {
    expect(() => normalizeApiUrl(url)).toThrow();
  }
});

test('injects bearer credentials, decodes the envelope and disables cookies and redirects', async () => {
  const { fetcher, client } = setup();
  fetcher.mockResolvedValue(response({ success: true, data: { total: 42 } }));
  await expect(client.request('/dashboard', z.object({ total: z.number() }))).resolves.toEqual({
    total: 42,
  });
  expect(fetcher).toHaveBeenCalledWith(
    'https://agenti.example/api/dashboard',
    expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer synthetic-token' }),
      credentials: 'omit',
      redirect: 'error',
    }),
  );
  await expect(client.request('//other.example', z.unknown())).rejects.toThrow('Invalid API route');
  expect(fetcher).toHaveBeenCalledTimes(1);
});

test('login never sends a previous token or invalidates a session on bad credentials', async () => {
  const { fetcher, client, unauthorized } = setup();
  fetcher.mockResolvedValue(response(null, 401));
  await expect(
    client.request('/auth/login', z.unknown(), {
      anonymous: true,
      method: 'POST',
      body: { email: 'test', password: 'synthetic' },
    }),
  ).rejects.toThrow('Email or password');
  expect(fetcher.mock.calls[0]?.[1]?.headers).not.toHaveProperty('Authorization');
  expect(unauthorized).not.toHaveBeenCalled();
});

test('protected 401 invalidates its exact token, but 403 leaves the session intact', async () => {
  const { fetcher, client, unauthorized } = setup();
  fetcher.mockResolvedValueOnce(response(null, 401)).mockResolvedValueOnce(response(null, 403));
  await expect(client.request('/dashboard', z.unknown())).rejects.toMatchObject({ status: 401 });
  expect(unauthorized).toHaveBeenCalledWith('synthetic-token');
  await expect(client.request('/vault/1', z.unknown())).rejects.toMatchObject({ status: 403 });
  expect(unauthorized).toHaveBeenCalledTimes(1);
});

test('surfaces validation errors, malformed JSON and incompatible DTOs', async () => {
  const { fetcher, client } = setup();
  fetcher.mockResolvedValueOnce(response({ success: false, error: 'Notes are required.' }, 400));
  await expect(client.request('/cash-counts', z.unknown())).rejects.toThrow('Notes are required.');
  fetcher.mockResolvedValueOnce({ ok: true, status: 200, text: async () => '<html>Login</html>' });
  await expect(client.request('/dashboard', z.object({}))).rejects.toThrow('unexpected response');
  fetcher.mockResolvedValueOnce(response({ success: true, data: { total: 'not-money' } }));
  await expect(client.request('/dashboard', z.object({ total: z.number() }))).rejects.toThrow(
    'not supported',
  );
});

test('times out, supports caller cancellation and never retries a mutation', async () => {
  const { fetcher, client } = setup();
  fetcher.mockImplementation(
    async (_url, options) =>
      new Promise((_resolve, reject) => {
        options?.signal?.addEventListener('abort', () => reject(new Error('abort')));
      }),
  );
  await expect(
    client.request('/vault/1/adjustment', z.unknown(), { method: 'POST', body: {} }),
  ).rejects.toThrow('timed out');
  expect(fetcher).toHaveBeenCalledTimes(1);
  const controller = new AbortController();
  const request = client.request('/dashboard', z.unknown(), { signal: controller.signal });
  controller.abort();
  await expect(request).rejects.toThrow('cancelled');
});

test('network failures are safe user-facing errors without original request details', async () => {
  const { fetcher, client } = setup();
  fetcher.mockRejectedValue(new Error('private network diagnostic'));
  await expect(client.request('/dashboard', z.unknown())).rejects.toEqual(
    new ApiError('Cannot reach Agenti. Check your connection and try again.'),
  );
});
