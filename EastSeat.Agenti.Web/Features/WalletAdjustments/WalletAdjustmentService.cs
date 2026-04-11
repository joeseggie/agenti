using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
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

        // Find today's active session
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == branchId && s.SessionDate == today && s.Status == CashSessionStatus.Open)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return WalletAdjustmentSaveResult.Error("No active cash session found. A session must be open to record adjustments.");
        }

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
            // Check amount doesn't exceed effective balance (wallet.Balance - existing non-rejected adjustments)
            var existingAdjustmentTotal = await dbContext.WalletAdjustments
                .Where(a => a.CashSessionId == session.Id &&
                            a.WalletId == form.WalletId &&
                            a.Status != WalletAdjustmentStatus.Rejected)
                .SumAsync(a => a.Amount);

            var effectiveBalance = wallet.Balance - existingAdjustmentTotal;
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
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.WalletAdjustments.Add(adjustment);
            await dbContext.SaveChangesAsync();
            adjustmentId = adjustment.Id;
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        // Notify branch admins for approval
        var agentName = agent.User != null ? $"{agent.User.FirstName} {agent.User.LastName}".Trim() : agent.Code;
        var reasonDisplay = GetReasonDisplayText(form.Reason);

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
            .Include(a => a.Agent)
                .ThenInclude(a => a!.User)
            .FirstOrDefaultAsync(a => a.Id == adjustmentId);

        if (adjustment == null)
        {
            return WalletAdjustmentSaveResult.Error("Wallet adjustment not found.");
        }

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
            .Include(a => a.Agent)
                .ThenInclude(a => a!.User)
            .FirstOrDefaultAsync(a => a.Id == adjustmentId);

        if (adjustment == null)
        {
            return WalletAdjustmentSaveResult.Error("Wallet adjustment not found.");
        }

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
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == agent.BranchId.Value && s.SessionDate == today && s.Status == CashSessionStatus.Open)
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
