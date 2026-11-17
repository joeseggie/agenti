using System.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.ClosingReconciliation;

public sealed class ClosingReconciliationService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock,
    ISessionBalanceService balances, ISessionSettlementService settlements) : IClosingReconciliationService
{
    public async Task<ClosingReviewDto> GetAsync(
        string userId, long closingCountId, CancellationToken cancellationToken = default)
    {
        var (count, _) = await RequireClosingAsync(userId, closingCountId, cancellationToken);
        var wallets = await ReadWalletsAsync(count, cancellationToken);
        var accounts = await AccountsAsync(count, cancellationToken);
        var statements = await db.DailyBankStatements.AsNoTracking()
            .Where(s => s.StatementDate == count.CountDate && accounts.Select(a => a.Id).Contains(s.BankAccountId))
            .OrderByDescending(s => s.Revision).ToListAsync(cancellationToken);
        var run = await db.ClosingReconciliationRuns.AsNoTracking().Include(r => r.Wallets)
            .SingleOrDefaultAsync(r => r.ClosingCountId == count.Id, cancellationToken);
        var approval = await db.ClosingApprovalDecisions.AsNoTracking()
            .SingleOrDefaultAsync(a => a.ClosingCountId == count.Id, cancellationToken);
        return new ClosingReviewDto(count.Id, count.CashSessionId, count.Agent!.User!.FullName,
            count.CountDate, count.Status, wallets.Select(w =>
            {
                var accountId = Binding(w, accounts)?.Id;
                return new ClosingWalletDto(w.Id, w.Name, w.Type, w.Currency, w.Amount, accountId,
                    statements.FirstOrDefault(s => s.BankAccountId == accountId)?.Id);
            }).ToList(),
            run == null ? null : new ClosingReportDto(run.Id, run.Succeeded, run.PerformedByUserId, run.PerformedAt,
                run.Wallets.OrderBy(w => w.WalletName).Select(w => new ClosingWalletResultDto(
                    w.WalletName, w.BankName, w.AccountIdentifier, w.Currency, w.StatementId,
                    statements.Single(s => s.Id == w.StatementId).Revision, w.OpeningBalance,
                    w.TotalDebits, w.TotalCredits, w.ExpectedClosing, w.SubmittedClosing,
                    w.StatementOpening, w.StatementClosing, w.Difference, w.Succeeded, w.Explanation)).ToList()),
            approval == null ? null : new ClosingApprovalDto(
                approval.IsException, approval.Reason, approval.ApprovedByUserId, approval.ApprovedAt));
    }

    public async Task<ClosingReviewDto> ReconcileAsync(
        string userId, long closingCountId, CancellationToken cancellationToken = default)
    {
        (await access.GetActorAsync(userId, cancellationToken)).RequireAdmin();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var (count, _) = await RequireClosingAsync(userId, closingCountId, cancellationToken);
            if (await db.ClosingReconciliationRuns.AnyAsync(r => r.ClosingCountId == count.Id, cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return await GetAsync(userId, count.Id, cancellationToken);
            }
            if (count.Status != CashCountStatus.PendingApproval)
                throw new BankingRuleException("Only a submitted closing awaiting approval can be reconciled.");
            var opening = await OpeningAsync(count, cancellationToken);
            var closingWallets = await ReadWalletsAsync(count, cancellationToken);
            var openingWallets = await ReadWalletsAsync(opening, cancellationToken);
            if (!closingWallets.Select(w => w.Id).Order().SequenceEqual(openingWallets.Select(w => w.Id).Order()))
                throw new BankingRuleException("The approved opening and submitted closing must contain the same wallets.");
            var banks = closingWallets.Where(w => w.Type == WalletTypeEnum.Bank).ToList();
            if (banks.Count == 0)
                throw new BankingRuleException("Bank reconciliation is not required for this closing.");
            var accounts = await AccountsAsync(count, cancellationToken);
            foreach (var account in accounts)
                await db.Entry(account).ReloadAsync(cancellationToken);
            var statements = await db.DailyBankStatements.AsNoTracking().Include(s => s.Rows)
                .Where(s => s.StatementDate == count.CountDate && accounts.Select(a => a.Id).Contains(s.BankAccountId))
                .OrderByDescending(s => s.Revision).ToListAsync(cancellationToken);
            var missing = new List<string>();
            foreach (var wallet in banks)
            {
                var account = Binding(wallet, accounts);
                if (account == null)
                    missing.Add($"{wallet.Name}: bank account binding is missing");
                else if (!statements.Any(s => s.BankAccountId == account.Id))
                    missing.Add($"{wallet.Name}: statement for {count.CountDate:yyyy-MM-dd} is missing");
            }
            if (missing.Count > 0)
                throw new BankingRuleException("Reconciliation blocked. " + string.Join("; ", missing) + ".");

            var run = new ClosingReconciliationRun
            {
                ClosingCountId = count.Id, OpeningCountId = opening.Id, BusinessDate = count.CountDate,
                InputHash = InputHash(opening, count, openingWallets, closingWallets),
                PerformedByUserId = userId, PerformedAt = clock.UtcNow
            };
            foreach (var wallet in banks)
            {
                var account = Binding(wallet, accounts)!;
                var statement = statements.First(s => s.BankAccountId == account.Id);
                var start = openingWallets.Single(w => w.Id == wallet.Id);
                if (start.Type != WalletTypeEnum.Bank || start.Currency != wallet.Currency ||
                    statement.Currency != wallet.Currency || account.Currency != wallet.Currency)
                    throw new BankingRuleException($"{wallet.Name}: opening, closing and statement currencies/types must agree.");
                var expected = start.Amount + statement.TotalCredits - statement.TotalDebits;
                const decimal maximum = 9_999_999_999_999_999.99m;
                if (Math.Abs(expected) > maximum || Math.Abs(wallet.Amount - expected) > maximum)
                    throw new BankingRuleException($"{wallet.Name}: calculated closing or difference exceeds the supported monetary range.");
                var explanation = new List<string>();
                if (statement.OpeningBalance != start.Amount)
                    explanation.Add("The statement opening differs from the approved opening.");
                if (expected != wallet.Amount)
                    explanation.Add("Approved opening plus credits minus debits differs from the submitted closing.");
                if (statement.ClosingBalance != wallet.Amount)
                    explanation.Add("The statement ending differs from the submitted closing.");
                var rolling = statement.OpeningBalance;
                foreach (var row in statement.Rows.OrderBy(r => r.Sequence))
                {
                    rolling += row.Credit - row.Debit;
                    if (row.TransactionDate != count.CountDate || rolling != row.RunningBalance)
                        throw new BankingRuleException($"{wallet.Name}: stored statement rows are inconsistent; investigation is required.");
                }
                if (statement.Rows.Count == 0 || rolling != statement.ClosingBalance ||
                    statement.Rows.Sum(r => r.Debit) != statement.TotalDebits ||
                    statement.Rows.Sum(r => r.Credit) != statement.TotalCredits)
                    throw new BankingRuleException($"{wallet.Name}: accepted statement data is incomplete.");
                run.Wallets.Add(new ClosingWalletResult
                {
                    WalletId = wallet.Id, BankAccountId = account.Id, StatementId = statement.Id,
                    WalletName = wallet.Name, BankName = account.BankName, AccountIdentifier = statement.AccountIdentifier,
                    Currency = wallet.Currency, OpeningBalance = start.Amount, TotalDebits = statement.TotalDebits,
                    TotalCredits = statement.TotalCredits, ExpectedClosing = expected, SubmittedClosing = wallet.Amount,
                    StatementOpening = statement.OpeningBalance, StatementClosing = statement.ClosingBalance,
                    Difference = wallet.Amount - expected, Succeeded = explanation.Count == 0,
                    Explanation = explanation.Count == 0 ? "Daily opening, statement movements and closing agree." : string.Join(" ", explanation)
                });
                // Upload and reconciliation contend on the same account version, freezing the selected revision.
                account.Version++;
            }
            run.Succeeded = run.Wallets.All(w => w.Succeeded);
            count.CashSession!.Version++;
            db.ClosingReconciliationRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(userId, count.Id, cancellationToken);
        }
        catch
        {
            await BankingConcurrency.RollbackAfterConflictAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ClosingReviewDto> ApproveAsync(
        string userId, long closingCountId, bool exception, string? reason = null,
        bool acknowledgeFailures = false, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var (count, actor) = await RequireClosingAsync(userId, closingCountId, cancellationToken);
            if (count.Agent!.UserId == actor.UserId)
                throw new BankingRuleException("You cannot approve your own closing.");
            if (exception)
            {
                actor.RequireAdmin();
                if (!acknowledgeFailures || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 2000)
                    throw new BankingRuleException("Acknowledge the failed wallets and provide an exception reason between 10 and 2,000 characters.");
            }
            var run = await db.ClosingReconciliationRuns.AsNoTracking()
                .SingleOrDefaultAsync(r => r.ClosingCountId == count.Id, cancellationToken)
                ?? throw new BankingRuleException("Reconcile all Bank wallets before approving this closing.");
            if (exception == run.Succeeded)
                throw new BankingRuleException(run.Succeeded
                    ? "This reconciliation succeeded. Use ordinary closing approval."
                    : "Reconciliation failed. Only an Admin may use the separate exception approval.");
            var existing = await db.ClosingApprovalDecisions.AsNoTracking()
                .SingleOrDefaultAsync(a => a.ClosingCountId == count.Id, cancellationToken);
            if (existing != null)
            {
                if (existing.IsException != exception || existing.Reason != (exception ? reason!.Trim() : null))
                    throw new BankingConflictException("This closing already has a different recorded approval decision.");
                await transaction.CommitAsync(cancellationToken);
                return await GetAsync(userId, count.Id, cancellationToken);
            }
            if (count.Status != CashCountStatus.PendingApproval)
                throw new BankingRuleException("This closing is no longer awaiting approval.");
            var opening = await OpeningAsync(count, cancellationToken);
            var wallets = await ReadWalletsAsync(count, cancellationToken);
            if (run.OpeningCountId != opening.Id || run.InputHash !=
                InputHash(opening, count, await ReadWalletsAsync(opening, cancellationToken), wallets))
                throw new BankingRuleException("The recorded opening/closing inputs changed. Approval requires investigation; rerunning is not supported.");
            if (await db.WalletAdjustments.AnyAsync(a => a.CashSessionId == count.CashSessionId &&
                    a.AgentId == count.AgentId && a.Status == WalletAdjustmentStatus.Pending, cancellationToken))
                throw new BankingRuleException("Review pending wallet adjustments before closing approval.");
            var snapshot = await db.CashCountSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(s => s.CashCountId == count.Id, cancellationToken);
            if (count.CashSession!.WorkflowVersion == SessionWorkflowVersion.BankReconciliation && snapshot != null)
            {
                var positions = await balances.GetPositionsAtSnapshotAsync(snapshot.Id, cancellationToken: cancellationToken);
                if (wallets.Where(w => w.Type != WalletTypeEnum.Bank)
                    .Any(w => positions.Single(p => p.WalletId == w.Id).ExpectedBalance != w.Amount))
                    throw new BankingRuleException("Cash or Mobile Money differs from recorded movements. A bank reconciliation exception cannot override this safeguard.");
            }
            if (!await db.SessionFloatSettlements.AnyAsync(s => s.CashCountId == count.Id &&
                    s.Stage == FloatSettlementStage.Closing, cancellationToken))
            {
                if (count.CashSession.WorkflowVersion != SessionWorkflowVersion.Legacy)
                    throw new BankingRuleException("The operational closing settlement is missing; investigate before approval.");
                // Pre-cutover legacy submissions had not settled yet. Complete only that first settlement.
                await settlements.SubmitClosingAsync(userId, count.Id, cancellationToken);
            }
            var now = clock.UtcNow;
            var discrepancy = await db.Discrepancies.SingleOrDefaultAsync(d => d.CashCountId == count.Id &&
                d.Status == DiscrepancyStatus.PendingReview, cancellationToken);
            if (discrepancy != null)
            {
                if (string.IsNullOrWhiteSpace(count.Explanation))
                    throw new BankingRuleException("A closing discrepancy requires the agent's explanation.");
                discrepancy.Status = DiscrepancyStatus.Approved;
                discrepancy.ApprovedByUserId = actor.UserId;
                discrepancy.ApprovedAt = now;
                discrepancy.ApprovalNotes = exception ? reason!.Trim() : "Reviewed with closing approval.";
                if (discrepancy.Variance > 0 && count.CashSession.WorkflowVersion == SessionWorkflowVersion.Legacy)
                {
                    var vault = await db.Vaults.SingleAsync(v => v.BranchId == actor.BranchId, cancellationToken);
                    db.VaultTransactions.Add(new VaultTransaction
                    {
                        VaultId = vault.Id, CashSessionId = count.CashSessionId, Amount = discrepancy.Variance,
                        Type = VaultTransactionType.SurplusDeposit, Status = VaultTransactionStatus.Completed,
                        BalanceAfter = vault.CurrentBalance, CreatedAt = now, CreatedByUserId = actor.UserId,
                        Notes = $"Audit-only surplus for closing count {count.Id}; operational funds were already returned."
                    });
                }
            }
            count.Status = CashCountStatus.Approved;
            count.ApprovedAt = now;
            count.ApprovedByUserId = actor.UserId;
            count.CashSession.Version++;
            db.ClosingApprovalDecisions.Add(new ClosingApprovalDecision
            {
                ClosingCountId = count.Id, RunId = run.Id, IsException = exception,
                Reason = exception ? reason!.Trim() : null, ApprovedByUserId = actor.UserId, ApprovedAt = now
            });
            db.CashCountAuditLogs.Add(new CashCountAuditLog
            {
                CashCountId = count.Id, CashSessionId = count.CashSessionId, AgentId = count.AgentId,
                Action = CashCountAuditAction.Approved, PreviousStatus = CashCountStatus.PendingApproval,
                NewStatus = CashCountStatus.Approved, TotalAmount = count.TotalAmount,
                PerformedByUserId = actor.UserId, PerformedAt = now,
                Notes = exception ? $"Approved with reconciliation exception. Failed report #{run.Id} retained. {reason!.Trim()}"
                    : $"Approved after successful daily reconciliation #{run.Id}; not exact-time certification."
            });
            WorkflowNotifications.AddForUser(db, count.Agent.UserId, "Closing count approved",
                "Your submitted closing count has been approved.", NotificationType.CountApproved, "/cashcount", now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(userId, count.Id, cancellationToken);
        }
        catch
        {
            await BankingConcurrency.RollbackAfterConflictAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<(CashCount Count, BankingActor Actor)> RequireClosingAsync(
        string userId, long countId, CancellationToken cancellationToken)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (actor.Role is not (UserRole.Admin or UserRole.Supervisor))
            throw new UnauthorizedAccessException("Only branch administrators and supervisors may review closings.");
        var count = await Counts().SingleOrDefaultAsync(c => c.Id == countId && !c.IsOpening &&
            c.CashSession!.BranchId == actor.BranchId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The closing is not available in this branch.");
        await db.Entry(count).ReloadAsync(cancellationToken);
        await db.Entry(count.CashSession!).ReloadAsync(cancellationToken);
        return (count, actor);
    }

    private async Task<CashCount> OpeningAsync(CashCount closing, CancellationToken cancellationToken) =>
        await Counts().SingleOrDefaultAsync(c => c.IsOpening && c.AgentId == closing.AgentId &&
            c.CashSessionId == closing.CashSessionId && c.CountDate == closing.CountDate &&
            c.Status == CashCountStatus.Approved, cancellationToken)
        ?? throw new BankingRuleException("An approved opening for the same agent, session and business date is required.");

    private IQueryable<CashCount> Counts() => db.CashCounts.Include(c => c.Agent).ThenInclude(a => a!.User)
        .Include(c => c.CashSession).Include(c => c.Details).ThenInclude(d => d.Wallet).ThenInclude(w => w!.WalletType);

    private Task<List<BankAccount>> AccountsAsync(CashCount count, CancellationToken cancellationToken) =>
        db.BankAccounts.Where(a => a.BranchId == count.CashSession!.BranchId && a.Wallet!.AgentId == count.AgentId)
            .OrderBy(a => a.Id).ToListAsync(cancellationToken);

    private static BankAccount? Binding(WalletObservation wallet, List<BankAccount> accounts) =>
        wallet.Type != WalletTypeEnum.Bank ? null :
        accounts.SingleOrDefault(a => a.WalletId == wallet.Id &&
            (!wallet.BankAccountId.HasValue || a.Id == wallet.BankAccountId));

    private async Task<List<WalletObservation>> ReadWalletsAsync(CashCount count, CancellationToken cancellationToken)
    {
        var snapshot = await db.CashCountSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.CashCountId == count.Id, cancellationToken);
        var wallets = snapshot == null
            ? count.Details.Select(d => new WalletObservation(d.WalletId, d.Wallet!.Name,
                d.Wallet.WalletType!.Type, d.Wallet.Currency, d.Amount, null)).ToList()
            : CountSnapshotCodec.Deserialize(snapshot.WalletsJson).Select(w =>
                new WalletObservation(w.WalletId, w.WalletName, w.WalletType, w.Currency, w.Amount, w.BankAccountId)).ToList();
        if (wallets.Count == 0 || wallets.Select(w => w.Id).Distinct().Count() != wallets.Count)
            throw new BankingRuleException("The cash count must contain distinct recorded wallet balances.");
        return wallets.OrderBy(w => w.Id).ToList();
    }

    private static string InputHash(CashCount opening, CashCount closing,
        List<WalletObservation> openingWallets, List<WalletObservation> closingWallets) =>
        BankingRequestIdentity.Hash(new { OpeningId = opening.Id, ClosingId = closing.Id, closing.CountDate,
            Opening = openingWallets, Closing = closingWallets });

    private sealed record WalletObservation(
        long Id, string Name, WalletTypeEnum Type, string Currency, decimal Amount, long? BankAccountId);
}
