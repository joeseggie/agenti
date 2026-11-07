import * as SecureStore from 'expo-secure-store';
import { ApiError } from './client';

export type VaultRequestScope = { apiUrl: string; userId: string; branchId: number };
export interface VaultPendingStorage {
  read(scope: VaultRequestScope): Promise<boolean>;
  write(scope: VaultRequestScope): Promise<void>;
  remove(scope: VaultRequestScope): Promise<void>;
}

const markerKey = 'agenti.vault-pending.v1';
function scopeIdentity(scope: VaultRequestScope): string {
  return JSON.stringify([scope.apiUrl, scope.userId, scope.branchId]);
}

function storageOptions(scope: VaultRequestScope): SecureStore.SecureStoreOptions {
  return {
    keychainService: `agenti-vault-${scopeIdentity(scope)}`,
    keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
  };
}

const secureStorage: VaultPendingStorage = {
  async read(scope) {
    try {
      return (await SecureStore.getItemAsync(markerKey, storageOptions(scope))) !== null;
    } catch {
      throw new Error('Cannot check the saved vault request. Unlock your device and retry.');
    }
  },
  async write(scope) {
    try {
      await SecureStore.setItemAsync(markerKey, 'pending', storageOptions(scope));
    } catch {
      throw new Error(
        'Cannot securely record this vault request. No request was sent. Unlock your device and retry.',
      );
    }
  },
  async remove(scope) {
    try {
      await SecureStore.deleteItemAsync(markerKey, storageOptions(scope));
    } catch {
      throw new Error(
        'Cannot clear the saved vault request. Check activity and retry after unlocking your device.',
      );
    }
  },
};

export class VaultRequestGuard {
  private storageTail: Promise<void> = Promise.resolve();
  private readonly inFlight = new Set<string>();

  constructor(private readonly storage: VaultPendingStorage) {}

  private serialize<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.storageTail.then(operation);
    this.storageTail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }

  isPending(scope: VaultRequestScope) {
    return this.serialize(() => this.storage.read(scope));
  }

  acknowledge(scope: VaultRequestScope) {
    return this.serialize(() => {
      if (this.inFlight.has(scopeIdentity(scope)))
        throw new Error(
          'The vault request is still in progress. Wait for its result before checking activity.',
        );
      return this.storage.remove(scope);
    });
  }

  async execute<T>(scope: VaultRequestScope, operation: () => Promise<T>): Promise<T> {
    const identity = scopeIdentity(scope);
    await this.serialize(async () => {
      if (await this.storage.read(scope))
        throw new Error(
          'Review the unresolved vault request in activity before starting another request.',
        );
      // Write ahead of the network call so termination or navigation cannot lose the guard.
      await this.storage.write(scope);
      this.inFlight.add(identity);
    });
    try {
      const result = await operation();
      await this.serialize(() => this.storage.remove(scope));
      return result;
    } catch (error) {
      if (error instanceof ApiError && error.status >= 400 && error.status < 500)
        await this.serialize(() => this.storage.remove(scope));
      throw error;
    } finally {
      this.inFlight.delete(identity);
    }
  }
}

export const vaultRequestGuard = new VaultRequestGuard(secureStorage);
