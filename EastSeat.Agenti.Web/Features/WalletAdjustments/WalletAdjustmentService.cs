using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.WalletAdjustments;

/// <summary>
/// Service for recording, approving, and querying wallet adjustments (debit-only withdrawals).
/// </summary>
public class WalletAdjustmentService(
    ApplicationDbContext dbContext,
    INotificationService notificationService,
    ISessionBalanceService balances,
    IBusinessClock businessClock,
    IBankingAccessService bankingAccess,
    TelemetryClient? telemetryClient = null) : IWalletAdjustmentService
{
    /// <inheritdoc />
    public async Task<WalletAdjustmentSaveResult> RecordAdjustmentAsync(string userId, WalletAdjustmentFormModel form)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return WalletAdjustmentSaveResult.Error("User is not configured as an agent.");
        }

        if (!agent.BranchId.HasValue)
        {
            return WalletAdjustmentSaveResult.Error("Agent is not assigned to a branch.");
        }

        var branchId = agent.BranchId.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var businessDate = businessClock.Today;
        var requestHash = BankingRequestIdentity.Hash(new
        {
            Version = 1, form.WalletId, form.Reason, form.Amount, Notes = form.Notes?.Trim(),
            OccurredAt = form.OccurredAt?.ToUniversalTime(), BankReference = form.BankReference?.Trim()
        });
        if (form.CommandId.HasValue)
        {
            var replay = await dbContext.WalletAdjustments.AsNoTracking().SingleOrDefaultAsync(
                a => a.AgentId == agent.Id && a.CommandId == form.CommandId);
            if (replay != null)
                return replay.RequestHash == requestHash
                    ? WalletAdjustmentSaveResult.Ok(replay.Id)
                    : WalletAdjustmentSaveResult.Error("This adjustment request identity was already used for different details.");
        }

        // Find today's active session
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == branchId && s.Status == CashSessionStatus.Open &&
                ((s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation && s.SessionDate == businessDate) ||
                 (s.WorkflowVersion == SessionWorkflowVersion.Legacy && s.SessionDate == today)))
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return WalletAdjustmentSaveResult.Error("No active cash session found. A session must be open to record adjustments.");
        }
        var modern = session.WorkflowVersion == SessionWorkflowVersion.BankReconciliation;
        if (modern && (!form.CommandId.HasValue || form.CommandId == Guid.Empty))
            return WalletAdjustmentSaveResult.Error("A stable request identity is required for an adjustment.");
        if (modern && (!form.OccurredAt.HasValue || form.OccurredAt > businessClock.UtcNow ||
            businessClock.GetBusinessDate(form.OccurredAt.Value) != session.SessionDate))
            return WalletAdjustmentSaveResult.Error("Enter the adjustment's non-future date and time for this session.");
        if (modern && (decimal.Round(form.Amount, 2) != form.Amount || form.Amount > 9_999_999_999_999_999.99m ||
            !Enum.IsDefined(form.Reason) || form.BankReference?.Length > 256 || form.Notes?.Length > 2000))
            return WalletAdjustmentSaveResult.Error("Check the amount, adjustment reason, reference and notes.");

        // Verify agent has an approved opening count in this session
        var hasApprovedOpening = await dbContext.CashCounts
            .AnyAsync(c => c.CashSessionId == session.Id &&
                           c.AgentId == agent.Id &&
                           c.IsOpening &&
                           c.Status == CashCountStatus.Approved);

        if (!hasApprovedOpening)
        {
            return WalletAdjustmentSaveResult.Error("Opening cash count must be approved before recording adjustments.");
        }

        // Verify no pending or approved closing count exists
        var hasClosingCount = await dbContext.CashCounts
            .AnyAsync(c => c.CashSessionId == session.Id &&
                           c.AgentId == agent.Id &&
                           !c.IsOpening &&
                           (c.Status == CashCountStatus.PendingApproval || c.Status == CashCountStatus.Approved));

        if (hasClosingCount)
        {
            return WalletAdjustmentSaveResult.Error("Cannot record adjustments after a closing count has been submitted or approved.");
        }

        // Validate amount
        if (form.Amount <= 0)
        {
            return WalletAdjustmentSaveResult.Error("Adjustment amount must be greater than zero.");
        }

        // Verify wallet belongs to this agent
        var wallet = await dbContext.Wallets
            .Include(w => w.WalletType)
            .FirstOrDefaultAsync(w => w.Id == form.WalletId && w.AgentId == agent.Id && w.IsActive);

        if (wallet == null)
        {
            return WalletAdjustmentSaveResult.Error("Wallet not found or does not belong to this agent.");
        }

        // Validate notes required for Other reason
        if (form.Reason == WalletAdjustmentReason.Other &&
            (string.IsNullOrWhiteSpace(form.Notes) || form.Notes.Trim().Length < 10))
        {
            return WalletAdjustmentSaveResult.Error("Notes are required (minimum 10 characters) when reason is 'Other'.");
        }

        // Use serializable transaction to prevent concurrent adjustments exceeding wallet balance
        long adjustmentId;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            if (modern)
            {
                var actor = await bankingAccess.GetActorAsync(userId);
                await bankingAccess.RequireTradingSessionAsync(actor, session.Id);
            }
            // Check amount doesn't exceed effective balance (wallet.Balance - existing non-rejected adjustments)
            decimal effectiveBalance;
            if (modern)
            {
                var positions = await balances.GetPositionsAsync(session.Id, agent.Id, includePendingAdjustments: true);
                effectiveBalance = positions.SingleOrDefault(p => p.WalletId == wallet.Id)?.AvailableForTransfer
                    ?? throw new BankingRuleException("The selected wallet is not part of this session's opening allocation.");
            }
            else
            {
                var existingAdjustmentTotal = await dbContext.WalletAdjustments
                    .Where(a => a.CashSessionId == session.Id && a.WalletId == form.WalletId &&
                        a.Status != WalletAdjustmentStatus.Rejected).SumAsync(a => a.Amount);
                effectiveBalance = wallet.Balance - existingAdjustmentTotal;
            }
            if (form.Amount > effectiveBalance)
            {
                await transaction.RollbackAsync();
                return WalletAdjustmentSaveResult.Error(
                    $"Adjustment amount ({form.Amount:N0}) exceeds the wallet's effective balance ({effectiveBalance:N0}).");
            }

            var adjustment = new WalletAdjustment
            {
                CashSessionId = session.Id,
                WalletId = form.WalletId,
                AgentId = agent.Id,
                Status = WalletAdjustmentStatus.Pending,
                Reason = form.Reason,
                Amount = form.Amount,
                Currency = wallet.Currency,
                Notes = form.Notes?.Trim(),
                RecordedByUserId = userId,
                CreatedAt = businessClock.UtcNow,
                OccurredAt = form.OccurredAt?.ToUniversalTime(),
                CommandId = form.CommandId,
                RequestHash = form.CommandId.HasValue ? requestHash : null,
                BankReference = form.BankReference?.Trim()
            };

            dbContext.WalletAdjustments.Add(adjustment);
            if (modern)
            {
                session.RegisterVersion++;
                session.Version++;
                await WorkflowNotifications.AddForBranchAsync(dbContext, branchId, "Wallet adjustment pending approval",
                    "A recorded wallet adjustment requires approval.", NotificationType.WalletAdjustmentRecorded,
                    "/cashcount-approvals", businessClock.UtcNow, includeSupervisors: true);
            }
            await dbContext.SaveChangesAsync();
            adjustmentId = adjustment.Id;
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            if (modern)
                dbContext.ChangeTracker.Clear();
            throw;
        }

        // Notify branch admins for approval
        var agentName = agent.User != null ? $"{agent.User.FirstName} {agent.User.LastName}".Trim() : agent.Code;
        var reasonDisplay = GetReasonDisplayText(form.Reason);

        if (!modern)
            await notificationService.NotifyBranchAdminsAsync(
                branchId,
                "Wallet Adjustment Pending Approval",
                $"{agentName} recorded a {wallet.WalletType?.Name} wallet adjustment of UGX {form.Amount:N0} ({reasonDisplay}) requiring your approval.",
                NotificationType.WalletAdjustmentRecorded,
                "/cashcount-approvals");

        telemetryClient?.TrackEvent("wallet_adjustment_recorded", new Dictionary<string, string>
        {
            { "AgentId", agent.Id.ToString() },
            { "SessionId", session.Id.ToString() },
            { "WalletId", form.WalletId.ToString() },
            { "Reason", form.Reason.ToString() },
            { "Amount", form.Amount.ToString("F2") }
        });

        return WalletAdjustmentSaveResult.Ok(adjustmentId);
    }

    /// <inheritdoc />
    public async Task<WalletAdjustmentSaveResult> ApproveAdjustmentAsync(string adminUserId, long adjustmentId)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        if (admin == null || (admin.Role != UserRole.Admin && admin.Role != UserRole.Supervisor))
        {
            return WalletAdjustmentSaveResult.Error("Only administrators or supervisors can approve wallet adjustments.");
        }

        var adjustment = await dbContext.WalletAdjustments
            .Include(a => a.CashSession)
            .Include(a => a.Agent)
                .ThenInclude(a => a!.User)
            .FirstOrDefaultAsync(a => a.Id == adjustmentId);

        if (adjustment == null)
        {
            return WalletAdjustmentSaveResult.Error("Wallet adjustment not found.");
        }
        if (adjustment.CashSession!.WorkflowVersion == SessionWorkflowVersion.BankReconciliation)
            return await ReviewModernAdjustmentAsync(adminUserId, adjustmentId, approve: true, reason: null);

        if (adjustment.Status != WalletAdjustmentStatus.Pending)
        {
            return WalletAdjustmentSaveResult.Error("Wallet adjustment is not pending approval.");
        }

        adjustment.Status = WalletAdjustmentStatus.Approved;
        adjustment.ApprovedByUserId = adminUserId;
        adjustment.ApprovedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        // Notify the agent
        if (adjustment.Agent?.UserId != null)
        {
            await notificationService.CreateSystemNotificationAsync(
                adjustment.Agent.UserId,
                "Wallet Adjustment Approved",
                $"Your wallet adjustment of UGX {adjustment.Amount:N0} has been approved.",
                NotificationType.WalletAdjustmentRecorded,
                "/cashcount");
        }

        telemetryClient?.TrackEvent("wallet_adjustment_approved", new Dictionary<string, string>
        {
            { "AdjustmentId", adjustmentId.ToString() },
            { "AdminUserId", adminUserId },
            { "Amount", adjustment.Amount.ToString("F2") }
        });

        return WalletAdjustmentSaveResult.Ok(adjustmentId);
    }

    /// <inheritdoc />
    public async Task<WalletAdjustmentSaveResult> RejectAdjustmentAsync(string adminUserId, long adjustmentId, string reason)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        if (admin == null || (admin.Role != UserRole.Admin && admin.Role != UserRole.Supervisor))
        {
            return WalletAdjustmentSaveResult.Error("Only administrators or supervisors can reject wallet adjustments.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
        {
            return WalletAdjustmentSaveResult.Error("Rejection reason must be at least 10 characters.");
        }

        var adjustment = await dbContext.WalletAdjustments
            .Include(a => a.CashSession)
            .Include(a => a.Agent)
                .ThenInclude(a => a!.User)
            .FirstOrDefaultAsync(a => a.Id == adjustmentId);

        if (adjustment == null)
        {
            return WalletAdjustmentSaveResult.Error("Wallet adjustment not found.");
        }
        if (adjustment.CashSession!.WorkflowVersion == SessionWorkflowVersion.BankReconciliation)
            return await ReviewModernAdjustmentAsync(adminUserId, adjustmentId, approve: false, reason);

        if (adjustment.Status != WalletAdjustmentStatus.Pending)
        {
            return WalletAdjustmentSaveResult.Error("Wallet adjustment is not pending approval.");
        }

        adjustment.Status = WalletAdjustmentStatus.Rejected;
        adjustment.RejectedByUserId = adminUserId;
        adjustment.RejectedAt = DateTimeOffset.UtcNow;
        adjustment.RejectionReason = reason.Trim();

        await dbContext.SaveChangesAsync();

        // Notify the agent
        if (adjustment.Agent?.UserId != null)
        {
            await notificationService.CreateSystemNotificationAsync(
                adjustment.Agent.UserId,
                "Wallet Adjustment Rejected",
                $"Your wallet adjustment of UGX {adjustment.Amount:N0} was rejected. Reason: {reason.Trim()}",
                NotificationType.WalletAdjustmentRecorded,
                "/cashcount");
        }

        return WalletAdjustmentSaveResult.Ok(adjustmentId);
    }

    private async Task<WalletAdjustmentSaveResult> ReviewModernAdjustmentAsync(string userId, long adjustmentId, bool approve, string? reason)
    {
        var actor = await bankingAccess.GetActorAsync(userId);
        if (actor.Role is not (UserRole.Admin or UserRole.Supervisor))
            throw new UnauthorizedAccessException("Opening and wallet-adjustment approval requires an administrator or supervisor.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var adjustment = await dbContext.WalletAdjustments.Include(a => a.CashSession).Include(a => a.Agent)
                .SingleAsync(a => a.Id == adjustmentId);
            await dbContext.Entry(adjustment).ReloadAsync();
            await dbContext.Entry(adjustment.CashSession!).ReloadAsync();
            if (adjustment.CashSession!.BranchId != actor.BranchId)
                throw new UnauthorizedAccessException("This adjustment belongs to a different branch.");
            if (adjustment.RecordedByUserId == actor.UserId || adjustment.Agent?.UserId == actor.UserId)
                throw new UnauthorizedAccessException("You cannot review your own wallet adjustment.");
            var target = approve ? WalletAdjustmentStatus.Approved : WalletAdjustmentStatus.Rejected;
            if (adjustment.Status == target && (approve || adjustment.RejectionReason == reason?.Trim()))
            {
                await transaction.CommitAsync();
                return WalletAdjustmentSaveResult.Ok(adjustment.Id);
            }
            if (adjustment.Status != WalletAdjustmentStatus.Pending)
                throw new BankingConflictException("This adjustment has already been reviewed. Refresh its status.");
            adjustment.Status = target;
            if (approve)
            {
                adjustment.ApprovedByUserId = actor.UserId;
                adjustment.ApprovedAt = businessClock.UtcNow;
            }
            else
            {
                adjustment.RejectedByUserId = actor.UserId;
                adjustment.RejectedAt = businessClock.UtcNow;
                adjustment.RejectionReason = reason!.Trim();
            }
            await TouchModernSessionAsync(adjustment.CashSessionId, adjustment.Id, actor.UserId);
            WorkflowNotifications.AddForUser(dbContext, adjustment.Agent!.UserId,
                approve ? "Wallet adjustment approved" : "Wallet adjustment rejected",
                approve ? "Your recorded wallet adjustment has been approved." : "Your recorded wallet adjustment was rejected; review the explanation.",
                NotificationType.WalletAdjustmentRecorded, "/cashcount", businessClock.UtcNow);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return WalletAdjustmentSaveResult.Ok(adjustment.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task TouchModernSessionAsync(long sessionId, long adjustmentId, string userId)
    {
        var session = await dbContext.CashSessions.SingleAsync(s => s.Id == sessionId);
        if (session.WorkflowVersion != SessionWorkflowVersion.BankReconciliation)
            return;
        session.RegisterVersion++;
        session.Version++;
        var reviews = await dbContext.BankReconciliationReviews.Where(r =>
                dbContext.BankReconciliationResults.Any(result => result.ReviewId == r.Id && result.CashSessionId == sessionId))
            .ToListAsync();
        foreach (var review in reviews)
        {
            review.Version++;
            if (review.Status is BankReconciliationStatus.Reconciled or BankReconciliationStatus.WithExceptions)
            {
                review.Status = BankReconciliationStatus.NeedsReview;
                dbContext.BankReconciliationDecisions.Add(new BankReconciliationDecision
                {
                    BankAccountId = review.BankAccountId, ReviewId = review.Id, CashSessionId = sessionId,
                    Kind = ReconciliationDecisionKind.ReviewInvalidated,
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { WalletAdjustmentId = adjustmentId }),
                    Reason = "A wallet adjustment decision changed the session's expected positions.",
                    PerformedByUserId = userId, PerformedAt = businessClock.UtcNow
                });
            }
        }
    }

    /// <inheritdoc />
    public async Task<List<WalletAdjustmentDto>> GetPendingAdjustmentsAsync(long branchId)
    {
        return await dbContext.WalletAdjustments
            .Include(a => a.Wallet)
                .ThenInclude(w => w!.WalletType)
            .Include(a => a.Agent)
                .ThenInclude(a => a!.User)
            .Include(a => a.CashSession)
            .Where(a => a.CashSession!.BranchId == branchId &&
                        a.Status == WalletAdjustmentStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .Select(a => MapToDto(a))
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<WalletAdjustmentDto>> GetAdjustmentsForSessionAsync(long cashSessionId, long? agentId = null)
    {
        var query = dbContext.WalletAdjustments
            .Include(a => a.Wallet)
                .ThenInclude(w => w!.WalletType)
            .Include(a => a.Agent)
                .ThenInclude(a => a!.User)
            .Where(a => a.CashSessionId == cashSessionId);

        if (agentId.HasValue)
        {
            query = query.Where(a => a.AgentId == agentId.Value);
        }

        return await query
            .OrderBy(a => a.CreatedAt)
            .Select(a => MapToDto(a))
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Dictionary<long, decimal>> GetWalletAdjustmentTotalsAsync(long cashSessionId, long agentId)
    {
        // Only approved adjustments affect expected closing balances.
        // Server-side aggregation via GroupBy + Select projection.
        return await dbContext.WalletAdjustments
            .Where(a => a.CashSessionId == cashSessionId &&
                        a.AgentId == agentId &&
                        a.Status == WalletAdjustmentStatus.Approved)
            .GroupBy(a => a.WalletId)
            .Select(g => new { WalletId = g.Key, TotalAmount = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(a => a.WalletId, a => a.TotalAmount);
    }

    /// <inheritdoc />
    public async Task<List<WalletAdjustmentDto>> GetAdjustmentsForAgentAsync(string userId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null || !agent.BranchId.HasValue)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var businessDate = businessClock.Today;
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == agent.BranchId.Value &&
                ((s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation && s.SessionDate == businessDate) ||
                 (s.WorkflowVersion == SessionWorkflowVersion.Legacy && s.SessionDate == today && s.Status == CashSessionStatus.Open)))
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return [];
        }

        return await GetAdjustmentsForSessionAsync(session.Id, agent.Id);
    }

    private async Task<Agent?> GetAgentForUserAsync(string userId)
    {
        return await dbContext.Agents
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId);
    }

    private static WalletAdjustmentDto MapToDto(WalletAdjustment a) => new()
    {
        Id = a.Id,
        WalletId = a.WalletId,
        WalletName = a.Wallet != null ? a.Wallet.Name : "Unknown",
        WalletTypeName = a.Wallet != null && a.Wallet.WalletType != null ? a.Wallet.WalletType.Name : "Unknown",
        AgentName = a.Agent != null && a.Agent.User != null
            ? $"{a.Agent.User.FirstName} {a.Agent.User.LastName}".Trim()
            : "Unknown",
        AgentCode = a.Agent != null ? a.Agent.Code : "N/A",
        Status = a.Status,
        Reason = a.Reason,
        Amount = a.Amount,
        Notes = a.Notes,
        CreatedAt = a.CreatedAt,
        RejectionReason = a.RejectionReason
    };

    private static string GetReasonDisplayText(WalletAdjustmentReason reason) => reason switch
    {
        WalletAdjustmentReason.BankShortage => "bank shortage",
        WalletAdjustmentReason.FakeNotes => "fake notes confiscated",
        WalletAdjustmentReason.OwnerPayment => "owner payment request",
        WalletAdjustmentReason.UnpaidCustomer => "unpaid customer",
        WalletAdjustmentReason.Other => "other reason",
        _ => reason.ToString()
    };
}
