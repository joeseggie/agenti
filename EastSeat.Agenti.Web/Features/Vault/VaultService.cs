using System.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace EastSeat.Agenti.Web.Features.Vaults;

/// <summary>
/// Service implementation for vault operations with pessimistic locking.
/// </summary>
public class VaultService(ApplicationDbContext dbContext) : IVaultService
{
    private const int PendingExpiryHours = 12;

    public async Task<VaultDto?> GetVaultAsync(long branchId, CancellationToken cancellationToken = default)
    {
        var vault = await dbContext.Vaults
            .Include(v => v.Branch)
            .FirstOrDefaultAsync(v => v.BranchId == branchId, cancellationToken);

        if (vault == null) return null;

        return new VaultDto
        {
            Id = vault.Id,
            BranchId = vault.BranchId,
            BranchName = vault.Branch?.Name ?? "Unknown",
            CurrentBalance = vault.CurrentBalance
        };
    }

    public async Task<List<VaultTransactionListItemDto>> GetRecentTransactionsAsync(long branchId, int take = 50, bool includeExpired = false, CancellationToken cancellationToken = default)
    {
        var vault = await dbContext.Vaults.FirstOrDefaultAsync(v => v.BranchId == branchId, cancellationToken);
        if (vault == null)
        {
            return [];
        }

        var query = dbContext.VaultTransactions
            .Include(t => t.CreatedByUser)
            .Include(t => t.ApprovedByUser)
            .Where(t => t.VaultId == vault.Id)
            .OrderByDescending(t => t.CreatedAt)
            .AsQueryable();

        if (!includeExpired)
        {
            query = query.Where(t => t.Status != VaultTransactionStatus.Expired);
        }

        return await query
            .Take(take)
            .Select(t => new VaultTransactionListItemDto
            {
                Id = t.Id,
                Type = t.Type,
                Status = t.Status,
                Amount = t.Amount,
                BalanceAfter = t.BalanceAfter,
                CreatedAt = t.CreatedAt,
                ApprovedAt = t.ApprovedAt,
                CreatedBy = t.CreatedByUser != null ? t.CreatedByUser.FullName : string.Empty,
                ApprovedBy = t.ApprovedByUser != null ? t.ApprovedByUser.FullName : null,
                Notes = t.Notes,
                CashSessionId = t.CashSessionId,
                ExpiresAt = t.ExpiresAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<VaultOperationResult> WithdrawForSessionAsync(long sessionId, long branchId, decimal amount, string userId, bool ensureTransaction = true, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return VaultOperationResult.Error("Amount must be greater than zero.");
        }

        if (ensureTransaction)
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await WithdrawInternalAsync(sessionId, branchId, amount, userId, cancellationToken);
            if (result.Success)
            {
                await tx.CommitAsync(cancellationToken);
            }
            return result;
        }

        return await WithdrawInternalAsync(sessionId, branchId, amount, userId, cancellationToken);
    }

    public async Task<VaultOperationResult> DepositForSessionAsync(long sessionId, long branchId, decimal amount, string userId, bool ensureTransaction = true, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return VaultOperationResult.Error("Amount must be greater than zero.");
        }

        if (ensureTransaction)
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await DepositInternalAsync(sessionId, branchId, amount, userId, cancellationToken);
            if (result.Success)
            {
                await tx.CommitAsync(cancellationToken);
            }
            return result;
        }

