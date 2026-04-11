using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.WalletAdjustments;

/// <summary>
/// Service for recording and querying wallet adjustments (debit-only withdrawals).
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

        // Check amount doesn't exceed effective balance (wallet.Balance - existing adjustments)
        var existingAdjustmentTotal = await dbContext.WalletAdjustments
            .Where(a => a.CashSessionId == session.Id && a.WalletId == form.WalletId)
            .SumAsync(a => a.Amount);

        var effectiveBalance = wallet.Balance - existingAdjustmentTotal;
        if (form.Amount > effectiveBalance)
        {
            return WalletAdjustmentSaveResult.Error(
                $"Adjustment amount ({form.Amount:N0}) exceeds the wallet's effective balance ({effectiveBalance:N0}).");
        }

        // Validate notes required for Other reason
        if (form.Reason == WalletAdjustmentReason.Other &&
            (string.IsNullOrWhiteSpace(form.Notes) || form.Notes.Trim().Length < 10))
        {
            return WalletAdjustmentSaveResult.Error("Notes are required (minimum 10 characters) when reason is 'Other'.");
        }

        var adjustment = new WalletAdjustment
        {
            CashSessionId = session.Id,
            WalletId = form.WalletId,
            AgentId = agent.Id,
            Reason = form.Reason,
            Amount = form.Amount,
            Currency = wallet.Currency,
            Notes = form.Notes?.Trim(),
            RecordedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.WalletAdjustments.Add(adjustment);
        await dbContext.SaveChangesAsync();

        // Notify branch admins
        var agentName = agent.User != null ? $"{agent.User.FirstName} {agent.User.LastName}".Trim() : agent.Code;
        var reasonDisplay = form.Reason switch
        {
            WalletAdjustmentReason.BankShortage => "bank shortage",
            WalletAdjustmentReason.FakeNotes => "fake notes confiscated",
            WalletAdjustmentReason.OwnerPayment => "owner payment request",
            WalletAdjustmentReason.UnpaidCustomer => "unpaid customer",
            WalletAdjustmentReason.Other => "other reason",
            _ => form.Reason.ToString()
        };

        await notificationService.NotifyBranchAdminsAsync(
            branchId,
            "Wallet Adjustment Recorded",
            $"{agentName} recorded a {wallet.WalletType?.Name} wallet adjustment of UGX {form.Amount:N0} ({reasonDisplay}).",
            NotificationType.WalletAdjustmentRecorded,
            "/cashsessions");

        telemetryClient?.TrackEvent("wallet_adjustment_recorded", new Dictionary<string, string>
        {
            { "AgentId", agent.Id.ToString() },
            { "SessionId", session.Id.ToString() },
            { "WalletId", form.WalletId.ToString() },
            { "Reason", form.Reason.ToString() },
            { "Amount", form.Amount.ToString("F2") }
        });

        return WalletAdjustmentSaveResult.Ok(adjustment.Id);
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
            .Select(a => new WalletAdjustmentDto
            {
                Id = a.Id,
                WalletId = a.WalletId,
                WalletName = a.Wallet != null ? a.Wallet.Name : "Unknown",
                WalletTypeName = a.Wallet != null && a.Wallet.WalletType != null ? a.Wallet.WalletType.Name : "Unknown",
                AgentName = a.Agent != null && a.Agent.User != null
                    ? (a.Agent.User.FirstName + " " + a.Agent.User.LastName).Trim()
                    : "Unknown",
                AgentCode = a.Agent != null ? a.Agent.Code : "N/A",
                Reason = a.Reason,
                Amount = a.Amount,
                Notes = a.Notes,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Dictionary<long, decimal>> GetWalletAdjustmentTotalsAsync(long cashSessionId, long agentId)
    {
        var adjustments = await dbContext.WalletAdjustments
            .Where(a => a.CashSessionId == cashSessionId && a.AgentId == agentId)
            .Select(a => new { a.WalletId, a.Amount })
            .ToListAsync();

        return adjustments
            .GroupBy(a => a.WalletId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
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
}
