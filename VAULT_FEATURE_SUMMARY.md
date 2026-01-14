# Vault Feature Implementation Summary

## Overview
Successfully implemented a comprehensive **Vault feature** for EastSeat Agenti that manages centralized cash vault for each branch with full audit trails, dual-approval workflow, and pessimistic locking for concurrent access safety.

## Key Features Implemented

### 1. **Domain Model**
- **Branch Entity**: Represents tenant branches (1:1 relationship with Vault)
- **Vault Entity**: Central cash repository per branch with current balance tracking
- **VaultTransaction Entity**: Immutable audit log of all vault movements with:
  - Transaction Type: Opening, Closing, ManualDeposit, ManualWithdrawal, Adjustment
  - Status: Completed, Pending, Rejected, Expired
  - Full audit fields: CreatedByUserId, ApprovedByUserId, timestamps
  - 12-hour expiry for pending manual adjustments

### 2. **Vault Service (IVaultService / VaultService)**
Pessimistic locking implementation using PostgreSQL `FOR UPDATE`:
- `GetVaultAsync(branchId)` - Retrieve vault with branch info
- `GetRecentTransactionsAsync(branchId, take, includeExpired)` - Paginated history
- `WithdrawForSessionAsync(sessionId, branchId, amount, userId)` - Deduct on opening
- `DepositForSessionAsync(sessionId, branchId, amount, userId)` - Return on closing
- `RequestManualAdjustmentAsync(...)` - Submit pending adjustment (Admins must approve)
- `ApproveManualAdjustmentAsync(transactionId, adminUserId)` - Admin approval (creator ≠ approver)
- `RejectManualAdjustmentAsync(transactionId, adminUserId)` - Admin rejection
- `ExpirePendingTransactionsAsync()` - Background expiration (12h timeout)

**Concurrency Safety**: Serializable isolation level + row-level `FOR UPDATE` locks

### 3. **Cash Session Integration**
Updated `CashCountService.SubmitCashCountAsync()` to:
- **Opening Count**: Withdraw total from vault → update wallet balances → zero out after session
- **Closing Count**: Return total to vault → zero wallet balances → close session
- Prevents fraud: Cannot withdraw more than vault balance

### 4. **Background Service**
`VaultExpirationService` (IHostedService):
- Runs every 5 minutes
- Marks pending vault transactions as Expired if `ExpiresAt <= Now`
- 12-hour expiration window for manual adjustments

### 5. **UI Components**

#### Vault.razor (`/vault`)
- **Authorization**: Policy="VaultView" (Admin, Supervisor)
- Displays current vault balance
- **Pending Approvals section** (Admin only via Policy="VaultApprove"):
  - Lists pending manual adjustments
  - Shows time remaining before expiry
  - Approve/Reject buttons with atomic balance updates
- **Transaction History**:
  - Shows all completed, pending, rejected, expired transactions
  - Color-coded by type and status
  - Running balance display
  - User attribution

#### VaultAdjustment.razor (`/vault-adjustment`)
- **Authorization**: Policy="VaultAdjust" (Admin, Supervisor)
- Form to request manual deposit/withdrawal
- Validation: Amount > 0, Notes ≥ 10 characters (audit requirement)
- Creates pending transaction requiring admin approval

#### NavMenu.razor
- Added "Vault" link inside nested `<AuthorizeView Policy="VaultView">` block
- Only visible to authorized users (Admin, Supervisor)

### 6. **Authorization Policies** (Program.cs)
```csharp
.AddPolicy("VaultView", policy => policy.RequireRole(UserRole.Admin, UserRole.Supervisor))
.AddPolicy("VaultAdjust", policy => policy.RequireRole(UserRole.Admin, UserRole.Supervisor))
.AddPolicy("VaultApprove", policy => policy.RequireRole(UserRole.Admin))
```

### 7. **Database Schema**

#### Branches Table
- `Id` (PK)
- `Name` (string, 256)
- `CreatedAt`, `UpdatedAt`

#### Vaults Table
- `Id` (PK)
- `BranchId` (FK, unique) - One vault per branch
- `CurrentBalance` (decimal 18,2)
- `CreatedAt`, `UpdatedAt`

#### VaultTransactions Table
- `Id` (PK)
- `VaultId` (FK, cascade delete)
- `CashSessionId` (FK, optional, restrict delete)
- `Amount`, `BalanceAfter` (decimal 18,2)
- `Type`, `Status` (enum as string, 50 chars)
- `CreatedAt`, `CreatedByUserId` (FK)
- `ApprovedAt`, `ApprovedByUserId` (FK, optional)
- `ExpiresAt` (timestamp, nullable for permanent transactions)
- `Notes` (string, 1000, for audit trail)
- **Indexes**: BranchId, VaultId, (VaultId, CreatedAt), (Status, ExpiresAt)

### 8. **Service Registration** (Program.cs)
```csharp
builder.Services.AddScoped<IVaultService, VaultService>();
builder.Services.AddHostedService<VaultExpirationService>();
```

### 9. **Migration**
- Created EF Core migration: `20260112091745_AddVaultFeature`
- Applied to database successfully
- All FK constraints and indexes configured

## Fraud Prevention Controls

