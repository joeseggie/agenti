using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Shared.Services;

public sealed class BankingAccessService(ApplicationDbContext db, IBusinessClock clock) : IBankingAccessService
{
    public async Task<BankingActor> GetActorAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Id, u.BranchId, u.Role })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("An active authenticated user is required.");
        var agent = await db.Agents.AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .Select(a => new { a.Id, a.BranchId })
            .SingleOrDefaultAsync(cancellationToken);
        var branchId = user.BranchId ?? agent?.BranchId
            ?? throw new UnauthorizedAccessException("A branch assignment is required for banking operations.");
        if (agent?.BranchId != null && agent.BranchId != branchId)
            throw new UnauthorizedAccessException("User and agent branch assignments must agree.");
        return new BankingActor(user.Id, branchId, agent?.Id, user.Role);
    }

    public async Task<BankAccountScope> RequireAccountAsync(
        string userId, long accountId, bool adminOnly = false, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(userId, cancellationToken);
        if (adminOnly)
            actor.RequireAdmin();
        var account = await db.BankAccounts.AsNoTracking()
            .Where(a => a.Id == accountId && a.BranchId == actor.BranchId)
            .Select(a => new BankAccountScope(a.Id, a.BranchId, a.WalletId, a.Wallet!.AgentId,
                a.Currency, a.BankName, a.IsActive && a.Wallet.IsActive,
                a.WorkflowEnabled, a.ImportProfileVerified))
            .SingleOrDefaultAsync(cancellationToken);
        if (account == null || (actor.Role != UserRole.Admin && actor.AgentId != account.AgentId))
            throw new UnauthorizedAccessException("You do not have access to this bank account.");
        return account;
    }

    public async Task RequireTradingSessionAsync(
        BankingActor actor, long sessionId, CancellationToken cancellationToken = default)
    {
        if (!actor.AgentId.HasValue)
            throw new UnauthorizedAccessException("An active agent assignment is required.");
        var session = await db.CashSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.BranchId == actor.BranchId)
            .Select(s => new { s.Status, s.SessionDate, s.WorkflowVersion, s.OperationalClosedAt })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("You do not have access to this session.");
        if (session.WorkflowVersion != SessionWorkflowVersion.BankReconciliation)
            throw new BankingRuleException("This is a legacy session. Historical entries require an administrator's backfill request.");
        if (session.OperationalClosedAt.HasValue || session.Status != CashSessionStatus.Open ||
            session.SessionDate != clock.Today)
            throw new BankingRuleException("This session is not open for new transactions.");
        if (!await db.CashCounts.AnyAsync(c => c.CashSessionId == sessionId && c.AgentId == actor.AgentId &&
                c.IsOpening && c.Status == CashCountStatus.Approved, cancellationToken))
            throw new BankingRuleException("The opening count must be approved before recording transactions.");
        if (await db.CashCountSnapshots.AnyAsync(s => s.CashCount!.CashSessionId == sessionId &&
                s.CashCount.AgentId == actor.AgentId && !s.CashCount.IsOpening, cancellationToken))
            throw new BankingRuleException("Closing has been submitted. Only explicitly requested historical corrections are permitted.");
        if (await db.CashCounts.AnyAsync(c => c.CashSessionId == sessionId && c.AgentId == actor.AgentId &&
                !c.IsOpening && c.Status != CashCountStatus.Draft && c.Status != CashCountStatus.Rejected, cancellationToken))
            throw new BankingRuleException("This closing count is no longer open for new transactions.");
    }
}
