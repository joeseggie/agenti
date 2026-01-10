using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CashSessions;

/// <summary>
/// Service implementation for cash session operations.
/// </summary>
public class CashSessionService(ApplicationDbContext dbContext) : ICashSessionService
{
    /// <inheritdoc />
    public async Task<List<CashSessionListItemDto>> GetCashSessionsAsync()
    {
        var sessions = await dbContext.CashSessions
            .Include(cs => cs.Agent)
                .ThenInclude(a => a!.User)
            .Include(cs => cs.CashCounts)
            .OrderByDescending(cs => cs.SessionDate)
            .ThenByDescending(cs => cs.OpenedAt)
            .ToListAsync();

        return sessions.Select(s =>
        {
            var openingCount = s.CashCounts.FirstOrDefault(c => c.IsOpening);
            var closingCount = s.CashCounts.FirstOrDefault(c => !c.IsOpening);

            return new CashSessionListItemDto
            {
                Id = s.Id,
                SessionDate = s.SessionDate,
                AgentName = s.Agent?.User != null
                    ? $"{s.Agent.User.FirstName} {s.Agent.User.LastName}".Trim()
                    : "Unknown",
                AgentCode = s.Agent?.Code ?? "N/A",
                Status = s.Status,
                OpenedAt = s.OpenedAt,
                ClosedAt = s.ClosedAt,
                OpeningTotal = openingCount?.TotalAmount ?? 0,
                ClosingTotal = closingCount?.TotalAmount
            };
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<CashSessionDetailDto?> GetCashSessionDetailAsync(long sessionId)
    {
        var session = await dbContext.CashSessions
            .Include(cs => cs.Agent)
                .ThenInclude(a => a!.User)
            .Include(cs => cs.CashCounts)
                .ThenInclude(c => c.Details)
                    .ThenInclude(d => d.Wallet)
                        .ThenInclude(w => w!.WalletType)
            .FirstOrDefaultAsync(cs => cs.Id == sessionId);

        if (session == null)
        {
            return null;
        }

        var openingCount = session.CashCounts.FirstOrDefault(c => c.IsOpening);
        var closingCount = session.CashCounts.FirstOrDefault(c => !c.IsOpening);

        return new CashSessionDetailDto
        {
            Id = session.Id,
            SessionDate = session.SessionDate,
            AgentName = session.Agent?.User != null
                ? $"{session.Agent.User.FirstName} {session.Agent.User.LastName}".Trim()
                : "Unknown",
            AgentCode = session.Agent?.Code ?? "N/A",
            Status = session.Status,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            OpeningTotal = openingCount?.TotalAmount ?? 0,
            ClosingTotal = closingCount?.TotalAmount,
            OpeningCount = openingCount != null ? MapToCashCountDetailDto(openingCount) : null,
            ClosingCount = closingCount != null ? MapToCashCountDetailDto(closingCount) : null
        };
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? ErrorMessage)> CloseSessionAsync(long sessionId)
    {
        var session = await dbContext.CashSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
        {
            return (false, "Cash session not found.");
        }

        if (session.Status == Shared.Domain.Enums.CashSessionStatus.Closed)
        {
            return (false, "Session is already closed.");
        }

        session.Status = Shared.Domain.Enums.CashSessionStatus.Closed;
        session.ClosedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        return (true, null);
    }

    private static CashCountDetailDto MapToCashCountDetailDto(EastSeat.Agenti.Shared.Domain.Entities.CashCount cashCount)
    {
        return new CashCountDetailDto
        {
            Id = cashCount.Id,
            TotalAmount = cashCount.TotalAmount,
            CreatedAt = cashCount.CreatedAt,
            SubmittedAt = cashCount.SubmittedAt,
            WalletEntries = cashCount.Details.Select(d => new WalletCountSummaryDto
            {
                WalletId = d.WalletId,
                WalletName = d.Wallet?.Name ?? "Unknown",
                WalletTypeName = d.Wallet?.WalletType?.Name ?? "Unknown",
                Amount = d.Amount
            }).OrderBy(w => w.WalletTypeName).ThenBy(w => w.WalletName).ToList()
        };
    }
}