| Control | Implementation |
|---------|----------------|
| **Dual Approval** | `CreatedByUserId ≠ ApprovedByUserId` enforced in service |
| **Admin-Only Approval** | `RequireRole(UserRole.Admin)` policy on approve endpoints |
| **Automatic Expiry** | Pending transactions expire after 12h via `ExpiresAt` field |
| **Full Audit Trail** | Every transaction logs: user, timestamp, notes, before/after balance |
| **Mandatory Justification** | Notes field required (min 10 chars) for manual adjustments |
| **Concurrent Access Safety** | PostgreSQL row-level locks + Serializable isolation level |
| **Immutable History** | No update/delete operations on VaultTransaction records |
| **Balance Enforcement** | Cannot withdraw more than current balance |
| **Session Isolation** | Wallets zeroed after session close; only non-zero during active session |

## Data Flow

### Opening a Cash Session
1. Agent submits opening cash count → total amount calculated
2. `CashCountService` calls `VaultService.WithdrawForSessionAsync()`
3. Vault acquires pessimistic lock (`FOR UPDATE`)
4. Check: Vault balance ≥ amount requested
5. Deduct from vault, update wallet balances
6. Create `VaultTransaction` (Type=Opening, Status=Completed)
7. Wallets now hold the cash for the session

### Closing a Cash Session
1. Agent submits closing cash count → total amount calculated
2. `CashCountService` calls `VaultService.DepositForSessionAsync()`
3. Vault acquires pessimistic lock
4. Add back to vault
5. Zero out wallet balances (no cash outside vault without active session)
6. Create `VaultTransaction` (Type=Closing, Status=Completed)
7. Session marked closed

### Manual Adjustment (Admin Workflow)
1. Supervisor requests deposit/withdrawal with notes
2. Creates pending transaction (Status=Pending, ExpiresAt=Now+12h)
3. Admin views pending approvals in Vault.razor
4. Admin approves:
   - Validates: Creator ≠ Approver, request not expired
   - Acquires lock, applies balance change
   - Updates: ApprovedByUserId, ApprovedAt, Status=Completed, BalanceAfter
5. Or Admin rejects → Status=Rejected

### Background Expiration
1. `VaultExpirationService` runs every 5 minutes
2. Queries: `Status=Pending AND ExpiresAt <= Now`
3. Updates all matched to `Status=Expired`
4. Logs count of expired transactions

## Testing Recommendations

1. **Vault Balance Constraint**: Open session > request amount → should succeed; < should fail
2. **Dual Approval**: Request as User A, approve as User A → should fail; as User B → succeed
3. **Concurrent Opening**: Two agents open sessions simultaneously → only one succeeds (lock prevents race)
4. **12-Hour Expiry**: Submit adjustment, wait 12h, auto-expire occurs
5. **Wallet Lifecycle**: Check wallets zero after session close, refill on new session open
6. **Audit Trail**: Verify all transactions logged with user, timestamp, balance after
7. **Authorization**: Non-Admin cannot access `/vault-adjustment`, non-Supervisor cannot see Vault menu

## Files Modified/Created

### Created
- [Shared/Domain/Entities/Branch.cs](EastSeat.Agenti.Web/Shared/Domain/Entities/Branch.cs)
- [Shared/Domain/Entities/Vault.cs](EastSeat.Agenti.Web/Shared/Domain/Entities/Vault.cs)
- [Shared/Domain/Entities/VaultTransaction.cs](EastSeat.Agenti.Web/Shared/Domain/Entities/VaultTransaction.cs)
- [Shared/Domain/Enums/VaultTransactionType.cs](EastSeat.Agenti.Web/Shared/Domain/Enums/VaultTransactionType.cs)
- [Shared/Domain/Enums/VaultTransactionStatus.cs](EastSeat.Agenti.Web/Shared/Domain/Enums/VaultTransactionStatus.cs)
- [Features/Vaults/VaultDtos.cs](EastSeat.Agenti.Web/Features/Vaults/VaultDtos.cs)
- [Features/Vaults/IVaultService.cs](EastSeat.Agenti.Web/Features/Vaults/IVaultService.cs)
- [Features/Vaults/VaultService.cs](EastSeat.Agenti.Web/Features/Vaults/VaultService.cs)
- [Features/Vaults/VaultExpirationService.cs](EastSeat.Agenti.Web/Features/Vaults/VaultExpirationService.cs)
- [Components/Pages/Vault.razor](EastSeat.Agenti.Web/Components/Pages/Vault.razor)
- [Components/Pages/VaultAdjustment.razor](EastSeat.Agenti.Web/Components/Pages/VaultAdjustment.razor)
- EF Core Migration: `20260112091745_AddVaultFeature`

### Modified
- [Data/ApplicationDbContext.cs](EastSeat.Agenti.Web/Data/ApplicationDbContext.cs) - Added DbSets, configurations, auto-vault-creation
- [Features/CashCounts/CashCountService.cs](EastSeat.Agenti.Web/Features/CashCounts/CashCountService.cs) - Integrated vault withdrawals/deposits
- [Program.cs](EastSeat.Agenti.Web/Program.cs) - Registered services, policies, background service
- [Components/Layout/NavMenu.razor](EastSeat.Agenti.Web/Components/Layout/NavMenu.razor) - Added Vault menu link

## Namespace Note
Feature namespace changed from `EastSeat.Agenti.Web.Features.Vault` to `EastSeat.Agenti.Web.Features.Vaults` to avoid naming collision with `Vault` entity class.
