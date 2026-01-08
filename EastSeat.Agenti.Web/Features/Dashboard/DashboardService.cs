using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Web.Features.Dashboard;

/// <summary>
/// Service for dashboard operations.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _context;

    public DashboardService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<DashboardViewModel> GetDashboardAsync(string userId)
    {
        // For now, we'll get wallets that don't have an agent assigned (system wallets)
        // or wallets where the AgentId matches. In the future, you may want to link
        // ApplicationUser to an Agent entity.
        var wallets = await _context.Wallets
            .Include(w => w.WalletType)
            .Where(w => w.IsActive)
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

        // Get the current session for today
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentSession = await _context.CashSessions
            .Where(s => s.SessionDate == today)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync();

        var sessionStatus = currentSession != null
            ? new SessionStatusDto
            {
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
            SessionStatus = sessionStatus,
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
