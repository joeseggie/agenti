using System.Data;
using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService
{
    public Task<BankReconciliationReviewDto> AutoMatchAsync(
        string userId, long reviewId, int expectedVersion, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, reviewId, expectedVersion, async (review, facts) =>
        {
            var usedBanks = facts.Matches.Select(m => m.BankTransactionId).ToHashSet();
            var usedLegs = facts.Matches.Select(Key).ToHashSet();
            var availableBanks = facts.Banks.Where(b => !usedBanks.Contains(b.Id) &&
                b.Classification == BankTransactionClassification.Unclassified).ToList();
            var availableLegs = facts.Legs.Where(l => !l.PendingApproval && l.Kind != BankLegKind.ExpectedSettlement &&
                !usedLegs.Contains(Key(l))).ToList();
            var accountTimeZone = TimeZoneInfo.FindSystemTimeZoneById(facts.Account.TimeZoneId);
            foreach (var bank in availableBanks)
            {
                var candidates = availableLegs.Where(l => !usedLegs.Contains(Key(l)) && ExactCandidate(bank, l, accountTimeZone)).ToList();
                if (candidates.Count != 1)
                    continue;
                var leg = candidates[0];
                if (availableBanks.Count(b => !usedBanks.Contains(b.Id) && ExactCandidate(b, leg, accountTimeZone)) != 1)
                    continue;
                AddMatch(review, bank, leg, userId, "Unique exact account, direction, amount and identity match.");
                usedBanks.Add(bank.Id);
                usedLegs.Add(Key(leg));
            }
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<BankReconciliationReviewDto> MatchAsync(
        string userId, long reviewId, BankMatchForm form, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, reviewId, form.ExpectedReviewVersion, async (review, facts) =>
        {
            RequireReviewReason(form.Reason);
            var bank = facts.Banks.SingleOrDefault(b => b.Id == form.BankTransactionId)
                ?? throw new BankingRuleException("Select a bank occurrence in this review.");
            var leg = facts.Legs.SingleOrDefault(l => l.Kind == form.Kind && l.Id == form.SourceId)
                ?? throw new BankingRuleException("Select a current register entry in this review.");
            if (bank.Classification != BankTransactionClassification.Unclassified)
                throw new BankingRuleException("Clear the bank-only classification before matching this occurrence.");
            if (facts.Matches.Any(m => m.BankTransactionId == bank.Id))
                throw new BankingRuleException("This bank occurrence is already matched.");
            if (leg.Kind == BankLegKind.ExpectedSettlement)
                leg = await RecordObservedSettlementAsync(userId, bank, leg, facts, form.RelatedMovementId, cancellationToken);
            if (!ValidFinancialMatch(bank, leg))
                throw new BankingRuleException("Amount, currency and direction must match an approved register source.");
            if (facts.Matches.Any(m => Key(m) == Key(leg)))
                throw new BankingRuleException("This register entry is already matched.");
            AddMatch(review, bank, leg, userId, form.Reason);
        }, cancellationToken);

    public Task<BankReconciliationReviewDto> UnmatchAsync(
        string userId, long reviewId, long matchId, int expectedVersion, string reason, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, reviewId, expectedVersion, async (review, facts) =>
        {
            RequireReviewReason(reason);
            var match = await db.BankReconciliationMatches.SingleOrDefaultAsync(m =>
                m.Id == matchId && m.BankTransaction!.BankAccountId == review.BankAccountId && m.IsActive, cancellationToken)
                ?? throw new BankingRuleException("Active match not found.");
            match.IsActive = false;
            match.Version++;
            AddDecision(review, userId, ReconciliationDecisionKind.Unmatched, reason,
                new { MatchId = match.Id, Source = Key(match).ToString() }, match.BankTransactionId);
        }, cancellationToken);

    private async Task<BankReconciliationReviewDto> ChangeAsync(
        string userId, long reviewId, int expectedVersion,
        Func<BankReconciliationReview, ReviewFacts, Task> action, CancellationToken cancellationToken)
    {
        var accountId = await ReviewAccountIdAsync(reviewId, cancellationToken);
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var review = await db.BankReconciliationReviews.SingleAsync(r => r.Id == reviewId, cancellationToken);
            await db.Entry(review).ReloadAsync(cancellationToken);
            if (review.Version != expectedVersion)
                throw new BankingConflictException("This review changed. Refresh before making another decision.");
            var facts = await LoadFactsAsync(review, cancellationToken);
            AddExpectedSettlements(facts);
            await action(review, facts);
            review.Version++;
            review.Status = BankReconciliationStatus.InProgress;
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

    private static BankReconciliationStatus AggregateStatus(BankReconciliationReviewDto result)
    {
        if (result.Sessions.Any(s => s.Status == BankReconciliationStatus.LegacyIncomplete))
            return BankReconciliationStatus.LegacyIncomplete;
        if (result.PeriodIssues.Count > 0 || result.Sessions.Any(s => !s.CanApprove))
            return BankReconciliationStatus.NeedsCorrection;
        if (result.Sessions.Count > 0 && result.Sessions.All(s => s.ApprovalStatus == CashCountStatus.Approved))
            return result.Exceptions.Any(e => !e.IsResolved)
                ? BankReconciliationStatus.WithExceptions : BankReconciliationStatus.Reconciled;
        return BankReconciliationStatus.InProgress;
    }

    private static bool ExactCandidate(BankTransaction bank, BankLegDto leg, TimeZoneInfo accountTimeZone)
    {
        if (!ValidFinancialMatch(bank, leg))
            return false;
        var row = bank.AcceptedRow!;
        var hasBankReference = !string.IsNullOrWhiteSpace(row.TransactionNumber) ||
                               !string.IsNullOrWhiteSpace(row.Reference);
        if (!string.IsNullOrWhiteSpace(leg.Reference) && hasBankReference)
            return string.Equals(leg.Reference, row.TransactionNumber, StringComparison.Ordinal) ||
                   string.Equals(leg.Reference, row.Reference, StringComparison.Ordinal);
        return AccountDate(leg.OccurredAt, accountTimeZone) == row.LedgerDate;
    }

    private void AddMatch(BankReconciliationReview review, BankTransaction bank, BankLegDto leg, string userId, string reason)
    {
        db.BankReconciliationMatches.Add(new BankReconciliationMatch
        {
            BankTransactionId = bank.Id,
            CustomerMovementId = leg.Kind == BankLegKind.CustomerMovement ? leg.Id : null,
            BankRunId = leg.Kind == BankLegKind.BankRun ? leg.Id : null,
            WalletAdjustmentId = leg.Kind == BankLegKind.WalletAdjustment ? leg.Id : null,
            MatchedByUserId = userId, MatchedAt = clock.UtcNow
        });
        AddDecision(review, userId, ReconciliationDecisionKind.Matched, reason,
            new { leg.Kind, leg.Id, leg.CashSessionId }, bank.Id);
    }

    private async Task<BankLegDto> RecordObservedSettlementAsync(
        string userId, BankTransaction bank, BankLegDto expected, ReviewFacts facts,
        long? relatedMovementId, CancellationToken cancellationToken)
    {
        if (expected.Currency != bank.AcceptedRow!.Currency ||
            Math.Sign(expected.SignedAmount) != Math.Sign(bank.AcceptedRow.SignedAmount) ||
            Math.Abs(bank.AcceptedRow.SignedAmount) > Math.Abs(expected.SignedAmount))
            throw new BankingRuleException("The bank posting exceeds or contradicts the expected case settlement.");
        var customer = facts.Customers.Single(c => c.Id == expected.CustomerTransactionId);
        var active = Active(customer.Movements);
        var primary = Primary(active) ?? throw new BankingRuleException("The case's original entry is missing.");
        var opposite = active.Where(m => m.BankEffect != 0 &&
            Math.Sign(m.BankEffect) == -Math.Sign(bank.AcceptedRow.SignedAmount)).ToList();
        CustomerMovement related;
        var kind = CustomerMovementKind.BankPostingObserved;
        if (relatedMovementId.HasValue)
            related = active.SingleOrDefault(m => m.Id == relatedMovementId.Value)
                ?? throw new BankingRuleException("Select a current related movement in this case.");
        else if (opposite.Count == 1)
            related = opposite[0];
        else if (opposite.Count > 1)
            throw new BankingRuleException("Several bank postings could be reversed. Select the related movement explicitly.");
        else
            related = primary;
        if (related.BankEffect != 0 && Math.Sign(related.BankEffect) == -Math.Sign(bank.AcceptedRow.SignedAmount))
            kind = CustomerMovementKind.BankReversal;
        var movement = new CustomerMovement
        {
            CustomerTransactionId = customer.Id, CashSessionId = customer.OriginatingSessionId,
            Kind = kind, Amount = Math.Abs(bank.AcceptedRow.SignedAmount),
            TransactionKind = primary.TransactionKind ?? customer.Kind,
            CustomerIdentifier = primary.CustomerIdentifier ?? customer.CustomerIdentifier,
            BankEffect = bank.AcceptedRow.SignedAmount, RelatedMovementId = related.Id,
            BankReference = bank.AcceptedRow.TransactionNumber ?? bank.AcceptedRow.Reference,
            ReferenceUnavailable = string.IsNullOrWhiteSpace(bank.AcceptedRow.TransactionNumber) &&
                                   string.IsNullOrWhiteSpace(bank.AcceptedRow.Reference),
            CommandId = Guid.NewGuid(), RequestHash = BankingRequestIdentity.Hash(new
            {
                BankTransactionId = bank.Id, bank.AcceptedRowId, CustomerTransactionId = customer.Id, RelatedMovementId = related.Id
            }),
            Notes = "Bank-origin posting verified from retained bank evidence.",
            OccurredAt = LedgerAt(bank), OccurrenceDate = clock.GetBusinessDate(LedgerAt(bank)),
            RecordedAt = clock.UtcNow, RecordedByUserId = userId
        };
        db.CustomerMovements.Add(movement);
        var session = await db.CashSessions.SingleAsync(s => s.Id == customer.OriginatingSessionId, cancellationToken);
        session.RegisterVersion++;
        session.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return new BankLegDto(BankLegKind.CustomerMovement, movement.Id, movement.CashSessionId, customer.Id,
            movement.BankEffect, customer.Currency, movement.BankReference, movement.CustomerIdentifier,
            movement.OccurredAt, true, false);
    }

    private void AddDecision(
        BankReconciliationReview review, string userId, ReconciliationDecisionKind kind,
        string reason, object details, long? bankTransactionId = null, long? sessionId = null) =>
        db.BankReconciliationDecisions.Add(new BankReconciliationDecision
        {
            BankAccountId = review.BankAccountId, ReviewId = review.Id, CashSessionId = sessionId,
            BankTransactionId = bankTransactionId, Kind = kind, DetailsJson = JsonSerializer.Serialize(details),
            Reason = reason.Trim(), PerformedByUserId = userId, PerformedAt = clock.UtcNow
        });

    private static void RequireReviewReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 2000)
            throw new BankingRuleException("Provide a review explanation between 10 and 2,000 characters.");
    }
}
