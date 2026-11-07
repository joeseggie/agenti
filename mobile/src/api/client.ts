import { fetch as expoFetch } from 'expo/fetch';
import { z } from 'zod';

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status = 0,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export function normalizeApiUrl(value: string | undefined, development = false): string {
  if (!value?.trim()) throw new Error('Set EXPO_PUBLIC_API_URL to your Agenti HTTPS API URL.');
  let url: URL;
  try {
    url = new URL(value.trim());
  } catch {
    throw new Error('EXPO_PUBLIC_API_URL must be an absolute HTTPS URL ending in /api.');
  }
  const local = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
  if (url.protocol !== 'https:' && !(development && local && url.protocol === 'http:')) {
    throw new Error('The Agenti API must use HTTPS. HTTP is only allowed for local development.');
  }
  if (
    url.username ||
    url.password ||
    url.search ||
    url.hash ||
    !url.pathname.replace(/\/$/, '').endsWith('/api')
  ) {
    throw new Error(
      'Use an API URL ending in /api without credentials, query parameters, or fragments.',
    );
  }
  return url.toString().replace(/\/$/, '');
}

type RequestOptions = {
  method?: 'GET' | 'POST';
  body?: unknown;
  signal?: AbortSignal;
  anonymous?: boolean;
};

export type ApiFetch = (
  url: string,
  options?: Parameters<typeof expoFetch>[1],
) => Promise<Pick<Awaited<ReturnType<typeof expoFetch>>, 'ok' | 'status' | 'text'>>;

type ClientOptions = {
  baseUrl: string;
  getToken: () => string | null;
  onUnauthorized: (token: string) => Promise<void>;
  fetcher?: ApiFetch;
  timeoutMs?: number;
};

const envelopeSchema = z.object({
  success: z.boolean(),
  data: z.unknown().optional(),
  error: z.string().nullable().optional(),
});

export function createApiClient({
  baseUrl,
  getToken,
  onUnauthorized,
  fetcher = expoFetch,
  timeoutMs = 15_000,
}: ClientOptions) {
  return {
    async request<T>(path: string, schema: z.ZodType<T>, options: RequestOptions = {}): Promise<T> {
      if (
        !path.startsWith('/') ||
        path.startsWith('//') ||
        path.includes('\\') ||
        path.includes('..')
      ) {
        throw new ApiError('Invalid API route.');
      }
      const token = options.anonymous ? null : getToken();
      if (!options.anonymous && !token) throw new ApiError('Sign in to continue.', 401);
      const controller = new AbortController();
      const cancel = () => controller.abort();
      options.signal?.addEventListener('abort', cancel, { once: true });
      if (options.signal?.aborted) cancel();
      let timedOut = false;
      const timeout = setTimeout(() => {
        timedOut = true;
        controller.abort();
      }, timeoutMs);
      try {
        const response = await fetcher(`${baseUrl}${path}`, {
          method: options.method ?? 'GET',
          headers: {
            Accept: 'application/json',
            ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' }),
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
          },
          credentials: 'omit',
          redirect: 'error',
          body: options.body === undefined ? undefined : JSON.stringify(options.body),
          signal: controller.signal,
        });
        if (response.status === 401) {
          if (token) await onUnauthorized(token);
          throw new ApiError(
            options.anonymous
              ? 'Email or password is incorrect, or this account is unavailable.'
              : 'Your session has expired. Please sign in again.',
            401,
          );
        }
        if (response.status === 403)
          throw new ApiError('You do not have permission to access this resource.', 403);
        if (response.status >= 500)
          throw new ApiError(
            'Agenti is unavailable. Refresh the status before retrying a saved operation.',
            response.status,
          );
        const text = await response.text();
        let payload: unknown;
        try {
          payload = JSON.parse(text);
        } catch {
          throw new ApiError(
            'Agenti returned an unexpected response. Check the API URL or try again.',
            response.status,
          );
        }
        const envelope = envelopeSchema.safeParse(payload);
        if (!envelope.success)
          throw new ApiError('Agenti returned an unsupported response.', response.status);
        if (!response.ok || !envelope.data.success) {
          throw new ApiError(
            envelope.data.error || 'The request could not be completed.',
            response.status,
          );
        }
        const data = schema.safeParse(envelope.data.data);
        if (!data.success)
          throw new ApiError(
            'This response is not supported by this app version.',
            response.status,
          );
        return data.data;
      } catch (error) {
        if (error instanceof ApiError) throw error;
        if (options.signal?.aborted) throw new ApiError('Request cancelled.');
        if (timedOut)
          throw new ApiError('The request timed out. Check the current status before retrying.');
        throw new ApiError('Cannot reach Agenti. Check your connection and try again.');
      } finally {
        clearTimeout(timeout);
        options.signal?.removeEventListener('abort', cancel);
      }
    },
  };
}

export type ApiClient = ReturnType<typeof createApiClient>;
