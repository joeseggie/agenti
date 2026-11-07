import { ApiFetch } from './client';

export function response(data: unknown, status = 200): Awaited<ReturnType<ApiFetch>> {
  return { ok: status >= 200 && status < 300, status, text: async () => JSON.stringify(data) };
}

export const user = {
  userId: 'test-user',
  email: 'test@example.invalid',
  fullName: 'Test Agent',
  role: 'Agent' as const,
  agentId: 1,
  branchId: 2,
};
export const login = {
  ...user,
  accessToken: 'synthetic-token',
  tokenType: 'Bearer',
  expiresIn: 3600,
};
