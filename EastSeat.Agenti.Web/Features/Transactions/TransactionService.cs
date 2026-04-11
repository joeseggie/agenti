using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Transactions;

/// <summary>
/// Service for querying agent transactions and managing erroneous transaction flags.
/// </summary>
public class TransactionService(
    ApplicationDbContext dbContext,
    INotificationService notificationService,
    TelemetryClient? telemetryClient = null) : ITransactionService
{
    /// <inheritdoc />
    public async Task<List<TransactionListItemDto>> GetTransactionsForAgentAsync(string userId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null || !agent.BranchId.HasValue)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == agent.BranchId.Value &&
                        s.SessionDate == today &&
                        s.Status == CashSessionStatus.Open)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return [];
        }

        return await GetTransactionsForSessionAsync(session.Id);
    }

    /// <inheritdoc />
    public async Task<List<TransactionListItemDto>> GetTransactionsForSessionAsync(long cashSessionId)
    {
        var transactions = await dbContext.Transactions
            .Include(t => t.FromWallet)
            .Include(t => t.ToWallet)
            .Where(t => t.CashSessionId == cashSessionId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        if (transactions.Count == 0)
        {
            return [];
        }

        var transactionIds = transactions.Select(t => t.Id).ToList();

        // Load active flags for these transactions in a single query
        var activeFlags = await dbContext.TransactionFlags
            .Where(f => transactionIds.Contains(f.TransactionId) &&
                        f.Status != TransactionFlagStatus.Dismissed &&
                        f.Status != TransactionFlagStatus.Resolved)
            .Select(f => new { f.TransactionId, f.Status })
            .ToListAsync();

        var flagLookup = activeFlags
            .GroupBy(f => f.TransactionId)
            .ToDictionary(g => g.Key, g => g.First().Status);

        return transactions.Select(t => new TransactionListItemDto
        {
            Id = t.Id,
            CashSessionId = t.CashSessionId,
            Type = t.Type,
            Amount = t.Amount,
            Currency = t.Currency,
            Reference = t.Reference,
            Notes = t.Notes,
            FromWalletName = t.FromWallet?.Name ?? "Unknown",
            ToWalletName = t.ToWallet?.Name ?? "Unknown",
            CreatedAt = t.CreatedAt,
            IsReversed = t.ReversedAt.HasValue,
            IsFlagged = flagLookup.ContainsKey(t.Id),
            FlagStatus = flagLookup.TryGetValue(t.Id, out var status) ? status : null
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<TransactionFlagResult> FlagTransactionAsync(string userId, FlagTransactionFormModel form)
    {
        if (string.IsNullOrWhiteSpace(form.Reason) || form.Reason.Trim().Length < 10)
        {
            return TransactionFlagResult.Error("Reason must be at least 10 characters.");
        }

        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return TransactionFlagResult.Error("User is not configured as an agent.");
        }

        if (!agent.BranchId.HasValue)
        {
            return TransactionFlagResult.Error("Agent is not assigned to a branch.");
        }

        // Load the transaction and verify it belongs to this agent's session
        var transaction = await dbContext.Transactions
            .Include(t => t.FromWallet)
            .Include(t => t.ToWallet)
            .Include(t => t.CashSession)
            .FirstOrDefaultAsync(t => t.Id == form.TransactionId);

        if (transaction == null)
        {
            return TransactionFlagResult.Error("Transaction not found.");
        }

        // Verify the transaction belongs to this agent's branch
        if (transaction.CashSession?.BranchId != agent.BranchId.Value)
        {
            return TransactionFlagResult.Error("Transaction does not belong to this agent's branch.");
        }

        // Verify the transaction is for the agent's wallets
        var agentWalletIds = await dbContext.Wallets
            .Where(w => w.AgentId == agent.Id)
            .Select(w => w.Id)
            .ToListAsync();

        if (!agentWalletIds.Contains(transaction.FromWalletId) &&
            !agentWalletIds.Contains(transaction.ToWalletId))
        {
            return TransactionFlagResult.Error("Transaction does not belong to this agent.");
        }

        // Check for an existing active flag on this transaction
        var existingActiveFlag = await dbContext.TransactionFlags
            .AnyAsync(f => f.TransactionId == form.TransactionId &&
                           f.Status != TransactionFlagStatus.Dismissed &&
                           f.Status != TransactionFlagStatus.Resolved);

        if (existingActiveFlag)
        {
            return TransactionFlagResult.Error("This transaction already has an active flag pending review.");
        }

        var flag = new TransactionFlag
        {
            TransactionId = form.TransactionId,
            FlaggedByUserId = userId,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = form.Reason.Trim(),
            Status = TransactionFlagStatus.PendingReview
        };

        dbContext.TransactionFlags.Add(flag);
        await dbContext.SaveChangesAsync();

        // Notify branch admins/supervisors
        var agentName = agent.User != null
            ? $"{agent.User.FirstName} {agent.User.LastName}".Trim()
            : agent.Code;

        await notificationService.NotifyBranchAdminsAsync(
            agent.BranchId.Value,
            "Erroneous Transaction Flagged",
            $"{agentName} flagged a transaction of {transaction.Currency} {transaction.Amount:N0} as erroneous and requiring investigation.",
            NotificationType.TransactionFlagged,
            "/transaction-flags");

        telemetryClient?.TrackEvent("transaction_flagged", new Dictionary<string, string>
        {
            { "AgentId", agent.Id.ToString() },
            { "TransactionId", form.TransactionId.ToString() },
            { "FlagId", flag.Id.ToString() }
        });

        return TransactionFlagResult.Ok(flag.Id);
    }

    /// <inheritdoc />
    public async Task<TransactionFlagResult> StartInvestigationAsync(string adminUserId, long flagId)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        if (admin == null || (admin.Role != UserRole.Admin && admin.Role != UserRole.Supervisor))
        {
            return TransactionFlagResult.Error("Only administrators or supervisors can start an investigation.");
        }

        var flag = await dbContext.TransactionFlags
            .Include(f => f.Transaction)
            .FirstOrDefaultAsync(f => f.Id == flagId);

        if (flag == null)
        {
            return TransactionFlagResult.Error("Transaction flag not found.");
        }

        if (flag.Status != TransactionFlagStatus.PendingReview)
        {
            return TransactionFlagResult.Error("Only flags with 'Pending Review' status can be moved to investigation.");
        }

        flag.Status = TransactionFlagStatus.UnderInvestigation;
        await dbContext.SaveChangesAsync();

        // Notify the agent who raised the flag
        await notificationService.CreateSystemNotificationAsync(
            flag.FlaggedByUserId,
            "Transaction Flag Under Investigation",
            $"Your transaction flag is now under investigation by a supervisor.",
            NotificationType.TransactionFlagged,
            "/cashcount");

        telemetryClient?.TrackEvent("transaction_flag_investigation_started", new Dictionary<string, string>
        {
            { "FlagId", flagId.ToString() },
            { "AdminUserId", adminUserId }
        });

        return TransactionFlagResult.Ok(flagId);
    }

    /// <inheritdoc />
    public async Task<TransactionFlagResult> ResolveFlagAsync(string adminUserId, long flagId, string notes)
    {
        return await UpdateFlagStatusAsync(adminUserId, flagId, notes, TransactionFlagStatus.Resolved, "resolved");
    }

    /// <inheritdoc />
    public async Task<TransactionFlagResult> DismissFlagAsync(string adminUserId, long flagId, string notes)
    {
        return await UpdateFlagStatusAsync(adminUserId, flagId, notes, TransactionFlagStatus.Dismissed, "dismissed");
    }

    /// <inheritdoc />
    public async Task<List<TransactionFlagDto>> GetActiveFlagsForBranchAsync(long branchId)
    {
        return await QueryFlagsForBranchAsync(branchId, active: true, statusFilter: null);
    }

    /// <inheritdoc />
    public async Task<List<TransactionFlagDto>> GetAllFlagsForBranchAsync(long branchId, string? statusFilter = null)
    {
        return await QueryFlagsForBranchAsync(branchId, active: false, statusFilter: statusFilter);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<TransactionFlagResult> UpdateFlagStatusAsync(
        string adminUserId,
        long flagId,
        string notes,
        TransactionFlagStatus newStatus,
        string actionVerb)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        if (admin == null || (admin.Role != UserRole.Admin && admin.Role != UserRole.Supervisor))
        {
            return TransactionFlagResult.Error($"Only administrators or supervisors can {actionVerb} a flag.");
        }

        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length < 10)
        {
            return TransactionFlagResult.Error($"Notes are required (minimum 10 characters) when {actionVerb} a flag.");
        }

        var flag = await dbContext.TransactionFlags
            .Include(f => f.Transaction)
            .FirstOrDefaultAsync(f => f.Id == flagId);

        if (flag == null)
        {
            return TransactionFlagResult.Error("Transaction flag not found.");
        }

        if (flag.Status == TransactionFlagStatus.Resolved || flag.Status == TransactionFlagStatus.Dismissed)
        {
            return TransactionFlagResult.Error("Flag has already been resolved or dismissed.");
        }

        flag.Status = newStatus;
        flag.ResolvedByUserId = adminUserId;
        flag.ResolvedAt = DateTimeOffset.UtcNow;
        flag.InvestigationNotes = notes.Trim();

        await dbContext.SaveChangesAsync();

        var statusDisplay = newStatus == TransactionFlagStatus.Resolved ? "resolved" : "dismissed";
        await notificationService.CreateSystemNotificationAsync(
            flag.FlaggedByUserId,
            $"Transaction Flag {char.ToUpper(statusDisplay[0]) + statusDisplay[1..]}",
            $"Your transaction flag has been {statusDisplay}. Notes: {notes.Trim()}",
            NotificationType.TransactionFlagged,
            "/cashcount");

        telemetryClient?.TrackEvent($"transaction_flag_{statusDisplay}", new Dictionary<string, string>
        {
            { "FlagId", flagId.ToString() },
            { "AdminUserId", adminUserId }
        });

        return TransactionFlagResult.Ok(flagId);
    }

    private async Task<List<TransactionFlagDto>> QueryFlagsForBranchAsync(
        long branchId,
        bool active,
        string? statusFilter)
    {
        var query = dbContext.TransactionFlags
            .Include(f => f.Transaction)
                .ThenInclude(t => t!.FromWallet)
            .Include(f => f.Transaction)
                .ThenInclude(t => t!.ToWallet)
            .Include(f => f.Transaction)
                .ThenInclude(t => t!.CashSession)
            .Include(f => f.FlaggedByUser)
                .ThenInclude(u => u!.Agent)
            .Include(f => f.ResolvedByUser)
            .Where(f => f.Transaction != null &&
                        f.Transaction.CashSession != null &&
                        f.Transaction.CashSession.BranchId == branchId)
            .AsQueryable();

        if (active)
        {
            query = query.Where(f => f.Status == TransactionFlagStatus.PendingReview ||
                                     f.Status == TransactionFlagStatus.UnderInvestigation);
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<TransactionFlagStatus>(statusFilter, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(f => f.Status == parsedStatus);
        }

        return await query
            .OrderByDescending(f => f.FlaggedAt)
            .Select(f => MapToDto(f))
            .ToListAsync();
    }

    private async Task<Agent?> GetAgentForUserAsync(string userId)
    {
        return await dbContext.Agents
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId);
    }

    private static TransactionFlagDto MapToDto(TransactionFlag f) => new()
    {
        Id = f.Id,
        TransactionId = f.TransactionId,
        TransactionType = f.Transaction?.Type ?? TransactionType.Transfer,
        TransactionAmount = f.Transaction?.Amount ?? 0m,
        TransactionCurrency = f.Transaction?.Currency ?? "UGX",
        TransactionReference = f.Transaction?.Reference,
        FromWalletName = f.Transaction?.FromWallet?.Name ?? "Unknown",
        ToWalletName = f.Transaction?.ToWallet?.Name ?? "Unknown",
        TransactionCreatedAt = f.Transaction?.CreatedAt ?? f.FlaggedAt,
        FlaggedByAgentName = f.FlaggedByUser != null
            ? $"{f.FlaggedByUser.FirstName} {f.FlaggedByUser.LastName}".Trim()
            : "Unknown",
        FlaggedByAgentCode = f.FlaggedByUser?.Agent?.Code ?? "N/A",
        FlaggedAt = f.FlaggedAt,
        Reason = f.Reason,
        Status = f.Status,
        InvestigationNotes = f.InvestigationNotes,
        ResolvedByName = f.ResolvedByUser != null
            ? $"{f.ResolvedByUser.FirstName} {f.ResolvedByUser.LastName}".Trim()
            : null,
        ResolvedAt = f.ResolvedAt
    };
}
