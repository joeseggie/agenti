using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Web.Features.Dashboard;

/// <summary>
/// Service for dashboard operations.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IBusinessClock _clock;

    public DashboardService(IDbContextFactory<ApplicationDbContext> dbContextFactory, IBusinessClock? clock = null)
    {
        _dbContextFactory = dbContextFactory;
        _clock = clock ?? new BusinessClock(TimeProvider.System);
    }

    /// <inheritdoc />
    public async Task<DashboardViewModel> GetDashboardAsync(string userId, long? branchId = null, long? agentId = null)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var wallets = await context.Wallets
            .Include(w => w.WalletType)
            .Where(w => w.IsActive)
            .Where(w => (!branchId.HasValue || w.Agent!.BranchId == branchId) &&
                        (!agentId.HasValue || w.AgentId == agentId))
            .OrderBy(w => w.WalletType!.Type)
            .ThenBy(w => w.Name)
            .Select(w => new WalletBalanceSummaryDto
            {
                WalletId = w.Id,
                WalletName = w.Name,
                WalletTypeName = w.WalletType!.Name,
                WalletTypeIcon = GetWalletTypeIcon(w.WalletType.Type),
                Balance = w.Balance,
                Currency = w.Currency,
                SupportsDenominations = w.WalletType.SupportsDenominations
            })
            .ToListAsync();

        // Get agent wallet summaries (each agent's total wallet balance)
        var agentSummaries = await context.Agents
            .Where(a => a.IsActive)
            .Where(a => (!branchId.HasValue || a.BranchId == branchId) &&
                        (!agentId.HasValue || a.Id == agentId))
            .Include(a => a.User)
            .Include(a => a.Wallets.Where(w => w.IsActive))
            .OrderBy(a => a.User!.FirstName)
            .ThenBy(a => a.User!.LastName)
            .Select(a => new AgentWalletSummaryDto
            {
                AgentId = a.Id,
                AgentCode = a.Code,
                AgentName = a.User != null ? (a.User.FirstName + " " + a.User.LastName).Trim() : a.Code,
                TotalBalance = a.Wallets.Where(w => w.IsActive).Sum(w => w.Balance),
                Currency = a.Wallets.Where(w => w.IsActive).Select(w => w.Currency).FirstOrDefault() ?? "UGX"
            })
            .ToListAsync();

        // Get vault balance for the branch
        var vaultBalance = agentId.HasValue ? 0m : await context.Vaults
            .Where(v => !branchId.HasValue || v.BranchId == branchId)
            .Select(v => v.CurrentBalance)
            .FirstOrDefaultAsync();

        // Get the current session for today
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var businessToday = _clock.Today;
        var sessions = context.CashSessions
            .Where(s => !branchId.HasValue || s.BranchId == branchId);
        var currentSession = await sessions
            .Include(s => s.CashCounts.Where(c => !agentId.HasValue || c.AgentId == agentId))
            .Where(s => s.SessionDate == businessToday && s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation)
            .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync();
        currentSession ??= await sessions
            .Include(s => s.CashCounts.Where(c => !agentId.HasValue || c.AgentId == agentId))
            .Where(s => s.SessionDate == today && s.WorkflowVersion == SessionWorkflowVersion.Legacy)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync();

        var sessionStatus = currentSession != null
            ? new SessionStatusDto
            {
                UsesBankReconciliation = currentSession.WorkflowVersion == SessionWorkflowVersion.BankReconciliation,
                OperationalClosedAt = currentSession.OperationalClosedAt,
                PendingClosingApprovalCount = currentSession.CashCounts.Count(c => !c.IsOpening && c.Status == CashCountStatus.PendingApproval),
                SessionId = currentSession.Id,
                SessionDate = currentSession.SessionDate,
                Status = currentSession.Status,
                OpenedAt = currentSession.OpenedAt,
                HasActiveSession = currentSession.Status == CashSessionStatus.Open
            }
            : new SessionStatusDto
            {
                HasActiveSession = false
            };

        return new DashboardViewModel
        {
            Wallets = wallets,
            AgentSummaries = agentSummaries,
            SessionStatus = sessionStatus,
            VaultBalance = vaultBalance,
            Currency = wallets.FirstOrDefault()?.Currency ?? "UGX"
        };
    }

    private static string GetWalletTypeIcon(WalletTypeEnum walletType) => walletType switch
    {
        WalletTypeEnum.Cash => "💵",
        WalletTypeEnum.MobileMoney => "📱",
        WalletTypeEnum.Bank => "🏦",
        WalletTypeEnum.Custom => "💼",
        _ => "💰"
    };
}