        return await DepositInternalAsync(sessionId, branchId, amount, userId, cancellationToken);
    }

    public async Task<VaultOperationResult> RequestManualAdjustmentAsync(long branchId, decimal amount, bool isDeposit, string notes, string userId, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return VaultOperationResult.Error("Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length < 10)
        {
            return VaultOperationResult.Error("Notes must be provided (minimum 10 characters) for audit.");
        }

        var branchExists = await dbContext.Branches.AnyAsync(b => b.Id == branchId, cancellationToken);
        if (!branchExists)
        {
            return VaultOperationResult.Error("Branch not found.");
        }

        var vault = await EnsureVaultExistsAsync(branchId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var transaction = new VaultTransaction
        {
            VaultId = vault.Id,
            Amount = amount,
            Type = isDeposit ? VaultTransactionType.ManualDeposit : VaultTransactionType.ManualWithdrawal,
            Status = VaultTransactionStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddHours(PendingExpiryHours),
            CreatedByUserId = userId,
            Notes = notes.Trim()
        };

        dbContext.VaultTransactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);

        return VaultOperationResult.Ok(transaction.Id);
    }

    public async Task<VaultOperationResult> ApproveManualAdjustmentAsync(long transactionId, string adminUserId, CancellationToken cancellationToken = default)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId, cancellationToken);
        if (admin == null || admin.Role != UserRole.Admin)
        {
            return VaultOperationResult.Error("Only administrators can approve vault adjustments.");
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var transaction = await dbContext.VaultTransactions
            .Include(t => t.Vault)
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

        if (transaction == null)
        {
            return VaultOperationResult.Error("Transaction not found.");
        }

        if (transaction.Status != VaultTransactionStatus.Pending)
        {
            return VaultOperationResult.Error("Transaction is not pending.");
        }

        if (transaction.CreatedByUserId == adminUserId)
        {
            return VaultOperationResult.Error("Creator cannot approve their own transaction.");
        }

        if (transaction.ExpiresAt.HasValue && transaction.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            transaction.Status = VaultTransactionStatus.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return VaultOperationResult.Error("Transaction has expired.");
        }

        if (transaction.Vault == null)
        {
            await tx.CommitAsync(cancellationToken);
            return VaultOperationResult.Error("Vault not found for transaction.");
        }

        // Lock the vault row
        var vault = await dbContext.Vaults
            .FromSqlRaw("SELECT * FROM \"Vaults\" WHERE \"Id\" = {0} FOR UPDATE", transaction.Vault.Id)
            .FirstAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (transaction.Type == VaultTransactionType.ManualWithdrawal || transaction.Type == VaultTransactionType.Adjustment && transaction.Amount > 0)
        {
            if (vault.CurrentBalance < transaction.Amount)
            {
                return VaultOperationResult.Error("Insufficient vault balance to approve withdrawal.");
            }
            vault.CurrentBalance -= transaction.Amount;
        }
        else
        {
            vault.CurrentBalance += transaction.Amount;
        }

        vault.UpdatedAt = now;
        transaction.Status = VaultTransactionStatus.Completed;
        transaction.ApprovedByUserId = adminUserId;
        transaction.ApprovedAt = now;
        transaction.BalanceAfter = vault.CurrentBalance;

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return VaultOperationResult.Ok(transaction.Id);
    }

    public async Task<VaultOperationResult> RejectManualAdjustmentAsync(long transactionId, string adminUserId, CancellationToken cancellationToken = default)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId, cancellationToken);
        if (admin == null || admin.Role != UserRole.Admin)
        {
            return VaultOperationResult.Error("Only administrators can reject vault adjustments.");
        }

        var transaction = await dbContext.VaultTransactions.FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);
        if (transaction == null)
        {
            return VaultOperationResult.Error("Transaction not found.");
        }

        if (transaction.Status != VaultTransactionStatus.Pending)
        {
            return VaultOperationResult.Error("Transaction is not pending.");
        }

        transaction.Status = VaultTransactionStatus.Rejected;
        transaction.ApprovedByUserId = adminUserId;
        transaction.ApprovedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return VaultOperationResult.Ok(transaction.Id);
    }

    public async Task<int> ExpirePendingTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await dbContext.VaultTransactions
            .Where(t => t.Status == VaultTransactionStatus.Pending && t.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (!expired.Any())
        {
            return 0;
        }

        foreach (var transaction in expired)
        {
            transaction.Status = VaultTransactionStatus.Expired;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<VaultOperationResult> WithdrawInternalAsync(long sessionId, long branchId, decimal amount, string userId, CancellationToken cancellationToken)
    {
        var vault = await LockVaultAsync(branchId, cancellationToken);
        if (vault == null)
        {
            return VaultOperationResult.Error("Vault not found for branch.");
        }

        if (vault.CurrentBalance < amount)
        {
            return VaultOperationResult.Error("Insufficient balance in vault.");
        }

        vault.CurrentBalance -= amount;
        vault.UpdatedAt = DateTimeOffset.UtcNow;

        var transaction = new VaultTransaction
        {
            VaultId = vault.Id,
            CashSessionId = sessionId,
            Amount = amount,
            Type = VaultTransactionType.Opening,
            Status = VaultTransactionStatus.Completed,
            BalanceAfter = vault.CurrentBalance,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            Notes = "Opening cash withdrawal"
        };

        dbContext.VaultTransactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);

        return VaultOperationResult.Ok(transaction.Id);
    }

    private async Task<VaultOperationResult> DepositInternalAsync(long sessionId, long branchId, decimal amount, string userId, CancellationToken cancellationToken)
    {
        var vault = await LockVaultAsync(branchId, cancellationToken);
        if (vault == null)
        {
            return VaultOperationResult.Error("Vault not found for branch.");
        }

        vault.CurrentBalance += amount;
        vault.UpdatedAt = DateTimeOffset.UtcNow;

        var transaction = new VaultTransaction
        {
            VaultId = vault.Id,
            CashSessionId = sessionId,
            Amount = amount,
            Type = VaultTransactionType.Closing,
            Status = VaultTransactionStatus.Completed,
            BalanceAfter = vault.CurrentBalance,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            Notes = "Closing cash deposit"
        };

        dbContext.VaultTransactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);

        return VaultOperationResult.Ok(transaction.Id);
    }

    private async Task<Vault> EnsureVaultExistsAsync(long branchId, CancellationToken cancellationToken)
    {
        var vault = await dbContext.Vaults.FirstOrDefaultAsync(v => v.BranchId == branchId, cancellationToken);
        if (vault != null) return vault;

        vault = new Vault
        {
            BranchId = branchId,
            CurrentBalance = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Vaults.Add(vault);
        await dbContext.SaveChangesAsync(cancellationToken);

        return vault;
    }

    private async Task<Vault?> LockVaultAsync(long branchId, CancellationToken cancellationToken)
    {
        var branchExists = await dbContext.Branches.AnyAsync(b => b.Id == branchId, cancellationToken);
        if (!branchExists)
        {
            return null;
        }

        var vault = await dbContext.Vaults
            .FromSqlRaw("SELECT * FROM \"Vaults\" WHERE \"BranchId\" = {0} FOR UPDATE", branchId)
            .FirstOrDefaultAsync(cancellationToken);

        if (vault != null)
        {
            return vault;
        }

        vault = new Vault
        {
            BranchId = branchId,
            CurrentBalance = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Vaults.Add(vault);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await dbContext.Vaults
            .FromSqlRaw("SELECT * FROM \"Vaults\" WHERE \"BranchId\" = {0} FOR UPDATE", branchId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
