using System.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CashCounts;

public sealed class BankSessionWorkflow(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock,
    ISessionBalanceService balances, ISessionSettlementService settlements) : IBankSessionWorkflow
{
    public async Task<bool> IsModernBranchAsync(long branchId, CancellationToken cancellationToken = default) =>
        await db.BankAccounts.AnyAsync(a => a.BranchId == branchId && a.WorkflowEnabled, cancellationToken) ||
        await db.CashSessions.AnyAsync(s => s.BranchId == branchId &&
            s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation, cancellationToken);

    public async Task<bool> HandlesFormAsync(long branchId, CashCountFormModel form, CancellationToken cancellationToken = default)
    {
        var sessions = db.CashSessions.AsNoTracking().Where(s => s.BranchId == branchId);
        CashSession? existing;
        if (form.CashCountId.HasValue)
            existing = await sessions.SingleOrDefaultAsync(s =>
                db.CashCounts.Any(c => c.Id == form.CashCountId && c.CashSessionId == s.Id), cancellationToken);
        else if (form.CashSessionId.HasValue)
            existing = await sessions.SingleOrDefaultAsync(s => s.Id == form.CashSessionId, cancellationToken);
        else
        {
            var date = form.CountDate ?? clock.Today;
            existing = await sessions.SingleOrDefaultAsync(s => s.SessionDate == date, cancellationToken);
        }
        return existing != null
            ? existing.WorkflowVersion == SessionWorkflowVersion.BankReconciliation
            : await IsModernBranchAsync(branchId, cancellationToken);
    }

    public async Task<CurrentSessionDto> GetCurrentSessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (!actor.AgentId.HasValue)
            throw new BankingRuleException("User is not configured as an agent.");
        var today = clock.Today;
        var result = new CurrentSessionDto
        {
            WorkflowVersion = SessionWorkflowVersion.BankReconciliation, ServerTime = clock.UtcNow
        };
        result.CorrectionRequestId = await db.CashCountCorrectionRequests.AsNoTracking()
            .Where(r => r.AgentId == actor.AgentId && r.IsOpen)
            .OrderByDescending(r => r.Id).Select(r => (long?)r.Id).FirstOrDefaultAsync(cancellationToken);
        result.HasPendingApproval = await db.CashCounts.AnyAsync(c =>
            c.AgentId == actor.AgentId && c.Status == CashCountStatus.PendingApproval, cancellationToken);
        var unfinished = await db.CashSessions.AsNoTracking().Where(s => s.BranchId == actor.BranchId &&
                s.SessionDate < today &&
                ((s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation && s.OperationalClosedAt == null) ||
                 (s.WorkflowVersion == SessionWorkflowVersion.Legacy && s.Status != CashSessionStatus.Closed)))
            .OrderByDescending(s => s.SessionDate).FirstOrDefaultAsync(cancellationToken);
        var session = unfinished ?? await db.CashSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.BranchId == actor.BranchId && s.SessionDate == today, cancellationToken);
        if (session == null)
        {
            var enabled = await HasEnrollmentAsync(actor, cancellationToken);
            result.StatusText = enabled ? "Ready for opening count" : "Bank workflow enrollment is paused";
            result.StatusColor = enabled ? "info" : "warning";
            result.CanPerformOpeningCount = enabled;
            result.BlockReason = enabled ? null : "An administrator must configure and verify the bank account before starting a new session.";
            return result;
        }

        result.SessionId = session.Id;
        result.SessionDate = session.SessionDate;
        var counts = await db.CashCounts.AsNoTracking().Include(c => c.ApprovedByUser)
            .Where(c => c.CashSessionId == session.Id && c.AgentId == actor.AgentId)
            .ToListAsync(cancellationToken);
        var opening = counts.SingleOrDefault(c => c.IsOpening);
        var closing = counts.SingleOrDefault(c => !c.IsOpening);
        var frozenIds = await db.CashCountSnapshots.Where(s => s.CashCount!.CashSessionId == session.Id &&
                s.CashCount.AgentId == actor.AgentId)
            .Select(s => s.CashCountId).ToListAsync(cancellationToken);
        var frozenClosing = closing != null && frozenIds.Contains(closing.Id);
        result.OpeningCountId = opening?.Id;
        result.ClosingCountId = closing?.Id;
        result.OpeningCountStatus = opening?.Status;
        result.ClosingCountStatus = closing?.Status;
        result.HasOpeningCount = opening != null && opening.Status != CashCountStatus.Rejected;
        result.HasClosingCount = frozenClosing;
        result.OpeningCountApprovedAt = opening?.ApprovedAt;
        result.ClosingCountApprovedAt = closing?.ApprovedAt;
        result.OpeningCountApprovedByName = opening?.ApprovedByUser?.FullName;
        result.ClosingCountApprovedByName = closing?.ApprovedByUser?.FullName;
        if (session.OperationalClosedAt.HasValue || frozenClosing || session.Status == CashSessionStatus.Closed)
        {
            result.StatusText = closing?.Status == CashCountStatus.Approved
                ? "Session ended; closing approved" : "Session ended; awaiting admin approval";
            result.StatusColor = closing?.Status == CashCountStatus.Approved ? "default" : "warning";
            return result;
        }

        var hasApprovedOpening = opening?.Status == CashCountStatus.Approved;
        result.CanPerformOpeningCount = unfinished == null &&
            (opening == null || (!frozenIds.Contains(opening.Id) && opening.Status != CashCountStatus.Approved));
        result.CanPerformClosingCount = hasApprovedOpening &&
            (closing == null || closing.Status is CashCountStatus.Draft or CashCountStatus.Rejected);
        var canTrade = unfinished == null && hasApprovedOpening &&
            (closing == null || closing.Status == CashCountStatus.Draft);
        result.CanRecordCustomerTransactions = canTrade;
        result.CanRecordBankRun = canTrade;
        result.CanRecordAdjustment = canTrade;
        result.StatusText = unfinished != null ? "Previous session still needs a closing submission" :
            hasApprovedOpening ? "Session open" : "Opening count awaiting approval";
        result.StatusColor = hasApprovedOpening && unfinished == null ? "success" : "warning";
        if (unfinished != null)
            result.BlockReason = "Submit the previous session's closing count. Its later bank review does not need to be complete.";
        return result;
    }

    public async Task<CashCountFormModel> InitializeFormAsync(
        string userId, bool isOpening, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        var current = await GetCurrentSessionAsync(userId, cancellationToken);
        if (isOpening ? !current.CanPerformOpeningCount : !current.CanPerformClosingCount)
            throw new BankingRuleException(current.BlockReason ?? "This count cannot be entered in the current session state.");
        var existingId = isOpening ? current.OpeningCountId : current.ClosingCountId;
        if (existingId.HasValue)
            return await GetFormAsync(userId, existingId.Value, cancellationToken)
                ?? throw new BankingRuleException("Cash count not found.");
        var wallets = await db.Wallets.AsNoTracking().Include(w => w.WalletType)
            .Where(w => w.AgentId == actor.AgentId && w.IsActive)
            .OrderBy(w => w.WalletType!.Type).ThenBy(w => w.Name).ToListAsync(cancellationToken);
        Dictionary<long, decimal>? expected = null;
        if (!isOpening && current.SessionId.HasValue)
            expected = (await balances.GetPositionsAsync(current.SessionId.Value, actor.AgentId!.Value,
                includePendingAdjustments: true, cancellationToken)).ToDictionary(p => p.WalletId, p => p.ExpectedBalance);
        return new CashCountFormModel
        {
            CashSessionId = current.SessionId, IsOpening = isOpening,
            CountDate = current.SessionDate ?? clock.Today,
            WalletEntries = wallets.Select(w => new WalletCountEntryDto
            {
                WalletId = w.Id, WalletName = w.Name, WalletTypeName = w.WalletType!.Name,
                SupportsDenominations = w.WalletType.SupportsDenominations,
                ExpectedBalance = expected == null ? w.Balance : expected.TryGetValue(w.Id, out var value)
                    ? value : throw new BankingRuleException("Wallet allocation changed after opening."),
                ObservedAt = null,
                Denominations = w.WalletType.SupportsDenominations ? DenominationBreakdown.Empty : null
            }).ToList()
        };
    }

    public async Task<CashCountFormModel?> GetFormAsync(
        string userId, long cashCountId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        var count = await db.CashCounts.AsNoTracking().Include(c => c.CashSession)
            .Include(c => c.Details).ThenInclude(d => d.Wallet).ThenInclude(w => w!.WalletType)
            .SingleOrDefaultAsync(c => c.Id == cashCountId && c.AgentId == actor.AgentId &&
                c.CashSession!.BranchId == actor.BranchId, cancellationToken);
        if (count == null)
            return null;
        var frozenSnapshot = await db.CashCountSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.CashCountId == count.Id, cancellationToken);
        var frozen = frozenSnapshot != null;
        Dictionary<long, decimal>? expected = null;
        if (!count.IsOpening)
            expected = (frozenSnapshot != null
                ? await balances.GetPositionsAtSnapshotAsync(frozenSnapshot.Id, includePendingAdjustments: true, cancellationToken)
                : await balances.GetPositionsAsync(count.CashSessionId, count.AgentId,
                    includePendingAdjustments: true, cancellationToken)).ToDictionary(p => p.WalletId, p => p.ExpectedBalance);
        return new CashCountFormModel
        {
            CashCountId = count.Id, CashSessionId = count.CashSessionId, IsOpening = count.IsOpening,
            CountDate = count.CountDate, Explanation = count.Explanation, IsReadOnly = frozen,
            WalletEntries = count.Details.Select(d => new WalletCountEntryDto
            {
                WalletId = d.WalletId, WalletName = d.Wallet!.Name, WalletTypeName = d.Wallet.WalletType!.Name,
                SupportsDenominations = d.Wallet.WalletType.SupportsDenominations,
                ExpectedBalance = expected == null ? d.Wallet.Balance : expected.TryGetValue(d.WalletId, out var value)
                    ? value : throw new BankingRuleException("Wallet allocation is missing from the opening snapshot."),
                CountedAmount = d.Amount, ObservedAt = d.ObservedAt,
                Denominations = d.Denominations == null ? null : DenominationBreakdown.FromJson(d.Denominations)
            }).ToList()
        };
    }

    public async Task<CashCountSaveResult> SaveAsync(
        string userId, CashCountFormModel form, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (!actor.AgentId.HasValue)
            throw new UnauthorizedAccessException("An active agent assignment is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var date = form.CountDate ?? clock.Today;
            if (date > clock.Today || (form.IsOpening && date != clock.Today))
                throw new BankingRuleException("New opening counts must use today's business date; future counts are not permitted.");
            var session = await db.CashSessions.Include(s => s.CashCounts.Where(c => c.AgentId == actor.AgentId))
                .SingleOrDefaultAsync(s => s.BranchId == actor.BranchId && s.SessionDate == date, cancellationToken);
            if (form.CashSessionId.HasValue && form.CashSessionId != session?.Id)
                throw new UnauthorizedAccessException("The selected session does not match this count's branch and business date.");
            if (session == null)
            {
                if (!form.IsOpening || !await HasEnrollmentAsync(actor, cancellationToken))
                    throw new BankingRuleException("A verified, enabled bank workflow is required to start this session.");
                var previousActive = await db.CashSessions.AnyAsync(s => s.BranchId == actor.BranchId && s.SessionDate < date &&
                    ((s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation && s.OperationalClosedAt == null) ||
                     (s.WorkflowVersion == SessionWorkflowVersion.Legacy && s.Status != CashSessionStatus.Closed)), cancellationToken);
                if (previousActive)
                    throw new BankingRuleException("Submit the previous session's closing before starting another session.");
                session = new CashSession
                {
                    BranchId = actor.BranchId, SessionDate = date, WorkflowVersion = SessionWorkflowVersion.BankReconciliation,
                    Status = CashSessionStatus.Open, OpenedAt = clock.UtcNow
                };
                db.CashSessions.Add(session);
                await db.SaveChangesAsync(cancellationToken);
            }
            if (session.WorkflowVersion != SessionWorkflowVersion.BankReconciliation ||
                session.OperationalClosedAt.HasValue || session.Status != CashSessionStatus.Open)
                throw new BankingRuleException("This session is not open for new count observations.");
            var count = session.CashCounts.SingleOrDefault(c => c.IsOpening == form.IsOpening);
            if (form.CashCountId.HasValue && form.CashCountId != count?.Id)
                throw new UnauthorizedAccessException("The cash count identity does not match this session.");
            if (count != null && (count.Status == CashCountStatus.Approved ||
                await db.CashCountSnapshots.AnyAsync(s => s.CashCountId == count.Id, cancellationToken)))
                throw new BankingRuleException("Submitted observations are immutable. Use the administrator's correction workflow.");
            if (await db.CashCountSnapshots.AnyAsync(s => s.CashCount!.CashSessionId == session.Id &&
                    s.CashCount.AgentId == actor.AgentId && !s.CashCount.IsOpening, cancellationToken))
                throw new BankingRuleException("This agent's session has ended. New trading and count edits are not permitted.");
            if (!form.IsOpening && !session.CashCounts.Any(c => c.IsOpening && c.Status == CashCountStatus.Approved))
                throw new BankingRuleException("The opening count must be approved before closing.");
            if (form.Explanation?.Length > 2000)
                throw new BankingRuleException("The explanation cannot exceed 2,000 characters.");
            if (Math.Abs(form.TotalAmount) > 9_999_999_999_999_999.99m)
                throw new BankingRuleException("The count total exceeds the supported financial range.");

            var wallets = await db.Wallets.Include(w => w.WalletType)
                .Where(w => w.AgentId == actor.AgentId && w.IsActive).ToListAsync(cancellationToken);
            if (form.WalletEntries.Count == 0 || form.WalletEntries.Select(w => w.WalletId).Distinct().Count() != form.WalletEntries.Count ||
                !form.WalletEntries.Select(w => w.WalletId).Order().SequenceEqual(wallets.Select(w => w.Id).Order()))
                throw new BankingRuleException("Count each allocated active wallet exactly once.");
            foreach (var entry in form.WalletEntries)
            {
                var wallet = wallets.Single(w => w.Id == entry.WalletId);
                if (!entry.ObservedAt.HasValue || entry.ObservedAt > clock.UtcNow ||
                    decimal.Round(entry.CountedAmount, 2) != entry.CountedAmount ||
                    Math.Abs(entry.CountedAmount) > 9_999_999_999_999_999.99m ||
                    (wallet.WalletType!.Type == WalletTypeEnum.Cash && entry.CountedAmount < 0))
                    throw new BankingRuleException("Enter valid observation times and two-decimal wallet amounts; cash cannot be negative.");
            }
            session.Version++;
            var previousStatus = count?.Status;
            if (count == null)
            {
                count = new CashCount
                {
                    CashSessionId = session.Id, AgentId = actor.AgentId.Value, IsOpening = form.IsOpening,
                    CountDate = date, CreatedAt = clock.UtcNow
                };
                db.CashCounts.Add(count);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var details = await db.CashCountDetails.Where(d => d.CashCountId == count.Id).ToListAsync(cancellationToken);
                db.CashCountDetails.RemoveRange(details);
            }
            count.Status = CashCountStatus.Draft;
            count.SubmittedAt = null;
            count.RejectedAt = null;
            count.RejectedByUserId = null;
            count.RejectionReason = null;
            count.TotalAmount = form.WalletEntries.Sum(w => w.CountedAmount);
            count.Explanation = form.Explanation?.Trim();
            foreach (var entry in form.WalletEntries)
                db.CashCountDetails.Add(new CashCountDetail
                {
                    CashCountId = count.Id, WalletId = entry.WalletId, Amount = entry.CountedAmount,
                    ObservedAt = entry.ObservedAt!.Value.ToUniversalTime(), Denominations = entry.Denominations?.ToJson()
                });
            db.CashCountAuditLogs.Add(new CashCountAuditLog
            {
                CashCountId = count.Id, CashSessionId = session.Id, AgentId = actor.AgentId.Value,
                IsOpening = count.IsOpening, Action = previousStatus.HasValue ? CashCountAuditAction.Saved : CashCountAuditAction.Created,
                PreviousStatus = previousStatus, NewStatus = CashCountStatus.Draft,
                TotalAmount = count.TotalAmount, PerformedByUserId = actor.UserId, PerformedAt = clock.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CashCountSaveResult.Ok(count.Id, session.Id);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<CashCountSaveResult> SubmitAsync(
        string userId, long cashCountId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        var identity = await db.CashCounts.AsNoTracking().Where(c => c.Id == cashCountId &&
                c.AgentId == actor.AgentId && c.CashSession!.BranchId == actor.BranchId)
            .Select(c => new { c.IsOpening }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("You do not have access to this cash count.");
        if (!identity.IsOpening)
        {
            var result = await settlements.SubmitClosingAsync(userId, cashCountId, cancellationToken);
            return CashCountSaveResult.Ok(result.CashCountId, result.CashSessionId);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var count = await db.CashCounts.Include(c => c.CashSession).SingleAsync(c => c.Id == cashCountId, cancellationToken);
            if (count.Status == CashCountStatus.PendingApproval ||
                (count.Status == CashCountStatus.Approved &&
                 await db.SessionFloatSettlements.AnyAsync(s => s.CashCountId == count.Id &&
                     s.Stage == FloatSettlementStage.Opening, cancellationToken)))
            {
                await transaction.CommitAsync(cancellationToken);
                return CashCountSaveResult.Ok(count.Id, count.CashSessionId);
            }
            if (count.Status != CashCountStatus.Draft || count.CashSession!.OperationalClosedAt.HasValue)
                throw new BankingRuleException("This opening count cannot be submitted.");
            count.Status = CashCountStatus.PendingApproval;
            count.SubmittedAt = clock.UtcNow;
            count.CashSession!.Version++;
            db.CashCountAuditLogs.Add(new CashCountAuditLog
            {
                CashCountId = count.Id, CashSessionId = count.CashSessionId, AgentId = count.AgentId, IsOpening = true,
                Action = CashCountAuditAction.Submitted, PreviousStatus = CashCountStatus.Draft,
                NewStatus = CashCountStatus.PendingApproval, TotalAmount = count.TotalAmount,
                PerformedByUserId = userId, PerformedAt = clock.UtcNow
            });
            await WorkflowNotifications.AddForBranchAsync(db, actor.BranchId, "Opening count pending approval",
                "An opening allocation requires approval. Older bank reviews do not block this request.",
                NotificationType.CountPendingApproval, "/cashcount-approvals", clock.UtcNow,
                includeSupervisors: true, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CashCountSaveResult.Ok(count.Id, count.CashSessionId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<CashCountSaveResult> ApproveOpeningAsync(
        string userId, long cashCountId, CancellationToken cancellationToken = default)
    {
        var result = await settlements.ApproveOpeningAsync(userId, cashCountId, cancellationToken);
        return CashCountSaveResult.Ok(result.CashCountId, result.CashSessionId);
    }

    public async Task<CashCountSaveResult> RejectOpeningAsync(
        string userId, long cashCountId, string reason, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (actor.Role is not (UserRole.Admin or UserRole.Supervisor))
            throw new UnauthorizedAccessException("Only administrators or supervisors can reject an opening count.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 2000)
            throw new BankingRuleException("Provide a rejection reason between 10 and 2,000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var count = await db.CashCounts.Include(c => c.CashSession).Include(c => c.Agent)
                .SingleOrDefaultAsync(c => c.Id == cashCountId && c.IsOpening, cancellationToken)
                ?? throw new BankingRuleException("Opening count not found.");
            if (count.CashSession!.BranchId != actor.BranchId)
                throw new UnauthorizedAccessException("You do not have access to this branch.");
            if (count.Status != CashCountStatus.PendingApproval ||
                await db.CashCountSnapshots.AnyAsync(s => s.CashCountId == count.Id, cancellationToken))
                throw new BankingRuleException("This opening count is no longer awaiting initial approval.");
            count.Status = CashCountStatus.Rejected;
            count.RejectedAt = clock.UtcNow;
            count.RejectedByUserId = userId;
            count.RejectionReason = reason.Trim();
            count.CashSession.Version++;
            db.CashCountAuditLogs.Add(new CashCountAuditLog
            {
                CashCountId = count.Id, CashSessionId = count.CashSessionId, AgentId = count.AgentId,
                IsOpening = true, Action = CashCountAuditAction.Rejected,
                PreviousStatus = CashCountStatus.PendingApproval, NewStatus = CashCountStatus.Rejected,
                TotalAmount = count.TotalAmount, Notes = reason.Trim(), PerformedByUserId = userId, PerformedAt = clock.UtcNow
            });
            WorkflowNotifications.AddForUser(db, count.Agent!.UserId, "Opening count returned",
                reason.Trim(), NotificationType.CountRejected, "/cashcount", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CashCountSaveResult.Ok(count.Id, count.CashSessionId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<bool> HasEnrollmentAsync(BankingActor actor, CancellationToken cancellationToken) =>
        db.BankAccounts.AnyAsync(a => a.BranchId == actor.BranchId && a.Wallet!.AgentId == actor.AgentId &&
            a.IsActive && a.Wallet.IsActive && a.WorkflowEnabled && a.ImportProfileVerified &&
            a.BalanceBasis != BankBalanceBasis.Unverified, cancellationToken);
}
