using System.Data;
using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService
{
    public Task<BankReconciliationReviewDto> ClassifyAsync(
        string userId, long reviewId, BankClassificationForm form, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, reviewId, form.ExpectedReviewVersion, async (review, facts) =>
        {
            RequireReviewReason(form.Reason);
            if (!Enum.IsDefined(form.Classification))
                throw new BankingRuleException("Choose a supported bank-only classification.");
            await RequireEvidenceAsync(form.EvidenceFileId, review.BankAccountId, cancellationToken);
            var bank = await db.BankTransactions.Include(t => t.AcceptedRow).SingleOrDefaultAsync(
                t => t.Id == form.BankTransactionId && t.BankAccountId == review.BankAccountId, cancellationToken)
                ?? throw new BankingRuleException("Bank occurrence not found.");
            if (await db.BankReconciliationMatches.AnyAsync(m => m.BankTransactionId == bank.Id && m.IsActive, cancellationToken))
                throw new BankingRuleException("A matched bank occurrence cannot also be a bank-only adjustment.");
            if (form.Classification == BankTransactionClassification.Fee && bank.AcceptedRow!.SignedAmount >= 0 ||
                form.Classification == BankTransactionClassification.Commission && bank.AcceptedRow!.SignedAmount <= 0 ||
                form.Classification == BankTransactionClassification.Interest && bank.AcceptedRow!.SignedAmount <= 0)
                throw new BankingRuleException("The selected classification contradicts the debit/credit direction.");
            if (form.RelatedCustomerTransactionId.HasValue)
            {
                if (!facts.Customers.Any(c => c.Id == form.RelatedCustomerTransactionId))
                    throw new BankingRuleException("Select a customer case in this account review.");
                if (form.Classification == BankTransactionClassification.BankReversal)
                    throw new BankingRuleException("Link a customer bank return to its expected case settlement so it cannot be counted twice.");
            }
            var previous = bank.Classification;
            bank.Classification = form.Classification;
            bank.ClassificationReason = form.Reason.Trim();
            bank.RelatedCustomerTransactionId = form.RelatedCustomerTransactionId;
            bank.Version++;
            AddDecision(review, userId, ReconciliationDecisionKind.Classified, form.Reason,
                new { Previous = previous, form.Classification, form.EvidenceFileId, form.RelatedCustomerTransactionId }, bank.Id);
        }, cancellationToken);

    public Task<BankReconciliationReviewDto> RecordExceptionAsync(
        string userId, long reviewId, BankExceptionForm form, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, reviewId, form.ExpectedReviewVersion, async (review, facts) =>
        {
            RequireReviewReason(form.Explanation);
            if (!Enum.IsDefined(form.Kind) || form.Amount <= 0 ||
                decimal.Round(form.Amount, 2) != form.Amount || form.Amount > 9_999_999_999_999_999.99m)
                throw new BankingRuleException("Choose an exception type and a valid affected amount.");
            if (!facts.Sessions.Any(s => s.Id == form.CashSessionId))
                throw new BankingRuleException("Select a session within this review.");
            await RequireEvidenceAsync(form.EvidenceFileId, review.BankAccountId, cancellationToken);
            if (form.Kind != BankExceptionKind.UnexplainedMovement && !form.BankExplanationVerified)
                throw new BankingRuleException("Verify and document the bank's explanation before treating this as an approval exception.");
            if (form.Kind == BankExceptionKind.PostingDelay && !form.ReflectedInClosingBalance.HasValue)
                throw new BankingRuleException("State whether the reported closing balance already includes the delayed posting.");
            var sources = new[] { form.CustomerMovementId, form.BankRunId, form.WalletAdjustmentId }.Count(id => id.HasValue);
            if (sources > 1)
                throw new BankingRuleException("An exception must identify a single register source.");
            if (form.CustomerMovementId.HasValue)
            {
                var movement = await db.CustomerMovements.AsNoTracking().Include(m => m.CustomerTransaction)
                    .SingleOrDefaultAsync(m => m.Id == form.CustomerMovementId &&
                        m.CustomerTransaction!.BankAccountId == review.BankAccountId, cancellationToken)
                    ?? throw new BankingRuleException("Customer movement not found in this account.");
                var affected = movement.BankEffect == 0 ? movement.Amount : Math.Abs(movement.BankEffect);
                if (form.Amount > affected)
                    throw new BankingRuleException("The exception exceeds the referenced movement.");
                form.CustomerTransactionId = movement.CustomerTransactionId;
            }
            else if (form.BankRunId.HasValue || form.WalletAdjustmentId.HasValue)
            {
                var leg = facts.Legs.SingleOrDefault(l =>
                    (l.Kind == BankLegKind.BankRun && l.Id == form.BankRunId) ||
                    (l.Kind == BankLegKind.WalletAdjustment && l.Id == form.WalletAdjustmentId))
                    ?? throw new BankingRuleException("Register source not found in this account review.");
                if (form.Amount != Math.Abs(leg.SignedAmount))
                    throw new BankingRuleException("The exception amount must identify the referenced bank leg exactly.");
            }
            if (form.CustomerTransactionId.HasValue &&
                !facts.Customers.Any(c => c.Id == form.CustomerTransactionId))
                throw new BankingRuleException("Customer case not found in this account review.");
            if (form.BankTransactionId.HasValue)
            {
                var bank = facts.Banks.SingleOrDefault(b => b.Id == form.BankTransactionId)
                    ?? throw new BankingRuleException("Bank occurrence not found in this review.");
                if (form.Amount != Math.Abs(bank.AcceptedRow!.SignedAmount))
                    throw new BankingRuleException("The bank exception must cover the selected occurrence exactly.");
            }
            if (form.PendingTransactionId.HasValue && !await db.PendingTransactions.AnyAsync(p =>
                    p.Id == form.PendingTransactionId && p.CashSessionId == form.CashSessionId &&
                    p.AgentId == facts.Account.Wallet!.AgentId, cancellationToken))
                throw new BankingRuleException("Pending case not found for this session and agent.");
            if (sources == 0 && !form.BankTransactionId.HasValue && !form.CustomerTransactionId.HasValue && !form.PendingTransactionId.HasValue)
                throw new BankingRuleException("Tie the exception to an actual bank occurrence, customer case, or pending operation.");
            if (form.Kind == BankExceptionKind.PostingDelay && sources == 0 && !form.CustomerTransactionId.HasValue)
                throw new BankingRuleException("A posting delay needs a specific expected register movement or customer settlement.");
            var exception = new BankReconciliationException
            {
                BankAccountId = review.BankAccountId, CashSessionId = form.CashSessionId,
                Kind = form.Kind, Amount = form.Amount, CustomerMovementId = form.CustomerMovementId,
                BankRunId = form.BankRunId, WalletAdjustmentId = form.WalletAdjustmentId,
                CustomerTransactionId = form.CustomerTransactionId, PendingTransactionId = form.PendingTransactionId,
                BankTransactionId = form.BankTransactionId, EvidenceFileId = form.EvidenceFileId,
                BankExplanationVerified = form.BankExplanationVerified,
                ReflectedInClosingBalance = form.ReflectedInClosingBalance,
                Explanation = form.Explanation.Trim(), RecordedByUserId = userId, RecordedAt = clock.UtcNow
            };
            db.BankReconciliationExceptions.Add(exception);
            await db.SaveChangesAsync(cancellationToken);
            AddDecision(review, userId, ReconciliationDecisionKind.ExceptionRecorded, form.Explanation,
                new { ExceptionId = exception.Id, form.Kind, form.Amount, form.EvidenceFileId, form.BankExplanationVerified },
                form.BankTransactionId, form.CashSessionId);
        }, cancellationToken);

    public Task<BankReconciliationReviewDto> ResolveExceptionAsync(
        string userId, long reviewId, long exceptionId, long evidenceBankTransactionId, int expectedVersion,
        string reason, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, reviewId, expectedVersion, async (review, facts) =>
        {
            RequireReviewReason(reason);
            var exception = await db.BankReconciliationExceptions.SingleOrDefaultAsync(e =>
                e.Id == exceptionId && e.BankAccountId == review.BankAccountId, cancellationToken)
                ?? throw new BankingRuleException("Bank exception not found.");
            if (exception.IsResolved)
                return;
            var bank = await BankQuery(review.BankAccountId).SingleOrDefaultAsync(b => b.Id == evidenceBankTransactionId, cancellationToken)
                ?? throw new BankingRuleException("Resolution requires a bank occurrence in this account.");
            var match = await db.BankReconciliationMatches.AsNoTracking()
                .SingleOrDefaultAsync(m => m.BankTransactionId == bank.Id && m.IsActive, cancellationToken);
            if (exception.Kind == BankExceptionKind.BankFailure && exception.CustomerTransactionId.HasValue)
            {
                var customer = await db.CustomerTransactions.AsNoTracking().Include(c => c.Movements)
                    .SingleAsync(c => c.Id == exception.CustomerTransactionId, cancellationToken);
                var active = Active(customer.Movements);
                var primary = Primary(active) ?? throw new BankingRuleException("The original customer record is missing.");
                var kind = primary.TransactionKind ?? customer.Kind;
                var exposure = active.Sum(m => m.BankEffect) -
                    (kind == CustomerTransactionKind.Deposit ? -active.Sum(m => m.CustomerSettledAmount) : active.Sum(m => m.CustomerSettledAmount));
                if (match?.CustomerMovementId == null ||
                    !active.Any(m => m.Id == match.CustomerMovementId && m.Kind == CustomerMovementKind.BankReversal) || exposure != 0)
                    throw new BankingRuleException("Match the bank reversal and resolve the case's bank exposure before clearing this bank-failure flag.");
            }
            else if (exception.Kind == BankExceptionKind.PostingDelay)
            {
                if (match == null)
                    throw new BankingRuleException("Match the delayed bank posting before clearing its flag.");
                var source = facts.Legs.SingleOrDefault(l => Key(l) == Key(match));
                if (source == null || (!RefersTo(exception, source) &&
                    (!exception.CustomerTransactionId.HasValue || source.CustomerTransactionId != exception.CustomerTransactionId)))
                    throw new BankingRuleException("The matched bank occurrence does not resolve this delayed source.");
            }
            else if (!BankAccountedFor(bank, facts))
                throw new BankingRuleException("Investigate and match or classify the bank occurrence before resolving this exception.");
            exception.IsResolved = true;
            exception.Version++;
            AddDecision(review, userId, ReconciliationDecisionKind.ExceptionResolved, reason,
                new { ExceptionId = exception.Id, EvidenceBankTransactionId = bank.Id }, bank.Id, exception.CashSessionId);
        }, cancellationToken);

    public Task<BankReconciliationReviewDto> ReturnForCorrectionAsync(
        string userId, long reviewId, ClosingCorrectionForm form, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, reviewId, form.ExpectedReviewVersion, async (review, facts) =>
        {
            RequireReviewReason(form.Reason);
            var count = await db.CashCounts.Include(c => c.Agent).Include(c => c.CashSession)
                .SingleOrDefaultAsync(c => c.Id == form.CashCountId && !c.IsOpening &&
                    c.AgentId == facts.Account.Wallet!.AgentId && c.CashSession!.BranchId == facts.Account.BranchId, cancellationToken)
                ?? throw new BankingRuleException("Closing count not found for this account.");
            if (!facts.Sessions.Any(s => s.Id == count.CashSessionId))
                throw new BankingRuleException("The closing is outside this review interval.");
            if (form.MovementIds.Count == 0 && !form.AllowMissingTransactions)
                throw new BankingRuleException("Specify the entries to correct or permit missing transaction records.");
            var ids = form.MovementIds.Distinct().ToList();
            if (ids.Count > 500 || await db.CustomerMovements.CountAsync(m => ids.Contains(m.Id) &&
                    m.CashSessionId == count.CashSessionId && m.CustomerTransaction!.AgentId == count.AgentId &&
                    m.CustomerTransaction.BankAccountId == review.BankAccountId, cancellationToken) != ids.Count)
                throw new BankingRuleException("The correction scope contains unavailable or unrelated movements.");
            if (await db.CashCountCorrectionRequests.AnyAsync(r => r.CashCountId == count.Id && r.IsOpen, cancellationToken))
                throw new BankingRuleException("This closing already has an open correction request.");
            var request = new CashCountCorrectionRequest
            {
                CashCountId = count.Id, AgentId = count.AgentId, Reason = form.Reason.Trim(),
                AllowedMovementIdsJson = JsonSerializer.Serialize(ids), AllowMissingTransactions = form.AllowMissingTransactions,
                RequestedByUserId = userId, RequestedAt = clock.UtcNow
            };
            db.CashCountCorrectionRequests.Add(request);
            count.CashSession!.Version++;
            await db.SaveChangesAsync(cancellationToken);
            WorkflowNotifications.AddForUser(db, count.Agent!.UserId, "Closing correction requested",
                form.Reason.Trim(), NotificationType.CountRejected,
                $"/customer-transactions?correctionId={request.Id}", clock.UtcNow);
            AddDecision(review, userId, ReconciliationDecisionKind.ReturnedForCorrection, form.Reason,
                new { CorrectionRequestId = request.Id, MovementIds = ids, form.AllowMissingTransactions },
                sessionId: count.CashSessionId);
        }, cancellationToken);

    public async Task<BankReconciliationReviewDto> ApproveClosingAsync(
        string userId, long reviewId, long cashSessionId, Guid commandId, int expectedVersion,
        string basisHash, CancellationToken cancellationToken = default)
    {
        if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(basisHash))
            throw new BankingRuleException("An approval request identity and reviewed evidence fingerprint are required.");
        var accountId = await ReviewAccountIdAsync(reviewId, cancellationToken);
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var review = await db.BankReconciliationReviews.SingleAsync(r => r.Id == reviewId, cancellationToken);
            await db.Entry(review).ReloadAsync(cancellationToken);
            var replay = await db.BankReconciliationResults.AsNoTracking()
                .SingleOrDefaultAsync(r => r.ReviewedByUserId == userId && r.CommandId == commandId, cancellationToken);
            if (replay != null)
            {
                if (replay.ReviewId != reviewId || replay.CashSessionId != cashSessionId || replay.BasisHash != basisHash)
                    throw new BankingRuleException("This approval request identity was used for a different decision.");
                var repeated = await EvaluateAsync(review, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return repeated;
            }
            if (review.Version != expectedVersion)
                throw new BankingConflictException("This review changed. Refresh and verify the current evidence before approving.");
            var report = await EvaluateAsync(review, cancellationToken);
            var session = report.Sessions.SingleOrDefault(s => s.CashSessionId == cashSessionId)
                ?? throw new BankingRuleException("The session is not part of this review.");
            if (!session.CanApprove || !session.ClosingCountId.HasValue || !session.ClosingSnapshotId.HasValue)
                throw new BankingRuleException("Approval is held: " + string.Join(" ", session.BlockingReasons.Take(5)));
            if (session.BasisHash != basisHash)
                throw new BankingConflictException("The evidence changed after it was reviewed. Refresh before approving.");
            var count = await db.CashCounts.Include(c => c.Agent).SingleAsync(c => c.Id == session.ClosingCountId, cancellationToken);
            if (count.Agent!.UserId == userId)
                throw new UnauthorizedAccessException("You cannot certify your own closing.");
            var previousStatus = count.Status;
            if (count.Status is not (CashCountStatus.PendingApproval or CashCountStatus.Approved))
                throw new BankingRuleException("This closing is not awaiting financial certification.");
            var previous = await db.BankReconciliationResults.AsNoTracking()
                .Where(r => r.CashSessionId == cashSessionId && r.Review!.BankAccountId == accountId)
                .OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            count.Status = CashCountStatus.Approved;
            count.ApprovedAt ??= clock.UtcNow;
            count.ApprovedByUserId ??= userId;
            db.BankReconciliationResults.Add(new BankReconciliationResult
            {
                ReviewId = reviewId, CashSessionId = cashSessionId, SnapshotId = session.ClosingSnapshotId.Value,
                RegisterVersion = session.RegisterVersion, Status = session.Status, BasisHash = basisHash,
                CommandId = commandId, ReportJson = JsonSerializer.Serialize(session),
                SupersedesResultId = previous?.Id, ReviewedByUserId = userId, ReviewedAt = clock.UtcNow
            });
            db.CashCountAuditLogs.Add(new CashCountAuditLog
            {
                CashCountId = count.Id, CashSessionId = cashSessionId, AgentId = count.AgentId,
                Action = CashCountAuditAction.Approved, PreviousStatus = previousStatus, NewStatus = CashCountStatus.Approved,
                TotalAmount = count.TotalAmount, PerformedByUserId = userId, PerformedAt = clock.UtcNow,
                Notes = "Closing approved by an administrator. No operational float movement was repeated."
            });
            AddDecision(review, userId, ReconciliationDecisionKind.Approved,
                "Closing certified against current immutable observations and bank evidence.",
                new { session.ClosingSnapshotId, session.RegisterVersion, BasisHash = basisHash, CommandId = commandId },
                sessionId: cashSessionId);
            WorkflowNotifications.AddForUser(db, count.Agent!.UserId, "Closing count approved",
                "Your submitted closing count has been approved.", NotificationType.CountApproved,
                "/cashcount-history", clock.UtcNow);
            review.Version++;
            var account = await db.BankAccounts.SingleAsync(a => a.Id == accountId, cancellationToken);
            account.Version++;
            await db.SaveChangesAsync(cancellationToken);
            var result = await EvaluateAsync(review, cancellationToken);
            review.Status = AggregateStatus(result);
            result.Status = review.Status;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task RequireEvidenceAsync(long evidenceId, long accountId, CancellationToken cancellationToken)
    {
        if (!await db.BankEvidenceFiles.AnyAsync(f => f.Id == evidenceId && f.BankAccountId == accountId, cancellationToken))
            throw new BankingRuleException("Attach retained evidence for this bank account.");
    }
}
