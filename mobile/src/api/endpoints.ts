import { z } from 'zod';
import { ApiClient } from './client';
import {
  agentDetailSchema,
  agentSchema,
  cashCountFormSchema,
  cashSessionDetailSchema,
  cashSessionListSchema,
  countSaveSchema,
  currentSessionSchema,
  dashboardSchema,
  vaultOperationSchema,
  vaultSchema,
  vaultTransactionSchema,
  CashCountForm,
} from './contracts';

export function createEndpoints(client: ApiClient) {
  return {
    dashboard: (signal?: AbortSignal) => client.request('/dashboard', dashboardSchema, { signal }),
    agents: (signal?: AbortSignal) => client.request('/agents', z.array(agentSchema), { signal }),
    agent: (id: number, signal?: AbortSignal) =>
      client.request(`/agents/${id}`, agentDetailSchema, { signal }),
    sessions: (signal?: AbortSignal) =>
      client.request('/cash-sessions', z.array(cashSessionListSchema), { signal }),
    session: (id: number, signal?: AbortSignal) =>
      client.request(`/cash-sessions/${id}`, cashSessionDetailSchema, { signal }),
    currentCount: (signal?: AbortSignal) =>
      client.request('/cash-counts/current', currentSessionSchema, { signal }),
    count: (id: number, signal?: AbortSignal) =>
      client.request(`/cash-counts/${id}`, cashCountFormSchema, { signal }),
    initializeCount: (isOpening: boolean, signal?: AbortSignal) =>
      client.request(`/cash-counts/initialize?isOpening=${isOpening}`, cashCountFormSchema, {
        signal,
      }),
    saveCount: (body: CashCountForm) =>
      client.request('/cash-counts', countSaveSchema, { method: 'POST', body }),
    submitCount: (id: number) =>
      client.request(`/cash-counts/${id}/submit`, countSaveSchema, { method: 'POST' }),
    vault: (branchId: number, signal?: AbortSignal) =>
      client.request(`/vault/${branchId}`, vaultSchema, { signal }),
    vaultTransactions: (branchId: number, signal?: AbortSignal) =>
      client.request(`/vault/${branchId}/transactions?take=50`, z.array(vaultTransactionSchema), {
        signal,
      }),
    requestAdjustment: (
      branchId: number,
      body: { amount: number; isDeposit: boolean; notes: string },
    ) =>
      client.request(`/vault/${branchId}/adjustment`, vaultOperationSchema, {
        method: 'POST',
        body,
      }),
    reviewAdjustment: (id: number, approve: boolean) =>
      client.request(
        `/vault/adjustment/${id}/${approve ? 'approve' : 'reject'}`,
        vaultOperationSchema,
        { method: 'POST' },
      ),
  };
}
