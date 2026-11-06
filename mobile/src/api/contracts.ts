import { z } from 'zod';

const id = z.number().int().positive().max(Number.MAX_SAFE_INTEGER);
const money = z.number().finite();
const timestamp = z.string();

export const sessionUserSchema = z.object({
  userId: z.string().min(1),
  email: z.string(),
  fullName: z.string(),
  role: z.enum(['Agent', 'Supervisor', 'Admin']),
  agentId: id.nullable(),
  branchId: id.nullable(),
});

export const loginSchema = sessionUserSchema.extend({
  accessToken: z.string().min(1),
  tokenType: z.literal('Bearer'),
  expiresIn: z.number().int().positive().max(2_147_483),
});

export const walletSchema = z.object({
  id,
  name: z.string(),
  walletTypeName: z.string(),
  currency: z.string(),
  balance: money,
  isActive: z.boolean(),
});

export const agentSchema = z.object({
  id,
  code: z.string(),
  fullName: z.string(),
  email: z.string().nullable(),
  phoneNumber: z.string().nullable(),
  branchId: id.nullable(),
  isActive: z.boolean(),
  walletCount: z.number().int(),
  totalBalance: money,
});

export const agentDetailSchema = agentSchema.omit({ walletCount: true }).extend({
  wallets: z.array(walletSchema),
});

export const dashboardSchema = z.object({
  wallets: z.array(
    z.object({
      walletId: id,
      walletName: z.string(),
      walletTypeName: z.string(),
      balance: money,
      currency: z.string(),
    }),
  ),
  sessionStatus: z.object({
    sessionId: id.nullable(),
    statusDisplay: z.string(),
    balanceBasis: z.string(),
    hasActiveSession: z.boolean(),
  }),
  vaultBalance: money,
  totalBalance: money,
  currency: z.string(),
});

export const cashSessionSchema = z.object({
  id,
  sessionDate: z.string(),
  status: z.number().int(),
  openedAt: timestamp,
  closedAt: timestamp.nullable(),
  workflowVersion: z.number().int(),
  operationalClosedAt: timestamp.nullable(),
  totalOpeningAmount: money,
  totalClosingAmount: money.nullable(),
  totalReportedClosingAmount: money.nullable(),
});

export const cashSessionListSchema = cashSessionSchema.extend({
  workflowDisplay: z.string(),
  agentCount: z.number().int(),
  pendingApprovalCount: z.number().int(),
});

const countSummarySchema = z.object({
  id,
  status: z.number().int(),
  totalAmount: money,
  explanation: z.string().nullable(),
  rejectionReason: z.string().nullable(),
  walletEntries: z.array(
    z.object({
      walletId: id,
      walletName: z.string(),
      amount: money,
      observedAt: timestamp.nullable(),
    }),
  ),
});

export const cashSessionDetailSchema = cashSessionSchema.extend({
  agentSummaries: z.array(
    z.object({
      agentId: id,
      agentName: z.string(),
      agentCode: z.string(),
      openingCount: countSummarySchema.nullable(),
      closingCount: countSummarySchema.nullable(),
      totalAdjustments: money,
    }),
  ),
});

export const currentSessionSchema = z.object({
  sessionId: id.nullable(),
  sessionDate: z.string().nullable(),
  workflowVersion: z.number().int(),
  statusText: z.string(),
  blockReason: z.string().nullable(),
  canPerformOpeningCount: z.boolean(),
  canPerformClosingCount: z.boolean(),
  openingCountId: id.nullable(),
  closingCountId: id.nullable(),
  openingCountStatus: z.number().int().nullable(),
  closingCountStatus: z.number().int().nullable(),
});

export const denominationsSchema = z.object({
  notes: z.record(z.string(), z.number().int().nonnegative()),
  coins: z.record(z.string(), z.number().int().nonnegative()),
});

export const cashCountFormSchema = z.object({
  cashCountId: id.nullable(),
  cashSessionId: id.nullable(),
  isOpening: z.boolean(),
  countDate: z.string().nullable(),
  explanation: z.string().nullable(),
  isReadOnly: z.boolean(),
  previousClosingTotal: money.nullable(),
  previousClosingDate: z.string().nullable(),
  walletEntries: z.array(
    z.object({
      walletId: id,
      walletName: z.string(),
      walletTypeName: z.string(),
      supportsDenominations: z.boolean(),
      expectedBalance: money,
      countedAmount: money,
      observedAt: timestamp.nullable(),
      denominations: denominationsSchema.nullable(),
    }),
  ),
});

export const countSaveSchema = z.object({
  success: z.literal(true),
  cashCountId: id,
  cashSessionId: id,
});

export const vaultSchema = z.object({
  id,
  branchId: id,
  branchName: z.string(),
  currentBalance: money,
});

export const vaultTransactionSchema = z.object({
  id,
  type: z.number().int(),
  status: z.number().int(),
  amount: money,
  balanceAfter: money.nullable(),
  createdAt: timestamp,
  createdBy: z.string(),
  approvedBy: z.string().nullable(),
  notes: z.string().nullable(),
  expiresAt: timestamp.nullable(),
});

export const vaultOperationSchema = z.object({
  success: z.literal(true),
  transactionId: id.nullable(),
});

export type SessionUser = z.infer<typeof sessionUserSchema>;
export type Login = z.infer<typeof loginSchema>;
export type Agent = z.infer<typeof agentSchema>;
export type CashCountForm = z.infer<typeof cashCountFormSchema>;
export type CurrentSession = z.infer<typeof currentSessionSchema>;
export type VaultTransaction = z.infer<typeof vaultTransactionSchema>;
