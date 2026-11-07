using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankStatements;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService
{
    private async Task<BankReconciliationReviewDto> EvaluateAsync(
        BankReconciliationReview review, CancellationToken cancellationToken)
    {
        var facts = await LoadFactsAsync(review, cancellationToken);
        var flags = await LoadOverlappingFlagsAsync(review, cancellationToken);
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
        {
            var sessionResult = await EvaluateSessionAsync(session, facts, cancellationToken);
            var openFlags = flags.Where(f => f.Response == null && f.FromDate <= session.SessionDate &&
                f.ToDate >= session.SessionDate).Select(f => f.Flag.Id).Order().ToList();
            if (openFlags.Count > 0)
            {
                sessionResult.CanApprove = false;
                sessionResult.Status = BankReconciliationStatus.NeedsCorrection;
                sessionResult.BlockingReasons.Add("An overlapping bank review has an unresolved supervisor flag.");
                sessionResult.BasisHash = BankingRequestIdentity.Hash(new { sessionResult.BasisHash, OpenFlags = openFlags });
            }
            result.Sessions.Add(sessionResult);
        }
        result.DailyControls = BuildDailyControls(review, facts);
        result.DailyControlStatus = result.DailyControls.Any(d =>
                d.Status is DailyBankControlStatus.MissingCoverage or DailyBankControlStatus.NeedsReview)
            ? BankReconciliationStatus.NeedsCorrection
            : result.DailyControls.Any(d => d.Status == DailyBankControlStatus.WithExceptions)
                ? BankReconciliationStatus.WithExceptions
                : BankReconciliationStatus.Reconciled;
        foreach (var flag in flags)
        {
            result.Flags.Add(new BankReviewFlagDto(flag.Flag.Id, flag.Flag.PerformedByUserId, flag.Flag.PerformedAt,
                flag.Flag.Reason, flag.Response?.PerformedByUserId, flag.Response?.PerformedAt,
                flag.Response?.Reason, flag.Response == null));
        }
        if (result.Flags.Any(f => f.IsOpen))
            result.DailyControlStatus = BankReconciliationStatus.NeedsReview;
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

    private async Task<List<ReviewFlagState>> LoadOverlappingFlagsAsync(
        BankReconciliationReview review, CancellationToken cancellationToken)
    {
        var reviews = await db.BankReconciliationReviews.AsNoTracking()
            .Where(r => r.BankAccountId == review.BankAccountId &&
                r.FromDate <= review.ToDate && r.ToDate >= review.FromDate)
            .Select(r => new { r.Id, r.FromDate, r.ToDate })
            .ToListAsync(cancellationToken);
        var reviewIds = reviews.Select(r => r.Id).ToList();
        var decisions = await db.BankReconciliationDecisions.AsNoTracking().Where(d =>
                d.ReviewId.HasValue && reviewIds.Contains(d.ReviewId.Value) &&
                (d.Kind == ReconciliationDecisionKind.SupervisorFlagged ||
                 d.Kind == ReconciliationDecisionKind.AdminFlagResponse))
            .OrderBy(d => d.Id).ToListAsync(cancellationToken);
        var responses = decisions.Where(d => d.Kind == ReconciliationDecisionKind.AdminFlagResponse)
            .Select(d => (FlagId: FlagIdFrom(d.DetailsJson), Decision: d))
            .Where(d => d.FlagId.HasValue)
            .ToDictionary(d => d.FlagId!.Value, d => d.Decision);
        var ranges = reviews.ToDictionary(r => r.Id);
        return decisions.Where(d => d.Kind == ReconciliationDecisionKind.SupervisorFlagged &&
                (d.ReviewId == review.Id || !responses.ContainsKey(d.Id)))
            .Select(d =>
            {
                var range = ranges[d.ReviewId!.Value];
                responses.TryGetValue(d.Id, out var response);
                return new ReviewFlagState(d, response, range.FromDate, range.ToDate);
            }).ToList();
    }

    private static long? FlagIdFrom(string details)
    {
        using var document = JsonDocument.Parse(details);
        return document.RootElement.TryGetProperty("FlagId", out var value) && value.TryGetInt64(out var id)
            ? id : null;
    }

    private List<DailyBankControlDto> BuildDailyControls(BankReconciliationReview review, ReviewFacts facts)
    {
        var controls = new List<DailyBankControlDto>();
        var sourceRows = facts.Coverage.ToDictionary(i => i.Id, i => StatementValidation.EffectiveRows(i.Rows));
        var chainValidity = new Dictionary<long, bool>();
        var closingBalances = new Dictionary<DateOnly, decimal?>();

        BankStatementImport? LatestCoverage(DateOnly date) => facts.Coverage
            .Where(i => i.CoverageFromDate <= date && i.CoverageToDate >= date)
            .OrderByDescending(i => i.CommittedAt ?? i.CreatedAt).ThenByDescending(i => i.Id).FirstOrDefault();

        bool IsChainValid(BankStatementImport source)
        {
            if (chainValidity.TryGetValue(source.Id, out var valid))
                return valid;
            if (source.CoverageVerified != true || !source.CoverageFromDate.HasValue ||
                !source.CoverageToDate.HasValue || !source.OpeningBalance.HasValue)
                return chainValidity[source.Id] = false;

            var start = source.CoverageFromDate.Value;
            if (start == DateOnly.MinValue)
                return chainValidity[source.Id] = true;
            var previousDate = start.AddDays(-1);
            var predecessor = LatestCoverage(previousDate);
            if (predecessor == null)
                return chainValidity[source.Id] =
                    !facts.Coverage.Any(i => i.CoverageToDate < start);

            var priorClosing = ClosingAt(previousDate);
            var sourceOpening = BalanceBefore(source, sourceRows[source.Id], start);
            return chainValidity[source.Id] =
                priorClosing.HasValue && sourceOpening.HasValue && priorClosing.Value == sourceOpening.Value;
        }

        decimal? ClosingAt(DateOnly date)
        {
            if (closingBalances.TryGetValue(date, out var cached))
                return cached;
            var source = LatestCoverage(date);
            if (source == null || !IsChainValid(source))
                return closingBalances[date] = null;
            var rows = sourceRows[source.Id];
            var opening = BalanceBefore(source, rows, date);
            if (!opening.HasValue)
                return closingBalances[date] = null;
            var dayRows = rows.Where(r => r.LedgerDate == date).OrderBy(r => r.Sequence).ToList();
            return closingBalances[date] = dayRows.Count > 0 ? dayRows[^1].RunningBalance : opening;
        }

        var bankById = facts.Banks.ToDictionary(b => b.Id);
        var legsByKey = facts.Legs.ToDictionary(Key);
        var validMatches = facts.Matches.Where(m => m.IsActive &&
                bankById.TryGetValue(m.BankTransactionId, out var bank) &&
                legsByKey.TryGetValue(Key(m), out var leg) && ValidFinancialMatch(bank, leg))
            .ToList();
        var matchedBankIds = validMatches.Select(m => m.BankTransactionId).ToHashSet();
        var matchedLegKeys = validMatches.Select(Key).ToHashSet();

        for (var date = review.FromDate; date <= review.ToDate; date = date.AddDays(1))
        {
            var import = LatestCoverage(date);
            if (import?.CoverageVerified != true)
            {
                controls.Add(new(date, DailyBankControlStatus.MissingCoverage, null, null, 0, 0, 0, 0, 0, 0,
                    "A committed, verified statement period is required for this date."));
                continue;
            }

            var rows = sourceRows[import.Id];
            var dayRows = rows.Where(r => r.LedgerDate == date).OrderBy(r => r.Sequence).ToList();
            var opening = IsChainValid(import) ? BalanceBefore(import, rows, date) : null;
            var closing = opening.HasValue ? dayRows.Count > 0 ? dayRows[^1].RunningBalance : opening : null;
            var bankEntries = facts.Banks.Where(b => b.AcceptedRow!.LedgerDate == date &&
                b.AcceptedRow.SignedAmount != 0).ToList();
            var statementMovements = bankEntries.Sum(b => b.AcceptedRow!.SignedAmount);
            var dayLegs = facts.Legs.Where(l => l.Kind != BankLegKind.ExpectedSettlement &&
                clock.GetBusinessDate(l.OccurredAt) == date).ToList();
            var registerMovements = dayLegs.Where(l => !matchedLegKeys.Contains(Key(l))).Sum(l => l.SignedAmount) +
                bankEntries.Where(b => matchedBankIds.Contains(b.Id)).Sum(b => b.AcceptedRow!.SignedAmount);
            var classifiedMovements = bankEntries.Where(b =>
                    b.Classification is not (BankTransactionClassification.Unclassified or BankTransactionClassification.Unknown) &&
                    !matchedBankIds.Contains(b.Id))
                .Sum(b => b.AcceptedRow!.SignedAmount);
            var unaccounted = bankEntries.Count(b => !BankAccountedFor(b, facts));
            var unaccountedRegister = dayLegs.Count(l => !RegisterLegAccountedFor(l, facts));
            var difference = statementMovements - registerMovements - classifiedMovements;
            var dayExceptions = facts.Exceptions.Where(e => !e.IsResolved &&
                facts.Sessions.Any(s => s.Id == e.CashSessionId && s.SessionDate == date)).ToList();
            var hasBlockingException = dayExceptions.Any(e =>
                e.Kind == BankExceptionKind.UnexplainedMovement || !e.BankExplanationVerified);
            var status = !opening.HasValue || !closing.HasValue || unaccounted > 0 ||
                unaccountedRegister > 0 || difference != 0 || hasBlockingException
                ? DailyBankControlStatus.NeedsReview
                : dayExceptions.Count > 0 ? DailyBankControlStatus.WithExceptions : DailyBankControlStatus.Reconciled;
            var note = dayRows.Any(r => !r.HasPreciseTimestamp)
                ? "Daily transaction-date control only; this does not certify balances at an intraday session cutoff."
                : "Daily movement control; session cutoff certification is evaluated separately.";
            controls.Add(new(date, status, opening, closing, statementMovements, registerMovements,
                classifiedMovements, difference, unaccounted, unaccountedRegister, note));
        }
        return controls;
    }

    private static decimal? BalanceBefore(
        BankStatementImport import, List<BankStatementRow> rows, DateOnly date)
    {
        if (!import.OpeningBalance.HasValue || !import.CoverageFromDate.HasValue || date < import.CoverageFromDate)
            return null;
        var balance = import.OpeningBalance.Value;
        foreach (var day in rows.Where(r => r.LedgerDate >= import.CoverageFromDate && r.LedgerDate < date)
                     .GroupBy(r => r.LedgerDate).OrderBy(g => g.Key))
        {
            var ordered = day.OrderBy(r => r.Sequence).ToList();
            if (ordered[0].RunningBalance - ordered[0].SignedAmount != balance)
                return null;
            balance = ordered[^1].RunningBalance;
        }
        var currentRows = rows.Where(r => r.LedgerDate == date).OrderBy(r => r.Sequence).ToList();
        if (currentRows.Count > 0 && currentRows[0].RunningBalance - currentRows[0].SignedAmount != balance)
            return null;
        return balance;
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
        var accountTimeZone = TimeZoneInfo.FindSystemTimeZoneById(facts.Account.TimeZoneId);
        if (!Covers(facts.Coverage, bankOpening.ObservedAt, bankClosing.ObservedAt, accountTimeZone))
            dto.BlockingReasons.Add("Verified bank-statement coverage is missing or contradictory for the complete opening-to-closing interval.");
        dto.StatementOpeningBalance = BalanceAt(facts.Coverage, bankOpening.ObservedAt, accountTimeZone);
        dto.StatementClosingBalance = BalanceAt(facts.Coverage, bankClosing.ObservedAt, accountTimeZone);
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
            if (bank.Classification is not (BankTransactionClassification.Unclassified or BankTransactionClassification.Unknown))
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
            if (!Covers(facts.Coverage, bridge.PreviousObservedAt.Value, bridge.ObservedAt, accountTimeZone))
                dto.BlockingReasons.Add("Statement coverage is missing for the interval between the previous closing and this opening.");
            foreach (var bank in facts.Banks.Where(b => InWindow(LedgerAt(b), bridge.PreviousObservedAt.Value, bridge.ObservedAt) &&
                         b.AcceptedRow!.SignedAmount != 0))
                if (!BankAccountedFor(bank, facts))
                    dto.BlockingReasons.Add($"Overnight bank occurrence {bank.Id} has not been explained.");
            var previousBankBalance = BalanceAt(facts.Coverage, bridge.PreviousObservedAt.Value, accountTimeZone);
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

    private static bool Covers(
        IEnumerable<BankStatementImport> imports, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo accountTimeZone)
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
                var previousBalance = SourceBalance(previous, boundary, inclusive: false, accountTimeZone: accountTimeZone);
                var nextBalance = SourceBalance(source, boundary, inclusive: false, accountTimeZone: accountTimeZone);
                if (!previousBalance.HasValue || previousBalance != nextBalance) return false;
            }
            previous = source;
        }
        return true;
    }

    private static decimal? BalanceAt(IEnumerable<BankStatementImport> imports, DateTimeOffset at, TimeZoneInfo accountTimeZone)
    {
        var source = LatestSource(imports, at);
        return source?.CoverageVerified == true
            ? SourceBalance(source, at, inclusive: true, accountTimeZone: accountTimeZone)
            : null;
    }

    private static BankStatementImport? LatestSource(IEnumerable<BankStatementImport> imports, DateTimeOffset at) =>
        imports.Where(i => i.CoverageFrom <= at && i.CoverageTo > at)
            .OrderByDescending(i => i.CommittedAt ?? i.CreatedAt).ThenByDescending(i => i.Id).FirstOrDefault();

    private static decimal? SourceBalance(
        BankStatementImport source, DateTimeOffset at, bool inclusive, TimeZoneInfo accountTimeZone)
    {
        if (source.OpeningBalance == null)
            return null;
        var rows = StatementValidation.EffectiveRows(source.Rows);
        var cutoffDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, accountTimeZone).DateTime);
        if (rows.Any(r => !r.HasPreciseTimestamp && r.LedgerDate == cutoffDate))
            return null;
        return source.OpeningBalance.Value + rows.Where(r => inclusive
            ? StatementValidation.LedgerTime(r) <= at : StatementValidation.LedgerTime(r) < at).Sum(r => r.SignedAmount);
    }

    private static bool BankAccountedFor(BankTransaction bank, ReviewFacts facts) =>
        bank.AcceptedRow!.SignedAmount == 0 ||
        bank.Classification != BankTransactionClassification.Unknown &&
        (bank.Classification != BankTransactionClassification.Unclassified ||
        facts.Matches.Any(m => m.BankTransactionId == bank.Id &&
            facts.Legs.Any(l => Key(l) == Key(m) && ValidFinancialMatch(bank, l))) ||
        facts.Exceptions.Any(e => e.BankTransactionId == bank.Id && e.Kind == BankExceptionKind.BankFailure &&
            e.BankExplanationVerified && e.Amount == Math.Abs(bank.AcceptedRow.SignedAmount)));

    private sealed record ReviewFlagState(
        BankReconciliationDecision Flag, BankReconciliationDecision? Response, DateOnly FromDate, DateOnly ToDate);

    private static bool RegisterLegAccountedFor(BankLegDto leg, ReviewFacts facts) =>
        facts.Matches.Any(m => m.IsActive && Key(m) == Key(leg) &&
            facts.Banks.Any(b => b.Id == m.BankTransactionId && ValidFinancialMatch(b, leg))) ||
        facts.Exceptions.Any(e => e.BankExplanationVerified && e.Kind != BankExceptionKind.UnexplainedMovement &&
            e.Amount >= Math.Abs(leg.SignedAmount) && RefersTo(e, leg));

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
