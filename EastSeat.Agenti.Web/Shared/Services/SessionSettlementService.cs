using System.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Shared.Services;

public sealed class SessionSettlementService(
    ApplicationDbContext db, IVaultService vaultService, IBankingAccessService access,
    ISessionBalanceService balances, IBusinessClock clock) : ISessionSettlementService
{
    public Task<SessionSettlementResult> ApproveOpeningAsync(
        string userId, long cashCountId, CancellationToken cancellationToken = default) =>
        SettleAsync(userId, cashCountId, true, cancellationToken);

    public Task<SessionSettlementResult> SubmitClosingAsync(
        string userId, long cashCountId, CancellationToken cancellationToken = default) =>
        SettleAsync(userId, cashCountId, false, cancellationToken);

    private async Task<SessionSettlementResult> SettleAsync(
        string userId, long cashCountId, bool isOpening, CancellationToken cancellationToken)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        await using var transaction = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        try
        {
            var count = await db.CashCounts
                .Include(c => c.CashSession)
                .Include(c => c.Agent)
                .Include(c => c.Details).ThenInclude(d => d.Wallet).ThenInclude(w => w!.WalletType)
                .SingleOrDefaultAsync(c => c.Id == cashCountId, cancellationToken)
                ?? throw new BankingRuleException("Cash count not found.");
            var session = count.CashSession!;
            var legacyClosing = !isOpening && session.WorkflowVersion == SessionWorkflowVersion.Legacy;
            if (session.BranchId != actor.BranchId ||
                (!isOpening && count.AgentId != actor.AgentId &&
                 !(legacyClosing && count.Status == CashCountStatus.PendingApproval &&
                   actor.Role is UserRole.Admin or UserRole.Supervisor)))
                throw new UnauthorizedAccessException("You do not have access to this cash count.");
            if (isOpening && actor.Role is not (UserRole.Admin or UserRole.Supervisor))
                throw new UnauthorizedAccessException("Only administrators or supervisors can approve opening counts.");
            if (count.IsOpening != isOpening ||
                (session.WorkflowVersion != SessionWorkflowVersion.BankReconciliation &&
                 !(legacyClosing && count.Details.Any(d => d.Wallet!.WalletType!.Type == WalletTypeEnum.Bank))))
                throw new BankingRuleException("This operation requires an eligible cash count.");
            if (isOpening && (count.Agent!.UserId == actor.UserId || count.AgentId == actor.AgentId))
                throw new UnauthorizedAccessException("You cannot approve your own opening count.");

            var stage = isOpening ? FloatSettlementStage.Opening : FloatSettlementStage.Closing;
            var existing = await db.SessionFloatSettlements.AsNoTracking()
                .SingleOrDefaultAsync(s => s.CashCountId == count.Id && s.Stage == stage, cancellationToken);
            if (existing != null)
            {
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                return new SessionSettlementResult(count.Id, session.Id, existing.SnapshotId, true);
            }
            if (session.OperationalClosedAt.HasValue || session.Status != CashSessionStatus.Open)
                throw new BankingRuleException("This session is already locked for trading.");
            var requiredStatus = isOpening ? CashCountStatus.PendingApproval : CashCountStatus.Draft;
            if (count.Status != requiredStatus && !(legacyClosing && count.Status == CashCountStatus.PendingApproval))
                throw new BankingRuleException(isOpening
                    ? "The opening count is not pending approval."
                    : "Only a draft closing count can be submitted.");
            if (!isOpening && !legacyClosing && !await db.SessionFloatSettlements.AnyAsync(
                    s => s.CashCount!.CashSessionId == session.Id && s.CashCount.AgentId == count.AgentId &&
                         s.Stage == FloatSettlementStage.Opening, cancellationToken))
                throw new BankingRuleException("The opening allocation must be approved before closing.");
            if (legacyClosing && !await db.CashCounts.AnyAsync(c => c.CashSessionId == session.Id &&
                    c.AgentId == count.AgentId && c.IsOpening && c.Status == CashCountStatus.Approved, cancellationToken))
                throw new BankingRuleException("The opening count must be approved before closing.");

            var now = clock.UtcNow;
            var walletSnapshots = await ReadCountWalletsAsync(count, now, cancellationToken);
            if (count.TotalAmount != walletSnapshots.Sum(w => w.Amount))
                throw new BankingRuleException("The count total does not agree with its wallet amounts.");
            if (count.TotalAmount < 0)
                throw new BankingRuleException("A negative total cannot be allocated as float. Administrative review is required.");
            var previousStatus = count.Status;
            session.Version++;
            var snapshot = new CashCountSnapshot
            {
                CashCountId = count.Id, RegisterVersion = session.RegisterVersion,
                TotalAmount = count.TotalAmount, WalletsJson = CountSnapshotCodec.Serialize(walletSnapshots),
                CapturedByUserId = actor.UserId, CapturedAt = now
            };
            db.CashCountSnapshots.Add(snapshot);
            await db.SaveChangesAsync(cancellationToken);

            var bankVariance = isOpening
                ? await RecordOpeningBridgesAsync(count, snapshot, walletSnapshots, now, cancellationToken)
                : 0m;
            var vaultAmount = count.TotalAmount - bankVariance;
            if (vaultAmount < 0)
                throw new BankingRuleException("The opening's internal allocation cannot be negative.");

            if (!isOpening && !legacyClosing)
            {
                var compared = walletSnapshots.Where(w => !w.WalletType.UsesEnteredClosingAmount()).ToList();
                var positions = await balances.GetPositionsAtSnapshotAsync(snapshot.Id,
                    includePendingAdjustments: true, cancellationToken, compared.Select(w => w.WalletId).ToHashSet());
                if (!positions.Select(p => p.WalletId).Order().SequenceEqual(walletSnapshots.Select(w => w.WalletId).Order()))
                    throw new BankingRuleException("Closing must contain the same wallets as opening.");
                if (walletSnapshots.Any(w => positions.Single(p => p.WalletId == w.WalletId).Currency != w.Currency))
                    throw new BankingRuleException("Wallet currencies cannot change between opening and closing.");
                var hasVariance = compared.Any(w => positions.Single(p => p.WalletId == w.WalletId).ExpectedBalance != w.Amount);
                if (hasVariance && string.IsNullOrWhiteSpace(count.Explanation))
                    throw new BankingRuleException("Closing wallet balances differ from recorded movements. Provide an explanation for admin review.");
                var nonBank = compared.Where(w => w.WalletType != WalletTypeEnum.Bank).ToList();
                var expectedNonBank = nonBank.Sum(w => positions.Single(p => p.WalletId == w.WalletId).ExpectedBalance);
                if (nonBank.Any(w => positions.Single(p => p.WalletId == w.WalletId).ExpectedBalance != w.Amount) &&
                    !await db.Discrepancies.AnyAsync(d => d.CashCountId == count.Id &&
                        d.Status == DiscrepancyStatus.PendingReview, cancellationToken))
                    db.Discrepancies.Add(new Discrepancy
                    {
                        CashSessionId = session.Id, CashCountId = count.Id, Status = DiscrepancyStatus.PendingReview,
                        ExpectedAmount = expectedNonBank, ActualAmount = nonBank.Sum(w => w.Amount),
                        Variance = nonBank.Sum(w => w.Amount) - expectedNonBank,
                        Explanation = count.Explanation, ExplainedByUserId = count.AgentId,
                        ExplainedAt = now, CreatedAt = now
                    });
                await EnsureNoLaterMovementsAsync(count, walletSnapshots, cancellationToken);
            }
            if (legacyClosing)
            {
                var opening = await db.CashCounts.Include(c => c.Details).SingleAsync(c =>
                    c.CashSessionId == session.Id && c.AgentId == count.AgentId &&
                    c.IsOpening && c.Status == CashCountStatus.Approved, cancellationToken);
                if (!opening.Details.Select(d => d.WalletId).Order()
                    .SequenceEqual(walletSnapshots.Select(w => w.WalletId).Order()))
                    throw new BankingRuleException("Closing must contain the same wallets as opening.");
                var compared = walletSnapshots.Where(w => !w.WalletType.UsesEnteredClosingAmount()).ToList();
                var comparedIds = compared.Select(w => w.WalletId).ToArray();
                var adjustments = await db.WalletAdjustments.Where(a => comparedIds.Contains(a.WalletId) && a.CashSessionId == session.Id &&
                        a.AgentId == count.AgentId && a.Status == WalletAdjustmentStatus.Approved)
                    .Select(a => a.Amount).ToListAsync(cancellationToken);
                var expected = opening.Details.Where(d => comparedIds.Contains(d.WalletId)).Sum(d => d.Amount) - adjustments.Sum();
                var actual = compared.Sum(w => w.Amount);
                if (expected != actual)
                {
                    if (string.IsNullOrWhiteSpace(count.Explanation))
                        throw new BankingRuleException("Closing differs from the expected amount. Provide an explanation for approval.");
                    if (!await db.Discrepancies.AnyAsync(d => d.CashCountId == count.Id &&
                            d.Status == DiscrepancyStatus.PendingReview, cancellationToken))
                        db.Discrepancies.Add(new Discrepancy
                        {
                            CashSessionId = session.Id, CashCountId = count.Id, Status = DiscrepancyStatus.PendingReview,
                            ExpectedAmount = expected, ActualAmount = actual, Variance = actual - expected,
                            Explanation = count.Explanation, ExplainedByUserId = count.AgentId,
                            ExplainedAt = now, CreatedAt = now
                        });
                }
            }

            long? vaultTransactionId = null;
            if (vaultAmount > 0)
            {
                var result = isOpening
                    ? await vaultService.WithdrawForSessionAsync(session.Id, session.BranchId, vaultAmount, userId,
                        ensureTransaction: false, cancellationToken)
                    : await vaultService.DepositForSessionAsync(session.Id, session.BranchId, vaultAmount, userId,
                        ensureTransaction: false, cancellationToken);
                if (!result.Success || !result.TransactionId.HasValue)
                    throw new BankingRuleException(result.ErrorMessage ?? "The internal float transfer was not recorded.");
                vaultTransactionId = result.TransactionId.Value;
            }

            foreach (var detail in count.Details)
            {
                detail.Wallet!.Balance = isOpening ? detail.Amount : 0m;
                detail.Wallet.UpdatedAt = now;
            }
            count.Status = isOpening ? CashCountStatus.Approved : CashCountStatus.PendingApproval;
            if (isOpening)
            {
                count.ApprovedAt = now;
                count.ApprovedByUserId = userId;
            }
            else
            {
                count.SubmittedAt ??= now;
            }
            db.SessionFloatSettlements.Add(new SessionFloatSettlement
            {
                SnapshotId = snapshot.Id, CashCountId = count.Id, Stage = stage,
                VaultAmount = vaultAmount, ReportedAmount = count.TotalAmount, BankVariance = bankVariance,
                VaultTransactionId = vaultTransactionId, PostedByUserId = userId, PostedAt = now
            });
            db.CashCountAuditLogs.Add(new CashCountAuditLog
            {
                CashCountId = count.Id, CashSessionId = session.Id, AgentId = count.AgentId,
                IsOpening = isOpening, Action = isOpening ? CashCountAuditAction.Approved : CashCountAuditAction.Submitted,
                PreviousStatus = previousStatus, NewStatus = count.Status, TotalAmount = count.TotalAmount,
                PerformedByUserId = userId, PerformedAt = now,
                Notes = isOpening ? "Opening allocation recorded with separately tracked bank observations."
                    : "Operational closing settled once. Financial approval is awaiting bank reconciliation."
            });
            if (!isOpening)
            {
                // Persist this settlement first so the shared completion check sees it.
                await db.SaveChangesAsync(cancellationToken);
                await SessionCompletion.CloseIfCompleteAsync(db, session, now, cancellationToken);
                await WorkflowNotifications.AddForBranchAsync(db, session.BranchId, "Closing count pending approval",
                    $"A submitted closing for {count.CountDate:yyyy-MM-dd} is awaiting review and approval.",
                    NotificationType.CountPendingApproval, $"/closing-approvals/{count.Id}", now,
                    includeSupervisors: true, cancellationToken: cancellationToken);
            }
            else if (count.Agent != null)
            {
                WorkflowNotifications.AddForUser(db, count.Agent.UserId, "Opening count approved",
                    "Your opening allocation is approved. You may record this session's transactions.",
                    NotificationType.CountApproved, "/cashcount", now);
            }

            await db.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return new SessionSettlementResult(count.Id, session.Id, snapshot.Id, false);
        }
        catch
        {
            await BankingConcurrency.RollbackAfterConflictAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<List<CountedWalletSnapshot>> ReadCountWalletsAsync(
        CashCount count, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (count.Details.Count == 0 || count.Details.Select(d => d.WalletId).Distinct().Count() != count.Details.Count)
            throw new BankingRuleException("The count must contain distinct wallet observations.");
        var bindings = await db.BankAccounts.AsNoTracking()
            .Where(a => a.BranchId == count.CashSession!.BranchId && a.Wallet!.AgentId == count.AgentId)
            .ToDictionaryAsync(a => a.WalletId, a => a.Id, cancellationToken);
        var result = new List<CountedWalletSnapshot>();
        foreach (var detail in count.Details)
        {
            var wallet = detail.Wallet;
            if (wallet == null || wallet.AgentId != count.AgentId || wallet.WalletType == null)
                throw new UnauthorizedAccessException("Every counted wallet must belong to this agent.");
            if ((!detail.ObservedAt.HasValue && count.CashSession!.WorkflowVersion != SessionWorkflowVersion.Legacy) || detail.ObservedAt > now)
                throw new BankingRuleException("Each wallet requires a valid, non-future balance observation time.");
            if (decimal.Round(detail.Amount, 2) != detail.Amount ||
                (wallet.WalletType.Type == WalletTypeEnum.Cash && detail.Amount < 0))
                throw new BankingRuleException("Cash cannot be negative and wallet amounts must use at most two decimal places.");
            result.Add(new CountedWalletSnapshot(wallet.Id, wallet.Name, wallet.WalletType.Type, wallet.Currency,
                detail.Amount, (detail.ObservedAt ?? count.SubmittedAt ?? now).ToUniversalTime(),
                bindings.TryGetValue(wallet.Id, out var accountId) ? accountId : null));
        }
        if (result.Select(w => w.Currency).Distinct().Count() != 1)
            throw new BankingRuleException("A float allocation cannot combine different currencies.");
        return result;
    }

    private async Task<decimal> RecordOpeningBridgesAsync(
        CashCount count, CashCountSnapshot snapshot, IReadOnlyList<CountedWalletSnapshot> wallets,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var previous = await db.CashCounts.AsNoTracking()
            .Include(c => c.Details)
            .Where(c => c.AgentId == count.AgentId && !c.IsOpening && c.CountDate < count.CountDate &&
                c.CashSession!.BranchId == count.CashSession!.BranchId &&
                ((c.CashSession!.WorkflowVersion == SessionWorkflowVersion.BankReconciliation &&
                  c.CashSession.OperationalClosedAt != null) ||
                 (c.CashSession!.WorkflowVersion == SessionWorkflowVersion.Legacy &&
                  (c.Status == CashCountStatus.Approved ||
                   (c.Status == CashCountStatus.PendingApproval && db.SessionFloatSettlements.Any(s =>
                       s.CashCountId == c.Id && s.Stage == FloatSettlementStage.Closing))))))
            .OrderByDescending(c => c.CountDate).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous == null)
            return 0m;
        var previousSnapshot = await db.CashCountSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.CashCountId == previous.Id, cancellationToken);
        var priorWallets = previousSnapshot == null ? null : CountSnapshotCodec.Deserialize(previousSnapshot.WalletsJson);
        var variance = 0m;
        foreach (var wallet in wallets.Where(w => w.BankAccountId.HasValue))
        {
            var prior = priorWallets?.SingleOrDefault(w => w.WalletId == wallet.WalletId);
            var legacyDetail = previous.Details.SingleOrDefault(d => d.WalletId == wallet.WalletId);
            if (prior == null && legacyDetail == null)
                throw new BankingRuleException("The bank account has no prior closing observation. Its opening allocation needs admin setup.");
            var priorAmount = prior?.Amount ?? legacyDetail!.Amount;
            var priorObservedAt = prior?.ObservedAt ?? legacyDetail?.ObservedAt;
            if (priorObservedAt.HasValue && wallet.ObservedAt <= priorObservedAt)
                throw new BankingRuleException("The opening observation must follow the prior closing observation.");
            variance += wallet.Amount - priorAmount;
            db.OpeningBankVariances.Add(new OpeningBankVariance
            {
                BankAccountId = wallet.BankAccountId!.Value, OpeningSnapshotId = snapshot.Id,
                PreviousClosingSnapshotId = previousSnapshot?.Id, PreviousClosingCountId = previous.Id,
                PreviousBankBalance = priorAmount, ObservedBankBalance = wallet.Amount,
                PreviousObservedAt = priorObservedAt?.ToUniversalTime(), ObservedAt = wallet.ObservedAt,
                RecordedAt = now
            });
        }
        return variance;
    }

    private async Task EnsureNoLaterMovementsAsync(
        CashCount count, IReadOnlyList<CountedWalletSnapshot> wallets, CancellationToken cancellationToken)
    {
        var cutoffs = wallets.ToDictionary(w => w.WalletId, w => w.ObservedAt);
        var movements = await db.CustomerMovements.AsNoTracking()
            .Where(m => m.CashSessionId == count.CashSessionId && m.CustomerTransaction!.AgentId == count.AgentId &&
                        !db.CustomerMovements.Any(next => next.SupersedesMovementId == m.Id))
            .Select(m => new
            {
                m.OccurredAt, m.CashEffect, m.BankEffect, m.CustomerTransaction!.CashWalletId,
                BankWalletId = m.CustomerTransaction.BankAccount!.WalletId
            }).ToListAsync(cancellationToken);
        if (movements.Any(m =>
                (m.CashEffect != 0 && (!cutoffs.TryGetValue(m.CashWalletId, out var cashCutoff) || m.OccurredAt > cashCutoff)) ||
                (m.BankEffect != 0 && (!cutoffs.TryGetValue(m.BankWalletId, out var bankCutoff) || m.OccurredAt > bankCutoff))))
            throw new BankingRuleException("Transactions occurred after a counted balance. Refresh the closing observations before submitting.");
        var runs = await db.BankRuns.AsNoTracking()
            .Where(r => r.CashSessionId == count.CashSessionId && r.AgentId == count.AgentId)
            .Select(r => new { r.FromWalletId, r.ToWalletId, OccurredAt = r.OccurredAt ?? r.CreatedAt })
            .ToListAsync(cancellationToken);
        if (runs.Any(r => !cutoffs.TryGetValue(r.FromWalletId, out var from) ||
                          !cutoffs.TryGetValue(r.ToWalletId, out var to) || r.OccurredAt > from || r.OccurredAt > to))
            throw new BankingRuleException("A bank run occurred after a counted balance. Refresh the closing observations.");
        var adjustments = await db.WalletAdjustments.AsNoTracking()
            .Where(a => a.CashSessionId == count.CashSessionId && a.AgentId == count.AgentId &&
                        a.Status != WalletAdjustmentStatus.Rejected)
            .Select(a => new { a.WalletId, OccurredAt = a.OccurredAt ?? a.CreatedAt }).ToListAsync(cancellationToken);
        if (adjustments.Any(a => !cutoffs.TryGetValue(a.WalletId, out var cutoff) || a.OccurredAt > cutoff))
            throw new BankingRuleException("A wallet adjustment occurred after a counted balance. Refresh the closing observations.");
    }

}
