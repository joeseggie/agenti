using System.Data;
using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock,
    ISessionBalanceService balances) : IBankReconciliationService
{
    public async Task<List<BankReviewListItemDto>> GetReviewsAsync(
        string userId, long accountId, CancellationToken cancellationToken = default)
    {
        await RequireReviewerAsync(userId, accountId, cancellationToken);
        return await db.BankReconciliationReviews.AsNoTracking().Where(r => r.BankAccountId == accountId)
            .OrderByDescending(r => r.Id).Select(r =>
                new BankReviewListItemDto(r.Id, r.BankAccountId, r.FromDate, r.ToDate, r.Status, r.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankReconciliationReviewDto> CreateAsync(
        string userId, long accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        if (from > to || to > clock.Today || to.DayNumber - from.DayNumber > 366)
            throw new BankingRuleException("Choose a non-future review interval of at most one year.");
        var review = new BankReconciliationReview
        {
            BankAccountId = accountId, FromDate = from, ToDate = to,
            Status = BankReconciliationStatus.InProgress, CreatedByUserId = userId, CreatedAt = clock.UtcNow
        };
        db.BankReconciliationReviews.Add(review);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(userId, review.Id, cancellationToken);
    }

    public async Task<BankReconciliationReviewDto> GetAsync(
        string userId, long reviewId, CancellationToken cancellationToken = default)
    {
        var accountId = await ReviewAccountIdAsync(reviewId, cancellationToken);
        await RequireReviewerAsync(userId, accountId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var review = await db.BankReconciliationReviews.AsNoTracking().SingleAsync(r => r.Id == reviewId, cancellationToken);
        var result = await EvaluateAsync(review, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<List<BankCorrectionRequestDto>> GetMyCorrectionsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (!actor.AgentId.HasValue)
            return [];
        var requests = await db.CashCountCorrectionRequests.AsNoTracking().Include(r => r.CashCount)
            .Where(r => r.AgentId == actor.AgentId && r.IsOpen && r.CashCount!.CashSession!.BranchId == actor.BranchId)
            .OrderByDescending(r => r.Id).ToListAsync(cancellationToken);
        return requests.Select(MapCorrection).ToList();
    }

    private async Task<long> ReviewAccountIdAsync(long reviewId, CancellationToken cancellationToken) =>
        await db.BankReconciliationReviews.Where(r => r.Id == reviewId).Select(r => (long?)r.BankAccountId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new BankingRuleException("Bank review not found.");

    private async Task RequireReviewerAsync(string userId, long accountId, CancellationToken cancellationToken)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (actor.Role is not (UserRole.Admin or UserRole.Supervisor) ||
            !await db.BankAccounts.AnyAsync(a => a.Id == accountId && a.BranchId == actor.BranchId && a.IsActive,
                cancellationToken))
            throw new UnauthorizedAccessException("Only a branch administrator or supervisor may review bank controls.");
    }

    private async Task<ReviewFacts> LoadFactsAsync(BankReconciliationReview review, CancellationToken cancellationToken)
    {
        var account = await db.BankAccounts.AsNoTracking().Include(a => a.Wallet)
            .SingleAsync(a => a.Id == review.BankAccountId, cancellationToken);
        var agentId = account.Wallet!.AgentId;
        var sessions = await db.CashSessions.AsNoTracking()
            .Where(s => s.BranchId == account.BranchId && s.SessionDate >= review.FromDate && s.SessionDate <= review.ToDate)
            .OrderBy(s => s.SessionDate).ToListAsync(cancellationToken);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var counts = await db.CashCounts.AsNoTracking().Where(c => sessionIds.Contains(c.CashSessionId) && c.AgentId == agentId)
            .ToListAsync(cancellationToken);
        var snapshots = await db.CashCountSnapshots.AsNoTracking().Include(s => s.CashCount)
            .Where(s => sessionIds.Contains(s.CashCount!.CashSessionId) && s.CashCount.AgentId == agentId)
            .ToListAsync(cancellationToken);
        var bridges = await db.OpeningBankVariances.AsNoTracking().Include(v => v.OpeningSnapshot).ThenInclude(s => s!.CashCount)
            .Where(v => v.BankAccountId == account.Id && sessionIds.Contains(v.OpeningSnapshot!.CashCount!.CashSessionId))
            .ToListAsync(cancellationToken);
        var from = review.FromDate;
        var to = review.ToDate;
        var instants = new List<DateTimeOffset>();
        foreach (var bridge in bridges.Where(b => b.PreviousObservedAt.HasValue))
        {
            instants.Add(bridge.PreviousObservedAt!.Value);
            instants.Add(bridge.ObservedAt);
            var date = clock.GetBusinessDate(bridge.PreviousObservedAt!.Value);
            if (date < from) from = date;
        }
        foreach (var snapshot in snapshots)
        {
            foreach (var wallet in CountSnapshotCodec.Deserialize(snapshot.WalletsJson))
            {
                instants.Add(wallet.ObservedAt);
                var date = clock.GetBusinessDate(wallet.ObservedAt);
                if (date < from) from = date;
                if (date > to) to = date;
            }
        }
        var accountTimeZone = TimeZoneInfo.FindSystemTimeZoneById(account.TimeZoneId);
        var accountFromKampala = clock.GetBusinessDate(AccountDayStart(from, accountTimeZone));
        var accountToKampala = clock.GetBusinessDate(AccountDayStart(to.AddDays(1), accountTimeZone).AddTicks(-1));
        var legFrom = accountFromKampala < from ? accountFromKampala : from;
        var legTo = accountToKampala > to ? accountToKampala : to;
        var matches = await db.BankReconciliationMatches.AsNoTracking()
            .Where(m => m.IsActive && m.BankTransaction!.BankAccountId == account.Id).ToListAsync(cancellationToken);
        var exceptions = await db.BankReconciliationExceptions.AsNoTracking()
            .Where(e => e.BankAccountId == account.Id).ToListAsync(cancellationToken);
        var banks = await BankQuery(account.Id).Where(t =>
            (t.AcceptedRow!.LedgerDate >= from && t.AcceptedRow.LedgerDate <= to) ||
            (t.AcceptedRow!.OccurrenceDate >= from && t.AcceptedRow.OccurrenceDate <= to))
            .ToListAsync(cancellationToken);
        var bankIds = banks.Select(t => t.Id).ToHashSet();
        var references = References(banks).ToList();
        var linkedCustomerIds = matches.Where(m => bankIds.Contains(m.BankTransactionId) && m.CustomerMovementId.HasValue)
            .Select(m => m.CustomerMovementId!.Value).ToList();
        linkedCustomerIds.AddRange(exceptions.Where(e => e.CustomerMovementId.HasValue).Select(e => e.CustomerMovementId!.Value));
        var movements = await db.CustomerMovements.AsNoTracking()
            .Where(m => m.CustomerTransaction!.BankAccountId == account.Id && m.BankEffect != 0 &&
                !db.CustomerMovements.Any(next => next.SupersedesMovementId == m.Id) &&
                (sessionIds.Contains(m.CashSessionId) || (m.OccurrenceDate >= legFrom && m.OccurrenceDate <= legTo) ||
                 (m.BankReference != null && references.Contains(m.BankReference)) || linkedCustomerIds.Contains(m.Id)))
            .Select(m => new BankLegDto(BankLegKind.CustomerMovement, m.Id, m.CashSessionId, m.CustomerTransactionId,
                m.BankEffect, m.CustomerTransaction!.Currency, m.BankReference,
                m.CustomerIdentifier ?? m.CustomerTransaction.CustomerIdentifier, m.OccurredAt,
                m.Kind == CustomerMovementKind.BankReversal || m.Kind == CustomerMovementKind.BankPostingObserved, false))
            .ToListAsync(cancellationToken);
        var linkedRunIds = matches.Where(m => bankIds.Contains(m.BankTransactionId) && m.BankRunId.HasValue)
            .Select(m => m.BankRunId!.Value).ToList();
        linkedRunIds.AddRange(exceptions.Where(e => e.BankRunId.HasValue).Select(e => e.BankRunId!.Value));
        var bankRuns = await db.BankRuns.AsNoTracking().Where(r => r.AgentId == agentId && r.ToWalletId == account.WalletId &&
                (sessionIds.Contains(r.CashSessionId) || (r.CashSession!.SessionDate >= legFrom && r.CashSession.SessionDate <= legTo) ||
                 (r.ReceiptNumber != null && references.Contains(r.ReceiptNumber)) || linkedRunIds.Contains(r.Id)))
            .Select(r => new BankLegDto(BankLegKind.BankRun, r.Id, r.CashSessionId, null, r.Amount,
                r.Currency, r.ReceiptNumber, null, r.OccurredAt ?? r.CreatedAt, false, false))
            .ToListAsync(cancellationToken);
        var linkedAdjustmentIds = matches.Where(m => bankIds.Contains(m.BankTransactionId) && m.WalletAdjustmentId.HasValue)
            .Select(m => m.WalletAdjustmentId!.Value).ToList();
        linkedAdjustmentIds.AddRange(exceptions.Where(e => e.WalletAdjustmentId.HasValue).Select(e => e.WalletAdjustmentId!.Value));
        var adjustments = await db.WalletAdjustments.AsNoTracking().Where(a => a.AgentId == agentId && a.WalletId == account.WalletId &&
                a.Status != WalletAdjustmentStatus.Rejected &&
                (sessionIds.Contains(a.CashSessionId) || (a.CashSession!.SessionDate >= legFrom && a.CashSession.SessionDate <= legTo) ||
                 (a.BankReference != null && references.Contains(a.BankReference)) || linkedAdjustmentIds.Contains(a.Id)))
            .Select(a => new BankLegDto(BankLegKind.WalletAdjustment, a.Id, a.CashSessionId, null, -a.Amount,
                a.Currency, a.BankReference, null, a.OccurredAt ?? a.CreatedAt, false, a.Status == WalletAdjustmentStatus.Pending))
            .ToListAsync(cancellationToken);
        var legs = movements.Concat(bankRuns).Concat(adjustments).ToList();
        var legReferences = legs.Where(l => !string.IsNullOrWhiteSpace(l.Reference)).Select(l => l.Reference!).Distinct().ToList();
        var legKeys = legs.Select(Key).ToHashSet();
        var linkedBankIds = matches.Where(m => legKeys.Contains(Key(m))).Select(m => m.BankTransactionId).ToList();
        var extraBanks = await BankQuery(account.Id).Where(t =>
                (t.AcceptedRow!.TransactionNumber != null && legReferences.Contains(t.AcceptedRow.TransactionNumber)) ||
                (t.AcceptedRow!.Reference != null && legReferences.Contains(t.AcceptedRow.Reference)) ||
                linkedBankIds.Contains(t.Id)).ToListAsync(cancellationToken);
        banks.AddRange(extraBanks.Where(b => bankIds.Add(b.Id)));
        var caseIds = legs.Where(l => l.CustomerTransactionId.HasValue).Select(l => l.CustomerTransactionId!.Value).Distinct().ToList();
        var customers = await db.CustomerTransactions.AsNoTracking().Include(t => t.Movements)
            .Where(t => t.BankAccountId == account.Id && (sessionIds.Contains(t.OriginatingSessionId) || caseIds.Contains(t.Id)))
            .ToListAsync(cancellationToken);
        var corrections = await db.CashCountCorrectionRequests.AsNoTracking().Include(r => r.CashCount)
            .Where(r => sessionIds.Contains(r.CashCount!.CashSessionId) && r.AgentId == agentId)
            .ToListAsync(cancellationToken);
        var coverageMetadata = await db.BankStatementImports.AsNoTracking()
            .Where(i => i.BankAccountId == account.Id && i.Status == StatementImportStatus.Committed &&
                i.CoverageFromDate <= to)
            .Select(i => new BankStatementImport
            {
                Id = i.Id, BankAccountId = i.BankAccountId, CoverageFrom = i.CoverageFrom,
                CoverageTo = i.CoverageTo, CoverageFromDate = i.CoverageFromDate,
                CoverageToDate = i.CoverageToDate, OpeningBalance = i.OpeningBalance,
                ClosingBalance = i.ClosingBalance, CoverageVerified = i.CoverageVerified,
                CreatedAt = i.CreatedAt, CommittedAt = i.CommittedAt
            })
            .ToListAsync(cancellationToken);
        var minInstant = instants.Count > 0 ? instants.Min() : (DateTimeOffset?)null;
        var maxInstant = instants.Count > 0 ? instants.Max() : (DateTimeOffset?)null;
        var activeCoverageIds = coverageMetadata.Where(i => i.CoverageToDate >= from ||
                (minInstant.HasValue && i.CoverageTo > minInstant && i.CoverageFrom <= maxInstant))
            .Select(i => i.Id).ToList();
        var activeCoverage = await db.BankStatementImports.AsNoTracking().Include(i => i.Rows)
            .Where(i => activeCoverageIds.Contains(i.Id)).ToListAsync(cancellationToken);
        var activeById = activeCoverage.ToDictionary(i => i.Id);
        var coverage = coverageMetadata.Select(i => activeById.GetValueOrDefault(i.Id) ?? i).ToList();
        await ValidateCurrentCoverageAsync(account.Id, activeCoverage, cancellationToken);
        return new ReviewFacts(account, sessions, counts, snapshots, bridges, banks, legs, matches, customers, exceptions, corrections, coverage);
    }

    private static DateTimeOffset AccountDayStart(DateOnly date, TimeZoneInfo accountTimeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (accountTimeZone.IsInvalidTime(local))
            local = local.AddMinutes(30);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, accountTimeZone), TimeSpan.Zero);
    }

    private async Task ValidateCurrentCoverageAsync(long accountId, List<BankStatementImport> coverage, CancellationToken cancellationToken)
    {
        if (coverage.Count == 0) return;
        var from = coverage.Min(i => i.CoverageFromDate);
        var to = coverage.Max(i => i.CoverageToDate);
        var canonical = await BankQuery(accountId).Where(t => t.AcceptedRow!.LedgerDate >= from &&
            t.AcceptedRow.LedgerDate <= to).ToDictionaryAsync(t => t.Id, cancellationToken);
        var importIds = coverage.Select(i => i.Id).ToList();
        var occurrences = await db.BankStatementOccurrences.AsNoTracking()
            .Where(o => importIds.Contains(o.StatementRow!.ImportId))
            .ToDictionaryAsync(o => o.StatementRowId, o => o.BankTransactionId, cancellationToken);
        foreach (var import in coverage)
        {
            var represented = new HashSet<long>();
            foreach (var row in BankStatements.StatementValidation.EffectiveRows(import.Rows))
            {
                if (!occurrences.TryGetValue(row.Id, out var occurrenceId) ||
                    !canonical.TryGetValue(occurrenceId, out var current) ||
                    !BankStatements.StatementValidation.SameFinancialFacts(row, current.AcceptedRow!))
                {
                    import.CoverageVerified = false;
                    break;
                }
                represented.Add(occurrenceId);
            }
            var expected = canonical.Values.Where(t => LedgerAt(t) >= import.CoverageFrom &&
                LedgerAt(t) < import.CoverageTo).Select(t => t.Id).ToHashSet();
            if (!represented.SetEquals(expected))
                import.CoverageVerified = false;
        }
    }

    private IQueryable<BankTransaction> BankQuery(long accountId) =>
        db.BankTransactions.AsNoTracking().Include(t => t.AcceptedRow).ThenInclude(r => r!.Import)
            .Where(t => t.BankAccountId == accountId && t.AcceptedRow!.Import!.Status == StatementImportStatus.Committed);

    private static IEnumerable<string> References(IEnumerable<BankTransaction> entries) =>
        entries.SelectMany(t => new[] { t.AcceptedRow!.TransactionNumber, t.AcceptedRow.Reference })
            .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!).Distinct();

    private sealed record ReviewFacts(
        BankAccount Account, List<CashSession> Sessions, List<CashCount> Counts, List<CashCountSnapshot> Snapshots,
        List<OpeningBankVariance> Bridges, List<BankTransaction> Banks, List<BankLegDto> Legs,
        List<BankReconciliationMatch> Matches, List<CustomerTransaction> Customers,
        List<BankReconciliationException> Exceptions, List<CashCountCorrectionRequest> Corrections,
        List<BankStatementImport> Coverage);

    private static (BankLegKind Kind, long Id) Key(BankLegDto leg) => (leg.Kind, leg.Id);
    private static (BankLegKind Kind, long Id) Key(BankReconciliationMatch match) =>
        match.CustomerMovementId.HasValue ? (BankLegKind.CustomerMovement, match.CustomerMovementId.Value) :
        match.BankRunId.HasValue ? (BankLegKind.BankRun, match.BankRunId.Value) :
        match.WalletAdjustmentId.HasValue ? (BankLegKind.WalletAdjustment, match.WalletAdjustmentId.Value) :
        throw new BankingRuleException("A reconciliation match has no source record.");

    private static BankCorrectionRequestDto MapCorrection(CashCountCorrectionRequest request) => new(
        request.Id, request.CashCountId, request.CashCount!.CashSessionId, request.AgentId, request.Reason,
        JsonSerializer.Deserialize<List<long>>(request.AllowedMovementIdsJson)
            ?? throw new BankingRuleException("The correction request scope is invalid."),
        request.AllowMissingTransactions, request.IsOpen, request.Version);

    private static BankLedgerItemDto MapBank(BankTransaction transaction)
    {
        var row = transaction.AcceptedRow!;
        return new BankLedgerItemDto(transaction.Id, transaction.Version, row.Id, row.Import!.EvidenceFileId,
            row.OccurredAt, row.PostedAt ?? row.OccurredAt, row.SignedAmount, row.RunningBalance, row.Currency,
            row.TransactionNumber, row.Reference, row.CustomerIdentifier, row.Narration, row.HasPreciseTimestamp,
            transaction.Classification, transaction.RelatedCustomerTransactionId);
    }

    private static BankExceptionDto MapException(BankReconciliationException item) => new(
        item.Id, item.CashSessionId, item.Kind, item.Amount, item.CustomerMovementId, item.BankRunId,
        item.WalletAdjustmentId, item.BankTransactionId, item.CustomerTransactionId, item.Explanation,
        item.EvidenceFileId, item.BankExplanationVerified, item.ReflectedInClosingBalance, item.IsResolved, item.Version);
}
