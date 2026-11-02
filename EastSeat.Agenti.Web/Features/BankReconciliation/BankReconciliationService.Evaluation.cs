using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService
{
    private async Task<BankReconciliationReviewDto> EvaluateAsync(
        BankReconciliationReview review, CancellationToken cancellationToken)
    {
        var facts = await LoadFactsAsync(review, cancellationToken);
        AddExpectedSettlements(facts);
        var result = new BankReconciliationReviewDto
        {
            Id = review.Id, BankAccountId = facts.Account.Id, BankName = facts.Account.BankName,
            FromDate = review.FromDate, ToDate = review.ToDate, Version = review.Version, Status = review.Status,
            BankEntries = facts.Banks.OrderBy(t => LedgerAt(t)).ThenBy(t => t.AcceptedRow!.Sequence).Select(MapBank).ToList(),
            RegisterEntries = facts.Legs.OrderBy(l => l.OccurredAt).ThenBy(l => l.Id).ToList(),
            Matches = facts.Matches.Where(m => facts.Banks.Any(b => b.Id == m.BankTransactionId) ||
                    facts.Legs.Any(l => Key(l) == Key(m)))
                .Select(m => new BankMatchDto(m.Id, m.BankTransactionId, Key(m).Kind, Key(m).Id, m.Version)).ToList(),
            Exceptions = facts.Exceptions.Select(MapException).ToList(),
            CorrectionRequests = facts.Corrections.Select(MapCorrection).ToList()
        };
        foreach (var session in facts.Sessions)
            result.Sessions.Add(await EvaluateSessionAsync(session, facts, cancellationToken));
        foreach (var bank in facts.Banks.Where(b => b.AcceptedRow!.LedgerDate >= review.FromDate &&
                     b.AcceptedRow.LedgerDate <= review.ToDate && b.AcceptedRow.SignedAmount != 0))
        {
            if (!BankAccountedFor(bank, facts))
                result.PeriodIssues.Add($"Bank occurrence {bank.Id} is not matched or independently explained.");
        }
        if (result.Sessions.Count == 0)
            result.PeriodIssues.Add("No agent sessions fall in this interval. Bank activity still requires account-level review.");
        if (result.Status is BankReconciliationStatus.Reconciled or BankReconciliationStatus.WithExceptions &&
            (result.Sessions.Any(s => !s.CanApprove) || result.PeriodIssues.Count > 0))
            result.Status = BankReconciliationStatus.NeedsReview;
        return result;
    }

    private async Task<SessionReconciliationDto> EvaluateSessionAsync(
        CashSession session, ReviewFacts facts, CancellationToken cancellationToken)
    {
        var closingCount = facts.Counts.SingleOrDefault(c => c.CashSessionId == session.Id && !c.IsOpening);
        var dto = new SessionReconciliationDto
        {
            CashSessionId = session.Id, SessionDate = session.SessionDate, ClosingCountId = closingCount?.Id,
            RegisterVersion = session.RegisterVersion, ApprovalStatus = closingCount?.Status,
            Status = BankReconciliationStatus.InProgress
        };
        if (session.WorkflowVersion != SessionWorkflowVersion.BankReconciliation)
        {
            dto.Status = BankReconciliationStatus.LegacyIncomplete;
            dto.BlockingReasons.Add("This is a legacy session. Its historical approval is retained; new-workflow completeness is not assumed.");
            return dto;
        }
        var openingSnapshot = facts.Snapshots.SingleOrDefault(s => s.CashCount!.CashSessionId == session.Id && s.CashCount.IsOpening);
        var closingSnapshot = facts.Snapshots.SingleOrDefault(s => s.CashCount!.CashSessionId == session.Id && !s.CashCount.IsOpening);
        if (openingSnapshot == null || closingSnapshot == null || closingCount == null)
        {
            dto.BlockingReasons.Add("Both an approved opening and a submitted immutable closing snapshot are required.");
            return dto;
        }
        dto.ClosingSnapshotId = closingSnapshot.Id;
        var opening = CountSnapshotCodec.Deserialize(openingSnapshot.WalletsJson);
        var closing = CountSnapshotCodec.Deserialize(closingSnapshot.WalletsJson);
        var bankOpening = opening.SingleOrDefault(w => w.WalletId == facts.Account.WalletId);
        var bankClosing = closing.SingleOrDefault(w => w.WalletId == facts.Account.WalletId);
        if (bankOpening == null || bankClosing == null)
        {
            dto.BlockingReasons.Add("The mapped bank wallet is missing from this session's snapshots.");
            return dto;
        }
        dto.OpeningObservedAt = bankOpening.ObservedAt;
        dto.ClosingObservedAt = bankClosing.ObservedAt;
        dto.ReportedBankClosingBalance = bankClosing.Amount;
        if (!Covers(facts.Coverage, bankOpening.ObservedAt, bankClosing.ObservedAt))
            dto.BlockingReasons.Add("Verified bank-statement coverage is missing or contradictory for the complete opening-to-closing interval.");
        dto.StatementOpeningBalance = BalanceAt(facts.Coverage, bankOpening.ObservedAt);
        dto.StatementClosingBalance = BalanceAt(facts.Coverage, bankClosing.ObservedAt);
        if (!dto.StatementOpeningBalance.HasValue || !dto.StatementClosingBalance.HasValue)
            dto.BlockingReasons.Add("A bank balance cannot be established at a snapshot cutoff; inspect coverage and timestamp precision.");
        if (facts.Corrections.Any(r => r.CashCountId == closingCount.Id && r.IsOpen))
            dto.BlockingReasons.Add("The agent's requested corrections have not been resubmitted.");
        if (await db.WalletAdjustments.AnyAsync(a => a.CashSessionId == session.Id &&
                a.AgentId == closingCount.AgentId && a.Status == WalletAdjustmentStatus.Pending, cancellationToken))
            dto.BlockingReasons.Add("Recorded wallet adjustments still require their separate approval.");
        if (!await db.SessionFloatSettlements.AnyAsync(s => s.CashCountId == closingCount.Id &&
                s.Stage == FloatSettlementStage.Closing, cancellationToken))
            dto.BlockingReasons.Add("The closing's operational settlement has not been recorded.");

        var positions = await balances.GetPositionsAtSnapshotAsync(closingSnapshot.Id, cancellationToken: cancellationToken);
        var legLookup = facts.Legs.Where(l => l.Kind != BankLegKind.ExpectedSettlement).ToDictionary(Key);
        var bankLookup = facts.Banks.ToDictionary(b => b.Id);
        var validMatches = facts.Matches.Where(m => bankLookup.TryGetValue(m.BankTransactionId, out var bank) &&
                legLookup.TryGetValue(Key(m), out var leg) && ValidFinancialMatch(bank, leg))
            .ToList();
        var matchedBankIds = validMatches.Select(m => m.BankTransactionId).ToHashSet();
        var matchedLegKeys = validMatches.Select(Key).ToHashSet();
        var bankAdjustment = 0m;
        foreach (var match in validMatches)
        {
            var bank = bankLookup[match.BankTransactionId];
            var leg = legLookup[Key(match)];
            var actual = InWindow(LedgerAt(bank), bankOpening.ObservedAt, bankClosing.ObservedAt);
            var included = IncludedInProjection(leg, session.Id, bankOpening.ObservedAt, bankClosing.ObservedAt);
            var change = (actual ? bank.AcceptedRow!.SignedAmount : 0m) - (included ? leg.SignedAmount : 0m);
            if (change != 0)
            {
                bankAdjustment += change;
                dto.Adjustments.Add(new("Posting timing", change,
                    "The bank posting and recorded operation fall in different snapshot intervals.", bank.Id, null));
            }
        }
        foreach (var bank in facts.Banks.Where(b => InWindow(LedgerAt(b), bankOpening.ObservedAt, bankClosing.ObservedAt)))
        {
            if (matchedBankIds.Contains(bank.Id) || bank.AcceptedRow!.SignedAmount == 0)
                continue;
            var explanation = facts.Exceptions.FirstOrDefault(e => e.BankTransactionId == bank.Id &&
                e.Kind == BankExceptionKind.BankFailure && e.BankExplanationVerified && e.Amount == Math.Abs(bank.AcceptedRow.SignedAmount));
            if (bank.Classification != BankTransactionClassification.Unclassified || explanation != null)
            {
                bankAdjustment += bank.AcceptedRow.SignedAmount;
                dto.Adjustments.Add(new(explanation == null ? bank.Classification.ToString() : "Verified bank failure",
                    bank.AcceptedRow.SignedAmount, bank.ClassificationReason ?? explanation!.Explanation, bank.Id, explanation?.Id));
            }
            else
                dto.BlockingReasons.Add($"Bank occurrence {bank.Id} has no matching register entry or verified explanation.");
        }
        foreach (var leg in facts.Legs.Where(l => l.Kind != BankLegKind.ExpectedSettlement &&
                     IncludedInProjection(l, session.Id, bankOpening.ObservedAt, bankClosing.ObservedAt)))
        {
            if (leg.PendingApproval)
                dto.BlockingReasons.Add($"Wallet adjustment {leg.Id} is not approved.");
            if (!leg.BankOrigin && leg.OccurredAt < bankOpening.ObservedAt)
                dto.BlockingReasons.Add($"Register entry {leg.Kind} {leg.Id} predates opening; verify its attribution and receipt timestamp.");
            if (matchedLegKeys.Contains(Key(leg)))
                continue;
            var delay = PostingDelay(facts, leg);
            if (delay == null)
                dto.BlockingReasons.Add($"Register entry {leg.Kind} {leg.Id} has not been matched to the bank.");
            else
            {
                bankAdjustment -= leg.SignedAmount;
                dto.Adjustments.Add(new("Unposted receipt", -leg.SignedAmount,
                    "A verified posting delay remains explicit, not falsely marked as a completed match.", null, delay.Id));
            }
        }

        var openingDifference = KnownPendingDifference(facts, bankOpening.ObservedAt, session.Id, false);
        var closingDifference = KnownPendingDifference(facts, bankClosing.ObservedAt, session.Id, true);
        var timingDifference = closingDifference - openingDifference;
        if (timingDifference != 0)
        {
            bankAdjustment += timingDifference;
            dto.Adjustments.Add(new("Documented pending-balance difference", timingDifference,
                "The reported balance includes an explicitly verified posting not yet reflected in bank ledger evidence.", null, null));
        }
        dto.ExplainedBankDifference = closingDifference;
        if (dto.StatementOpeningBalance.HasValue && bankOpening.Amount != dto.StatementOpeningBalance.Value + openingDifference)
            dto.BlockingReasons.Add("The opening bank observation does not agree with the bank ledger and documented timing items.");
        if (dto.StatementClosingBalance.HasValue)
        {
            dto.UnexplainedBankDifference = bankClosing.Amount - dto.StatementClosingBalance.Value - closingDifference;
            if (dto.UnexplainedBankDifference != 0)
                dto.BlockingReasons.Add("The reported closing bank balance has an unexplained difference from bank evidence.");
        }
        foreach (var wallet in closing)
        {
            var position = positions.SingleOrDefault(p => p.WalletId == wallet.WalletId);
            if (position == null)
            {
                dto.BlockingReasons.Add("A closing wallet does not exist in the opening allocation.");
                continue;
            }
            var adjustment = wallet.WalletId == facts.Account.WalletId ? bankAdjustment : 0m;
            var expected = position.ExpectedBalance + adjustment;
            var variance = wallet.Amount - expected;
            dto.Wallets.Add(new(wallet.WalletId, wallet.WalletName, position.OpeningAmount,
                position.ExpectedBalance, adjustment, expected, wallet.Amount, variance));
            if (variance != 0)
                dto.BlockingReasons.Add($"Wallet {wallet.WalletName} has an unexplained closing variance.");
        }

        foreach (var bridge in facts.Bridges.Where(b => b.OpeningSnapshot!.CashCount!.CashSessionId == session.Id))
        {
            if (!bridge.PreviousObservedAt.HasValue)
            {
                dto.Notes.Add("The initial bridge from legacy history has no verified old observation timestamp; historical coverage remains explicitly incomplete.");
                continue;
            }
            if (!Covers(facts.Coverage, bridge.PreviousObservedAt.Value, bridge.ObservedAt))
                dto.BlockingReasons.Add("Statement coverage is missing for the interval between the previous closing and this opening.");
            foreach (var bank in facts.Banks.Where(b => InWindow(LedgerAt(b), bridge.PreviousObservedAt.Value, bridge.ObservedAt) &&
                         b.AcceptedRow!.SignedAmount != 0))
                if (!BankAccountedFor(bank, facts))
                    dto.BlockingReasons.Add($"Overnight bank occurrence {bank.Id} has not been explained.");
            var previousBankBalance = BalanceAt(facts.Coverage, bridge.PreviousObservedAt.Value);
            if (previousBankBalance.HasValue &&
                previousBankBalance.Value + KnownPendingDifference(facts, bridge.PreviousObservedAt.Value, session.Id, false) != bridge.PreviousBankBalance)
                dto.BlockingReasons.Add("The prior closing bank observation does not agree with the bank evidence used for this opening bridge.");
        }

        foreach (var customer in facts.Customers.Where(c => c.OriginatingSessionId == session.Id))
        {
            var active = Active(customer.Movements);
            var primary = Primary(active);
            if (primary == null)
            {
                dto.BlockingReasons.Add($"Customer transaction {customer.Id} has no valid primary record.");
                continue;
            }
            if (primary.Kind == CustomerMovementKind.Voided)
                continue;
            var settled = active.Sum(m => m.CustomerSettledAmount);
            var refunded = active.Where(m => m.Kind == CustomerMovementKind.Refund).Sum(m => -m.CashEffect);
            var cancelled = active.Where(m => m.Kind == CustomerMovementKind.Cancelled).Sum(m => m.Amount);
            var kind = primary.TransactionKind ?? customer.Kind;
            var due = kind == CustomerTransactionKind.Deposit ? Math.Max(0, settled - active.Sum(m => m.CashEffect)) : 0m;
            var exposure = active.Sum(m => m.BankEffect) - (kind == CustomerTransactionKind.Deposit ? -settled : settled);
            var unfinished = primary.Amount != settled + refunded + cancelled || due != 0 || exposure != 0;
            var affectedAmount = Math.Max(Math.Abs(exposure), Math.Max(due, Math.Max(0, primary.Amount - settled - refunded - cancelled)));
            var documented = facts.Exceptions.Any(e => e.CustomerTransactionId == customer.Id &&
                e.Kind != BankExceptionKind.UnexplainedMovement && e.BankExplanationVerified && e.Amount >= affectedAmount);
            if (settled < 0 || refunded < 0 || settled + refunded + cancelled > primary.Amount)
                dto.BlockingReasons.Add($"Customer transaction {customer.Id} has inconsistent settlement amounts.");
            if (unfinished && !documented)
                dto.BlockingReasons.Add($"Customer case {customer.Id} requires an explicit verified exception or completion evidence.");
        }
        var sessionExceptions = facts.Exceptions.Where(e => e.CashSessionId == session.Id).ToList();
        if (sessionExceptions.Any(e => !e.IsResolved &&
            (e.Kind == BankExceptionKind.UnexplainedMovement || !e.BankExplanationVerified)))
            dto.BlockingReasons.Add("An unexplained or unverified bank exception still holds approval.");
        dto.BlockingReasons = dto.BlockingReasons.Distinct().ToList();
        dto.CanApprove = dto.BlockingReasons.Count == 0;
        dto.Status = dto.CanApprove
            ? sessionExceptions.Any(e => !e.IsResolved) ? BankReconciliationStatus.WithExceptions : BankReconciliationStatus.Reconciled
            : BankReconciliationStatus.NeedsCorrection;
        dto.BasisHash = BankingRequestIdentity.Hash(new
        {
            Version = 1, session.Id, session.RegisterVersion, OpeningSnapshot = openingSnapshot.Id, ClosingSnapshot = closingSnapshot.Id,
            BankRows = facts.Banks.OrderBy(b => b.Id).Select(b => new { b.Id, b.Version, b.AcceptedRowId, b.Classification, b.RelatedCustomerTransactionId }),
            Legs = facts.Legs.OrderBy(l => l.Kind).ThenBy(l => l.Id),
            Matches = facts.Matches.OrderBy(m => m.Id).Select(m => new { m.Id, m.Version, m.BankTransactionId, Source = Key(m).ToString() }),
            Exceptions = facts.Exceptions.OrderBy(e => e.Id).Select(e => new { e.Id, e.Version, e.IsResolved, e.ReflectedInClosingBalance }),
            Coverage = facts.Coverage.OrderBy(i => i.Id).Select(i => new { i.Id, i.Version, i.CoverageFrom, i.CoverageTo, i.CoverageVerified }),
            Corrections = facts.Corrections.OrderBy(r => r.Id).Select(r => new { r.Id, r.Version, r.IsOpen }),
            Cases = facts.Customers.OrderBy(c => c.Id).Select(c => new
            {
                c.Id, Events = Active(c.Movements).OrderBy(m => m.Id).Select(m => new
                {
                    m.Id, m.Amount, m.CashEffect, m.BankEffect, m.CustomerSettledAmount, m.Kind,
                    m.OccurredAt, m.EvidenceFileId, m.SupportTicketNumber
                })
            }),
            dto.Wallets, dto.StatementOpeningBalance, dto.StatementClosingBalance, dto.ExplainedBankDifference
        });
        return dto;
    }

    private static void AddExpectedSettlements(ReviewFacts facts)
    {
        foreach (var entry in facts.Customers)
        {
            var active = Active(entry.Movements);
            var primary = Primary(active);
            if (primary == null || primary.Kind == CustomerMovementKind.Voided)
                continue;
            var settled = active.Sum(m => m.CustomerSettledAmount);
            var kind = primary.TransactionKind ?? entry.Kind;
            var missing = (kind == CustomerTransactionKind.Deposit ? -settled : settled) - active.Sum(m => m.BankEffect);
            if (missing != 0)
                facts.Legs.Add(new BankLegDto(BankLegKind.ExpectedSettlement, entry.Id, entry.OriginatingSessionId,
                    entry.Id, missing, entry.Currency, null, primary.CustomerIdentifier ?? entry.CustomerIdentifier,
                    primary.OccurredAt, true, false));
        }
    }

    private static List<CustomerMovement> Active(IEnumerable<CustomerMovement> movements)
    {
        var all = movements.ToList();
        var superseded = all.Where(m => m.SupersedesMovementId.HasValue).Select(m => m.SupersedesMovementId!.Value).ToHashSet();
        return all.Where(m => !superseded.Contains(m.Id)).ToList();
    }

    private static CustomerMovement? Primary(IEnumerable<CustomerMovement> movements) =>
        movements.SingleOrDefault(m => m.RelatedMovementId == null &&
            m.Kind is CustomerMovementKind.Completed or CustomerMovementKind.FailedAttempt or CustomerMovementKind.PendingAttempt or CustomerMovementKind.Voided);

    private static DateTimeOffset LedgerAt(BankTransaction transaction) => transaction.AcceptedRow!.PostedAt ?? transaction.AcceptedRow.OccurredAt;
    private static bool InWindow(DateTimeOffset at, DateTimeOffset from, DateTimeOffset to) => at > from && at <= to;
    private static bool IncludedInProjection(BankLegDto leg, long sessionId, DateTimeOffset from, DateTimeOffset to) =>
        leg.Kind != BankLegKind.ExpectedSettlement &&
        (leg.BankOrigin ? InWindow(leg.OccurredAt, from, to) : leg.CashSessionId == sessionId && leg.OccurredAt <= to);
    private static bool ValidFinancialMatch(BankTransaction bank, BankLegDto leg) =>
        !leg.PendingApproval && bank.AcceptedRow!.Currency == leg.Currency && bank.AcceptedRow.SignedAmount == leg.SignedAmount;

    private static bool Covers(IEnumerable<BankStatementImport> imports, DateTimeOffset from, DateTimeOffset to)
    {
        var sources = imports.ToList();
        var boundaries = sources.SelectMany(i => new[] { i.CoverageFrom, i.CoverageTo })
            .Where(t => t.HasValue && t.Value > from && t.Value <= to).Select(t => t!.Value)
            .Append(from).Append(to).Distinct().Order().ToList();
        BankStatementImport? previous = null;
        foreach (var boundary in boundaries)
        {
            var source = LatestSource(sources, boundary);
            if (source?.CoverageVerified != true) return false;
            if (previous != null && previous.Id != source.Id)
            {
                var previousBalance = SourceBalance(previous, boundary, inclusive: false);
                var nextBalance = SourceBalance(source, boundary, inclusive: false);
                if (!previousBalance.HasValue || previousBalance != nextBalance) return false;
            }
            previous = source;
        }
        return true;
    }

    private static decimal? BalanceAt(IEnumerable<BankStatementImport> imports, DateTimeOffset at)
    {
        var source = LatestSource(imports, at);
        return source?.CoverageVerified == true ? SourceBalance(source, at, inclusive: true) : null;
    }

    private static BankStatementImport? LatestSource(IEnumerable<BankStatementImport> imports, DateTimeOffset at) =>
        imports.Where(i => i.CoverageFrom <= at && i.CoverageTo > at)
            .OrderByDescending(i => i.CommittedAt ?? i.CreatedAt).ThenByDescending(i => i.Id).FirstOrDefault();

    private static decimal? SourceBalance(BankStatementImport source, DateTimeOffset at, bool inclusive)
    {
        if (source.OpeningBalance == null)
            return null;
        var rows = StatementValidation.EffectiveRows(source.Rows);
        if (rows.Any(r => !r.HasPreciseTimestamp && r.LedgerDate == DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(3)).DateTime)))
            return null;
        return source.OpeningBalance.Value + rows.Where(r => inclusive
            ? StatementValidation.LedgerTime(r) <= at : StatementValidation.LedgerTime(r) < at).Sum(r => r.SignedAmount);
    }

    private static bool BankAccountedFor(BankTransaction bank, ReviewFacts facts) =>
        bank.AcceptedRow!.SignedAmount == 0 ||
        bank.Classification != BankTransactionClassification.Unclassified ||
        facts.Matches.Any(m => m.BankTransactionId == bank.Id &&
            facts.Legs.Any(l => Key(l) == Key(m) && ValidFinancialMatch(bank, l))) ||
        facts.Exceptions.Any(e => e.BankTransactionId == bank.Id && e.Kind == BankExceptionKind.BankFailure &&
            e.BankExplanationVerified && e.Amount == Math.Abs(bank.AcceptedRow.SignedAmount));

    private static bool RefersTo(BankReconciliationException exception, BankLegDto leg) => leg.Kind switch
    {
        BankLegKind.CustomerMovement => exception.CustomerMovementId == leg.Id,
        BankLegKind.BankRun => exception.BankRunId == leg.Id,
        BankLegKind.WalletAdjustment => exception.WalletAdjustmentId == leg.Id,
        _ => false
    };

    private static BankReconciliationException? PostingDelay(ReviewFacts facts, BankLegDto leg) =>
        facts.Exceptions.Where(e => e.Kind == BankExceptionKind.PostingDelay && e.BankExplanationVerified &&
                e.ReflectedInClosingBalance.HasValue && RefersTo(e, leg) && e.Amount == Math.Abs(leg.SignedAmount))
            .OrderByDescending(e => e.Id).FirstOrDefault();

    private static decimal KnownPendingDifference(ReviewFacts facts, DateTimeOffset at, long sessionId, bool closing)
    {
        var total = 0m;
        foreach (var group in facts.Exceptions.Where(e => e.Kind == BankExceptionKind.PostingDelay &&
                     e.BankExplanationVerified && e.ReflectedInClosingBalance.HasValue)
                     .GroupBy(e => (e.CustomerMovementId, e.BankRunId, e.WalletAdjustmentId)))
        {
            var selected = group.OrderByDescending(e => closing && e.CashSessionId == sessionId)
                .ThenByDescending(e => e.Id).First();
            if (selected.ReflectedInClosingBalance != true)
                continue;
            var leg = facts.Legs.FirstOrDefault(l => RefersTo(selected, l));
            if (leg == null || leg.OccurredAt > at)
                continue;
            var match = facts.Matches.FirstOrDefault(m => Key(m) == Key(leg));
            var bank = match == null ? null : facts.Banks.FirstOrDefault(b => b.Id == match.BankTransactionId);
            if (bank == null || LedgerAt(bank) > at)
                total += leg.SignedAmount;
        }
        return total;
    }
}
