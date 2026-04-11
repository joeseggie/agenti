using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankRuns;

/// <summary>
/// Service for recording and querying bank runs (physical bank deposits).
/// </summary>
public class BankRunService(
    ApplicationDbContext dbContext,
    INotificationService notificationService,
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

        // Find today's open session
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == branchId && s.SessionDate == today && s.Status == CashSessionStatus.Open)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return BankRunSaveResult.Error("No active cash session found. A session must be open to record a bank run.");
        }

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

        if (model.FromWalletId == model.ToWalletId)
        {
            return BankRunSaveResult.Error("Source and destination wallets must be different.");
        }

        // Start serializable transaction before loading wallets so the balance read
        // and the balance update are protected against concurrent modifications.
        long bankRunId;
        string toWalletName;
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            // Re-read wallets inside the transaction so their balances are current
            var fromWallet = await dbContext.Wallets
                .Include(w => w.WalletType)
                .FirstOrDefaultAsync(w => w.Id == model.FromWalletId && w.AgentId == agent.Id && w.IsActive);

            if (fromWallet == null)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error("Source wallet not found or does not belong to this agent.");
            }

            if (fromWallet.WalletType?.Type != WalletTypeEnum.Cash)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error("The source wallet must be a Cash wallet.");
            }

            var toWallet = await dbContext.Wallets
                .Include(w => w.WalletType)
                .FirstOrDefaultAsync(w => w.Id == model.ToWalletId && w.AgentId == agent.Id && w.IsActive);

            if (toWallet == null)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error("Destination wallet not found or does not belong to this agent.");
            }

            if (toWallet.WalletType?.Type != WalletTypeEnum.Bank)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error("The destination wallet must be a Bank wallet.");
            }

            if (model.Amount > fromWallet.Balance)
            {
                await dbTransaction.RollbackAsync();
                return BankRunSaveResult.Error(
                    $"Bank run amount ({model.Amount:N0}) exceeds the cash wallet balance ({fromWallet.Balance:N0}).");
            }

            // Deduct from cash wallet and credit bank wallet
            fromWallet.Balance -= model.Amount;
            fromWallet.UpdatedAt = DateTimeOffset.UtcNow;

            toWallet.Balance += model.Amount;
            toWallet.UpdatedAt = DateTimeOffset.UtcNow;
            toWalletName = toWallet.Name;

            var bankRun = new BankRun
            {
                CashSessionId = session.Id,
                AgentId = agent.Id,
                FromWalletId = model.FromWalletId,
                ToWalletId = model.ToWalletId,
                Amount = model.Amount,
                Currency = fromWallet.Currency,
                Denominations = model.Denominations,
                ReceiptNumber = model.ReceiptNumber?.Trim(),
                Notes = model.Notes?.Trim(),
                RecordedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.BankRuns.Add(bankRun);
            await dbContext.SaveChangesAsync();
            bankRunId = bankRun.Id;
            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }

        // Notify branch admins
        var agentName = agent.User != null ? $"{agent.User.FirstName} {agent.User.LastName}".Trim() : agent.Code;
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
            { "FromWalletId", model.FromWalletId.ToString() },
            { "ToWalletId", model.ToWalletId.ToString() },
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
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == agent.BranchId.Value && s.SessionDate == today && s.Status == CashSessionStatus.Open)
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
        Denominations = b.Denominations,
        ReceiptNumber = b.ReceiptNumber,
        Notes = b.Notes,
        CreatedAt = b.CreatedAt
    };
}
