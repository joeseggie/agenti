using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EastSeat.Agenti.Web.Features.BankRuns;

/// <summary>
/// Service for recording and querying bank runs (physical bank deposits).
/// </summary>
public class BankRunService(
    ApplicationDbContext dbContext,
    INotificationService notificationService,
    ISessionBalanceService balances,
    IBusinessClock businessClock,
    IBankingAccessService bankingAccess,
    TelemetryClient? telemetryClient = null) : IBankRunService
{
    /// <inheritdoc />
    public async Task<BankRunSaveResult> RecordBankRunAsync(string userId, BankRunFormModel model)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return BankRunSaveResult.Error("User is not configured as an agent.");
        }

        if (!agent.BranchId.HasValue)
        {
            return BankRunSaveResult.Error("Agent is not assigned to a branch.");
        }

        var branchId = agent.BranchId.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var businessDate = businessClock.Today;
        var requestHash = BankingRequestIdentity.Hash(new
        {
            Version = 1, model.ToWalletTypeId, model.Amount, ReceiptNumber = model.ReceiptNumber?.Trim(),
            Notes = model.Notes?.Trim(), OccurredAt = model.OccurredAt?.ToUniversalTime(),
            ImageHash = model.ReceiptImage == null ? null : Convert.ToHexString(SHA256.HashData(model.ReceiptImage)),
            model.ReceiptImageContentType
        });
        if (model.CommandId.HasValue)
        {
            var replay = await dbContext.BankRuns.AsNoTracking().SingleOrDefaultAsync(
                b => b.AgentId == agent.Id && b.CommandId == model.CommandId);
            if (replay != null)
                return replay.RequestHash == requestHash
                    ? BankRunSaveResult.Ok(replay.Id)
                    : BankRunSaveResult.Error("This bank-run request identity was already used for different details.");
        }

        // Find today's open session
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == branchId && s.Status == CashSessionStatus.Open &&
                ((s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation && s.SessionDate == businessDate) ||
                 (s.WorkflowVersion == SessionWorkflowVersion.Legacy && s.SessionDate == today)))
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return BankRunSaveResult.Error("No active cash session found. A session must be open to record a bank run.");
        }
        var modern = session.WorkflowVersion == SessionWorkflowVersion.BankReconciliation;
        if (modern && (!model.CommandId.HasValue || model.CommandId == Guid.Empty))
            return BankRunSaveResult.Error("A stable request identity is required for a bank run.");
        if (modern && (!model.OccurredAt.HasValue || model.OccurredAt > businessClock.UtcNow ||
            businessClock.GetBusinessDate(model.OccurredAt.Value) != session.SessionDate))
            return BankRunSaveResult.Error("Enter the bank receipt's non-future date and time for this session.");
        if (modern && (decimal.Round(model.Amount, 2) != model.Amount || model.Amount > 9_999_999_999_999_999.99m))
            return BankRunSaveResult.Error("Bank run amounts must fit the supported range and use at most two decimal places.");

        // Agent must have an approved opening count before doing a bank run
        var hasApprovedOpening = await dbContext.CashCounts
            .AnyAsync(c => c.CashSessionId == session.Id &&
                           c.AgentId == agent.Id &&
                           c.IsOpening &&
                           c.Status == CashCountStatus.Approved);

        if (!hasApprovedOpening)
        {
            return BankRunSaveResult.Error("Opening cash count must be approved before recording a bank run.");
        }

        // No pending or approved closing count must exist
        var hasClosingCount = await dbContext.CashCounts
            .AnyAsync(c => c.CashSessionId == session.Id &&
                           c.AgentId == agent.Id &&
                           !c.IsOpening &&
                           (c.Status == CashCountStatus.PendingApproval || c.Status == CashCountStatus.Approved));

        if (hasClosingCount)
        {
            return BankRunSaveResult.Error("Cannot record a bank run after a closing count has been submitted or approved.");
        }

        if (model.Amount <= 0)
        {
            return BankRunSaveResult.Error("Bank run amount must be greater than zero.");
        }

        // Start serializable transaction before loading wallets so the balance read
        // and the balance update are protected against concurrent modifications.
        long bankRunId;
        string toWalletName;
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            if (modern)
            {
                var actor = await bankingAccess.GetActorAsync(userId);
                await bankingAccess.RequireTradingSessionAsync(actor, session.Id);
            }
            // Auto-discover the agent's active cash wallet — bank runs always originate from cash.
            var fromWallet = await dbContext.Wallets
                .Include(w => w.WalletType)
                .FirstOrDefaultAsync(w => w.AgentId == agent.Id && w.IsActive && w.WalletType!.Type == WalletTypeEnum.Cash);

            if (fromWallet == null)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error("No active cash wallet found for this agent.");
            }

            var toWallet = await dbContext.Wallets
                .Include(w => w.WalletType)
                .FirstOrDefaultAsync(w => w.WalletTypeId == model.ToWalletTypeId && w.AgentId == agent.Id && w.IsActive);

            if (toWallet == null)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error("No active wallet found for the selected wallet type.");
            }

            if (toWallet.WalletType?.Type == WalletTypeEnum.Cash)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error("The destination wallet type must not be Cash.");
            }

            var sourceBalance = fromWallet.Balance;
            var destinationBalance = toWallet.Balance;
            var availableCash = sourceBalance;
            if (modern)
            {
                if (fromWallet.Currency != toWallet.Currency)
                    throw new BankingRuleException("A bank run cannot transfer between different currencies.");
                var positions = await balances.GetPositionsAsync(session.Id, agent.Id, includePendingAdjustments: true);
                var source = positions.SingleOrDefault(p => p.WalletId == fromWallet.Id)
                    ?? throw new BankingRuleException("The cash wallet is not part of this session's opening allocation.");
                var destination = positions.SingleOrDefault(p => p.WalletId == toWallet.Id)
                    ?? throw new BankingRuleException("The destination wallet is not part of this session's opening allocation.");
                sourceBalance = source.ExpectedBalance;
                destinationBalance = destination.ExpectedBalance;
                availableCash = source.AvailableForTransfer;
            }
            if (model.Amount > availableCash)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error(
                    $"Bank run amount ({model.Amount:N0}) exceeds the cash wallet balance available for transfer ({availableCash:N0}).");
            }

            // Deduct from cash wallet and credit bank wallet
            fromWallet.Balance = sourceBalance - model.Amount;
            fromWallet.UpdatedAt = DateTimeOffset.UtcNow;

            toWallet.Balance = destinationBalance + model.Amount;
            toWallet.UpdatedAt = DateTimeOffset.UtcNow;
            toWalletName = toWallet.Name;

            var bankRun = new BankRun
            {
                CashSessionId = session.Id,
                AgentId = agent.Id,
                FromWalletId = fromWallet.Id,
                ToWalletId = toWallet.Id,
                Amount = model.Amount,
                Currency = fromWallet.Currency,
                ReceiptNumber = model.ReceiptNumber?.Trim(),
                ReceiptImage = model.ReceiptImage,
                ReceiptImageContentType = model.ReceiptImageContentType,
                Notes = model.Notes?.Trim(),
                RecordedByUserId = userId,
                CreatedAt = businessClock.UtcNow,
                OccurredAt = model.OccurredAt?.ToUniversalTime(),
                CommandId = model.CommandId,
                RequestHash = model.CommandId.HasValue ? requestHash : null
            };

            dbContext.BankRuns.Add(bankRun);
            if (modern)
            {
                session.RegisterVersion++;
                session.Version++;
                await WorkflowNotifications.AddForBranchAsync(dbContext, branchId, "Bank run recorded",
                    "A bank run has been recorded in the active session.", NotificationType.BankRunRecorded,
                    "/bank-runs", businessClock.UtcNow, includeSupervisors: true);
            }
            await dbContext.SaveChangesAsync();
            bankRunId = bankRun.Id;
            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            if (modern)
                dbContext.ChangeTracker.Clear();
            throw;
        }

        // Notify branch admins
        var agentName = agent.User != null ? $"{agent.User.FirstName} {agent.User.LastName}".Trim() : agent.Code;
        if (!modern)
            await notificationService.NotifyBranchAdminsAsync(
                branchId,
                "Bank Run Recorded",
                $"{agentName} recorded a bank run of UGX {model.Amount:N0} to {toWalletName}.",
                NotificationType.BankRunRecorded,
                "/bank-runs");

        telemetryClient?.TrackEvent("bank_run_recorded", new Dictionary<string, string>
        {
            { "AgentId", agent.Id.ToString() },
            { "SessionId", session.Id.ToString() },
            { "ToWalletTypeId", model.ToWalletTypeId.ToString() },
            { "Amount", model.Amount.ToString("F2") }
        });

        return BankRunSaveResult.Ok(bankRunId);
    }

    /// <inheritdoc />
    public async Task<List<BankRunDto>> GetBankRunsForSessionAsync(long cashSessionId, long? agentId = null)
    {
        var query = dbContext.BankRuns
            .Include(b => b.FromWallet)
            .Include(b => b.ToWallet)
            .Include(b => b.Agent)
                .ThenInclude(a => a!.User)
            .Where(b => b.CashSessionId == cashSessionId);

        if (agentId.HasValue)
        {
            query = query.Where(b => b.AgentId == agentId.Value);
        }

        var bankRuns = await query.ToListAsync();

        return bankRuns
            .OrderBy(b => b.CreatedAt)
            .Select(MapToDto)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<BankRunDto>> GetBankRunsForAgentAsync(string userId)
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

        return await GetBankRunsForSessionAsync(session.Id, agent.Id);
    }

    /// <inheritdoc />
    public async Task<Dictionary<long, decimal>> GetBankRunTotalsAsync(long cashSessionId, long agentId)
    {
        return await dbContext.BankRuns
            .Where(b => b.CashSessionId == cashSessionId && b.AgentId == agentId)
            .GroupBy(b => b.FromWalletId)
            .Select(g => new { WalletId = g.Key, TotalAmount = g.Sum(b => b.Amount) })
            .ToDictionaryAsync(b => b.WalletId, b => b.TotalAmount);
    }

    /// <inheritdoc />
    public async Task<List<AgentWalletDto>> GetAgentWalletsAsync(string userId, WalletTypeEnum walletType)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null) return [];

        return await dbContext.Wallets
            .Include(w => w.WalletType)
            .Where(w => w.AgentId == agent.Id && w.IsActive && w.WalletType!.Type == walletType)
            .OrderBy(w => w.Name)
            .Select(w => new AgentWalletDto
            {
                Id = w.Id,
                Name = w.Name,
                Balance = w.Balance
            })
            .ToListAsync();
    }

    private async Task<Agent?> GetAgentForUserAsync(string userId)
    {
        return await dbContext.Agents
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId);
    }

    private static BankRunDto MapToDto(BankRun b) => new()
    {
        Id = b.Id,
        CashSessionId = b.CashSessionId,
        FromWalletName = b.FromWallet?.Name ?? "Unknown",
        ToWalletName = b.ToWallet?.Name ?? "Unknown",
        AgentName = b.Agent?.User != null
            ? $"{b.Agent.User.FirstName} {b.Agent.User.LastName}".Trim()
            : "Unknown",
        AgentCode = b.Agent?.Code ?? "N/A",
        Amount = b.Amount,
        Currency = b.Currency,
        HasReceiptImage = b.ReceiptImage != null && b.ReceiptImage.Length > 0,
        ReceiptNumber = b.ReceiptNumber,
        Notes = b.Notes,
        CreatedAt = b.CreatedAt
    };

    /// <inheritdoc />
    public async Task<List<WalletTypeOptionDto>> GetAgentNonCashWalletTypesAsync(string userId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null) return [];

        return await dbContext.Wallets
            .Include(w => w.WalletType)
            .Where(w => w.AgentId == agent.Id && w.IsActive && w.WalletType!.Type != WalletTypeEnum.Cash)
            .OrderBy(w => w.WalletType!.Name)
            .Select(w => new WalletTypeOptionDto
            {
                WalletTypeId = w.WalletTypeId,
                Name = w.WalletType!.Name
            })
            .Distinct()
            .ToListAsync();
    }
}
