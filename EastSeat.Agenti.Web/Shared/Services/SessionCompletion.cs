using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Shared.Services;

/// <summary>
/// One settlement-aware definition of a finished session, shared by every closing path. A closing whose float is already
/// settled counts as submitted even while its financial approval is still pending.
/// </summary>
internal static class SessionCompletion
{
    public static async Task<bool> IsCompleteAsync(
        ApplicationDbContext db, long sessionId, CancellationToken cancellationToken = default)
    {
        var counts = await db.CashCounts.AsNoTracking().Where(c => c.CashSessionId == sessionId)
            .Select(c => new
            {
                c.AgentId, c.IsOpening, c.Status,
                Settled = db.SessionFloatSettlements.Any(s =>
                    s.CashCountId == c.Id && s.Stage == FloatSettlementStage.Closing)
            }).ToListAsync(cancellationToken);
        if (counts.Any(c => c.IsOpening && c.Status == CashCountStatus.PendingApproval) ||
            counts.Any(c => !c.IsOpening && c.Status == CashCountStatus.PendingApproval && !c.Settled))
            return false;
        var openingAgents = counts.Where(c => c.IsOpening && c.Status == CashCountStatus.Approved)
            .Select(c => c.AgentId).Distinct().ToList();
        if (openingAgents.Count == 0)
            return false;
        var finished = counts.Where(c => !c.IsOpening &&
                (c.Status == CashCountStatus.Approved || (c.Status == CashCountStatus.PendingApproval && c.Settled)))
            .Select(c => c.AgentId).ToHashSet();
        return openingAgents.All(finished.Contains);
    }

    /// <summary>Closes an open session once every opened agent has finished; returns whether it was closed now.</summary>
    public static async Task<bool> CloseIfCompleteAsync(
        ApplicationDbContext db, CashSession session, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (session.Status != CashSessionStatus.Open || !await IsCompleteAsync(db, session.Id, cancellationToken))
            return false;
        session.Status = CashSessionStatus.Closed;
        session.ClosedAt = now;
        if (await db.SessionFloatSettlements.AnyAsync(s => s.CashCount!.CashSessionId == session.Id &&
                s.Stage == FloatSettlementStage.Closing, cancellationToken))
            session.OperationalClosedAt ??= now;
        return true;
    }
}
