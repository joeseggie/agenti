import { z } from 'zod';
import { ApiClient, ApiError, createApiClient } from '@/api/client';
import { loginSchema, SessionUser, sessionUserSchema } from '@/api/contracts';

const storedSessionSchema = z.object({
  accessToken: z.string().min(1),
  expiresAt: z.number().finite().positive(),
  apiUrl: z.string(),
});
type StoredSession = z.infer<typeof storedSessionSchema>;
export type SessionState =
  | { status: 'restoring'; user: null; error: null }
  | { status: 'signedOut'; user: null; error: string | null }
  | { status: 'restoreError'; user: null; error: string; retry: 'restore' | 'clear' }
  | { status: 'signedIn'; user: SessionUser; expiresAt: number; error: null };

export interface SessionStorage {
  read(): Promise<string | null>;
  write(value: string): Promise<void>;
  remove(): Promise<void>;
}

export class SessionController {
  readonly client: ApiClient;
  private stored: StoredSession | null = null;
  private state: SessionState = { status: 'restoring', user: null, error: null };
  private readonly listeners = new Set<() => void>();
  private generation = 0;
  private revalidationAttempt = 0;
  // Serializing storage prevents an old sign-out from deleting a new sign-in.
  private storageTail: Promise<void> = Promise.resolve();

  constructor(
    readonly apiUrl: string,
    private readonly storage: SessionStorage,
    private readonly clearPrivateData: () => void,
    fetcher?: Parameters<typeof createApiClient>[0]['fetcher'],
    private readonly now: () => number = Date.now,
  ) {
    this.client = createApiClient({
      baseUrl: apiUrl,
      fetcher,
      getToken: () =>
        this.stored && this.stored.expiresAt > this.now() ? this.stored.accessToken : null,
      onUnauthorized: async (token) => {
        if (this.stored?.accessToken === token)
          await this.signOut('Your session has expired. Please sign in again.');
      },
    });
  }

  getSnapshot = () => this.state;
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  private update(state: SessionState) {
    this.state = state;
    this.listeners.forEach((listener) => listener());
  }

  private store(operation: () => Promise<void>) {
    const result = this.storageTail.then(operation);
    // The caller receives the failure; keep the queue usable for a deliberate retry.
    this.storageTail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }

  async restore() {
    const generation = ++this.generation;
    this.update({ status: 'restoring', user: null, error: null });
    try {
      await this.storageTail;
      const raw = await this.storage.read();
      if (generation !== this.generation) return;
      if (!raw) {
        this.update({ status: 'signedOut', user: null, error: null });
        return;
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(raw);
      } catch {
        await this.signOut('The saved session could not be read. Please sign in again.');
        return;
      }
      const result = storedSessionSchema.safeParse(parsed);
      if (
        !result.success ||
        result.data.apiUrl !== this.apiUrl ||
        result.data.expiresAt <= this.now()
      ) {
        await this.signOut(
          'Your saved session has expired or the API has changed. Please sign in again.',
        );
        return;
      }
      this.stored = result.data;
      const user = await this.client.request('/auth/me', sessionUserSchema);
      if (generation !== this.generation) return;
      if (this.stored.expiresAt <= this.now()) {
        await this.signOut('Your session has expired. Please sign in again.');
        return;
      }
      this.update({ status: 'signedIn', user, expiresAt: this.stored.expiresAt, error: null });
    } catch (error) {
      if (generation !== this.generation) return;
      this.stored = null;
      this.clearPrivateData();
      this.update({
        status: 'restoreError',
        user: null,
        retry: 'restore',
        error:
          error instanceof ApiError
            ? error.message
            : 'Cannot read secure storage. Unlock your device and retry, or clear the saved session.',
      });
    }
  }

  async signIn(email: string, password: string) {
    const generation = ++this.generation;
    const startedAt = this.now();
    const login = await this.client.request('/auth/login', loginSchema, {
      method: 'POST',
      anonymous: true,
      body: { email: email.trim(), password },
    });
    if (generation !== this.generation) return;
    const stored = {
      accessToken: login.accessToken,
      expiresAt: startedAt + login.expiresIn * 1_000,
      apiUrl: this.apiUrl,
    };
    if (stored.expiresAt <= this.now())
      throw new ApiError('Sign-in took too long. Please try again.');
    try {
      await this.store(() => this.storage.write(JSON.stringify(stored)));
    } catch {
      throw new ApiError('Cannot securely save this session. Unlock your device and try again.');
    }
    if (generation !== this.generation) return;
    this.clearPrivateData();
    this.stored = stored;
    this.update({
      status: 'signedIn',
      user: sessionUserSchema.parse(login),
      expiresAt: stored.expiresAt,
      error: null,
    });
  }

  async signOut(message: string | null = null) {
    const generation = ++this.generation;
    this.stored = null;
    this.clearPrivateData();
    this.update({ status: 'signedOut', user: null, error: message });
    try {
      await this.store(() => this.storage.remove());
    } catch {
      if (generation === this.generation)
        this.update({
          status: 'restoreError',
          user: null,
          retry: 'clear',
          error:
            'Signed out in memory, but secure storage could not be cleared. Unlock your device and retry clearing it.',
        });
    }
  }

  async checkExpiry() {
    if (this.stored && this.stored.expiresAt <= this.now()) {
      await this.signOut('Your session has expired. Please sign in again.');
    }
  }

  async revalidate() {
    if (this.state.status !== 'signedIn' || !this.stored) return;
    await this.checkExpiry();
    if (this.state.status !== 'signedIn' || !this.stored) return;
    const generation = this.generation;
    const attempt = ++this.revalidationAttempt;
    const previous = this.state.user;
    try {
      const user = await this.client.request('/auth/me', sessionUserSchema);
      if (generation !== this.generation || attempt !== this.revalidationAttempt) return;
      await this.checkExpiry();
      if (generation !== this.generation || attempt !== this.revalidationAttempt || !this.stored)
        return;
      if (
        user.userId !== previous.userId ||
        user.role !== previous.role ||
        user.agentId !== previous.agentId ||
        user.branchId !== previous.branchId
      ) {
        this.clearPrivateData();
      }
      this.update({ status: 'signedIn', user, expiresAt: this.stored.expiresAt, error: null });
    } catch (error) {
      if (generation !== this.generation || attempt !== this.revalidationAttempt) return;
      this.stored = null;
      this.clearPrivateData();
      this.update({
        status: 'restoreError',
        user: null,
        retry: 'restore',
        error:
          error instanceof ApiError
            ? error.message
            : 'Cannot verify your account. Retry before continuing.',
      });
    }
  }
}
