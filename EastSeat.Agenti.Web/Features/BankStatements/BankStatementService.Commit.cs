using System.Data;
using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed partial class BankStatementService
{
    public async Task<BankStatementPreviewDto> CorrectRowAsync(
        string userId, long importId, long rowId, CorrectStatementRowForm form, CancellationToken cancellationToken = default)
    {
        var accountId = await db.BankStatementImports.Where(i => i.Id == importId)
            .Select(i => (long?)i.BankAccountId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new BankingRuleException("Statement import not found.");
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        RequireReason(form.Reason);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var import = await db.BankStatementImports.Include(i => i.Rows).Include(i => i.BankAccount)
                .SingleAsync(i => i.Id == importId, cancellationToken);
            // A Blazor context can outlive processing performed by the separate import worker.
            await db.Entry(import).ReloadAsync(cancellationToken);
            if (import.Status != StatementImportStatus.ReadyForReview || import.Version != form.ExpectedVersion)
                throw new BankingConflictException("The import is not at the expected review revision. Refresh before correcting it.");
            var original = StatementValidation.EffectiveRows(import.Rows).SingleOrDefault(r => r.Id == rowId)
                ?? throw new BankingRuleException("Select the current version of a row in this import.");
            var entry = new ParsedStatementEntry(original.SourceLocation, original.RawData,
                form.TransactionNumber, form.Reference, form.CustomerIdentifier, form.Narration, form.Currency,
                form.SignedAmount, form.RunningBalance, form.OccurredAt, form.PostedAt, form.ValueDate, form.HasPreciseTimestamp);
            StatementValidation.ValidateEntry(entry);
            var correction = NewRow(importId, original.Sequence, entry,
                import.ProfileKey == UniversalBankCsvStatementProfile.ProfileKey);
            correction.Revision = original.Revision + 1;
            correction.SupersedesRowId = original.Id;
            correction.CorrectionReason = form.Reason.Trim();
            correction.CorrectedByUserId = userId;
            correction.CorrectedAt = clock.UtcNow;
            db.BankStatementRows.Add(correction);
            import.Version++;
            db.BankReconciliationDecisions.Add(new BankReconciliationDecision
            {
                BankAccountId = accountId, Kind = ReconciliationDecisionKind.ImportCorrected,
                DetailsJson = JsonSerializer.Serialize(new { ImportId = importId, OriginalRowId = original.Id, form.ExpectedVersion }),
                Reason = form.Reason.Trim(), PerformedByUserId = userId, PerformedAt = clock.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            var errors = StatementValidation.Check(import, StatementValidation.EffectiveRows(import.Rows),
                import.BankAccount!.AccountIdentifier, import.BankAccount.Currency, import.BankAccount.BalanceBasis);
            import.CoverageVerified = errors.Count == 0;
            import.ErrorMessage = errors.Count == 0 ? null : string.Join(" ", errors.Take(5));
            await db.SaveChangesAsync(cancellationToken);
            var result = await GetPreviewAsync(userId, importId, cancellationToken);
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

    public async Task<BankStatementImportDto> CommitAsync(
        string userId, long importId, CommitStatementForm form, CancellationToken cancellationToken = default)
    {
        var accountId = await db.BankStatementImports.Where(i => i.Id == importId)
            .Select(i => (long?)i.BankAccountId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new BankingRuleException("Statement import not found.");
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        if (form.Resolutions.Select(r => r.RowId).Distinct().Count() != form.Resolutions.Count)
            throw new BankingRuleException("Provide at most one identity decision per statement row.");
        var hash = BankingRequestIdentity.Hash(new
        {
            Version = 1, importId, form.ExpectedVersion,
            Decisions = form.Resolutions.OrderBy(r => r.RowId).Select(r =>
                new { r.RowId, r.Kind, r.ExistingTransactionId, Reason = r.Reason.Trim() })
        });
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var import = await db.BankStatementImports.Include(i => i.Rows).Include(i => i.BankAccount)
                .SingleAsync(i => i.Id == importId, cancellationToken);
            await db.Entry(import).ReloadAsync(cancellationToken);
            if (import.Status == StatementImportStatus.Committed)
            {
                if (import.CommitRequestHash != hash)
                    throw new BankingRuleException("This import was already committed using different review decisions.");
                var existingDto = await ImportDtoAsync(importId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existingDto;
            }
            if (import.Status != StatementImportStatus.ReadyForReview || import.Version != form.ExpectedVersion)
                throw new BankingConflictException("The statement preview changed. Refresh and review it before committing.");
            var profile = RequireProfile(import);
            var account = import.BankAccount!;
            if ((profile.BankIdentifier != "*" && account.BankIdentifier != profile.BankIdentifier) ||
                (profile.Key != UniversalBankCsvStatementProfile.ProfileKey && !account.ImportProfileVerified))
                throw new BankingRuleException("The statement parser is no longer configured for this account.");
            var rows = StatementValidation.EffectiveRows(import.Rows);
            var errors = StatementValidation.Check(import, rows, account.AccountIdentifier, account.Currency, account.BalanceBasis);
            if (errors.Count != 0)
                throw new BankingRuleException(string.Join(" ", errors.Take(5)));
            await ValidateCoverageSequenceAtCommitAsync(import, cancellationToken);
            if (form.Resolutions.Any(r => !rows.Any(row => row.Id == r.RowId)))
                throw new BankingRuleException("An identity decision refers to a row outside the current preview.");
            var candidates = await LoadCandidatesAsync(accountId, rows, profile, cancellationToken);
            var extraIds = form.Resolutions.Where(r => r.ExistingTransactionId.HasValue)
                .Select(r => r.ExistingTransactionId!.Value).Except(candidates.Select(c => c.Id)).ToList();
            if (extraIds.Count > 0)
                candidates.AddRange(await db.BankTransactions.Include(t => t.AcceptedRow)
                    .Where(t => t.BankAccountId == accountId && extraIds.Contains(t.Id)).ToListAsync(cancellationToken));
            var intervals = await CoveredIntervalsAsync(accountId, importId, cancellationToken);
            var usedExisting = new HashSet<long>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var changedDates = new HashSet<DateOnly>();
            foreach (var row in rows)
            {
                var identity = Identity(profile, row);
                if (identity != null && !identities.Add(identity))
                    throw new BankingRuleException("The preview repeats a supposedly unique bank identity.");
                var fingerprint = StatementValidation.Fingerprint(row);
                var resolution = form.Resolutions.SingleOrDefault(r => r.RowId == row.Id);
                if (resolution != null)
                {
                    if (!Enum.IsDefined(resolution.Kind))
                        throw new BankingRuleException("Choose a supported row identity decision.");
                    RequireReason(resolution.Reason);
                }
                BankTransaction? existing = identity == null ? null :
                    candidates.SingleOrDefault(t => t.ReliableIdentity == identity);
                if (existing != null && resolution?.ExistingTransactionId.HasValue == true &&
                    resolution.ExistingTransactionId.Value != existing.Id)
                    throw new BankingRuleException("The selected occurrence conflicts with this row's reliable bank identity.");
                if (existing == null && resolution?.ExistingTransactionId.HasValue == true)
                    existing = candidates.SingleOrDefault(t => t.Id == resolution.ExistingTransactionId.Value)
                        ?? throw new BankingRuleException("The selected bank occurrence does not belong to this account.");
                var overlap = intervals.Any(i => StatementValidation.LedgerTime(row) >= i.From &&
                    StatementValidation.LedgerTime(row) < i.To);
                var possibleDuplicate = candidates.Any(t => t.Fingerprint == fingerprint);
                if (identity == null && (overlap || possibleDuplicate) && resolution == null)
                    throw new BankingRuleException($"Row {row.Sequence} needs an explicit identity decision. Overlapping sources must not duplicate bank movements.");
                if (existing != null)
                {
                    if (!usedExisting.Add(existing.Id))
                        throw new BankingRuleException("Two rows in this statement cannot consume the same bank occurrence.");
                    if (resolution?.Kind == StatementRowResolutionKind.NewOccurrence)
                        throw new BankingRuleException("A new occurrence decision cannot reuse an existing bank identity.");
                    if (identity != null && existing.ReliableIdentity != null && identity != existing.ReliableIdentity)
                        throw new BankingRuleException("Different reliable bank identities cannot be merged.");
                    if (resolution?.Kind == StatementRowResolutionKind.AcceptRevision)
                    {
                        var priorCoverage = await db.BankStatementImports.AsNoTracking()
                            .SingleAsync(i => i.Id == existing.AcceptedRow!.ImportId, cancellationToken);
                        if (priorCoverage.CoverageFromDate.HasValue) changedDates.Add(priorCoverage.CoverageFromDate.Value);
                        if (priorCoverage.CoverageToDate.HasValue) changedDates.Add(priorCoverage.CoverageToDate.Value);
                        changedDates.Add(clock.GetBusinessDate(StatementValidation.LedgerTime(existing.AcceptedRow!)));
                        changedDates.Add(clock.GetBusinessDate(StatementValidation.LedgerTime(row)));
                        var oldRowId = existing.AcceptedRowId;
                        existing.AcceptedRowId = row.Id;
                        existing.AcceptedRow = row;
                        existing.Fingerprint = fingerprint;
                        existing.Classification = BankTransactionClassification.Unclassified;
                        existing.ClassificationReason = null;
                        existing.RelatedCustomerTransactionId = null;
                        existing.Version++;
                        var staleMatches = await db.BankReconciliationMatches
                            .Where(m => m.BankTransactionId == existing.Id && m.IsActive).ToListAsync(cancellationToken);
                        foreach (var match in staleMatches)
                        {
                            match.IsActive = false;
                            match.Version++;
                        }
                        AddImportDecision(userId, accountId, importId, row.Id, existing.Id,
                            resolution.Reason, new { OldRowId = oldRowId, NewRowId = row.Id });
                    }
                    else if (identity != null ? !StatementValidation.SameFacts(existing.AcceptedRow!, row) :
                             !StatementValidation.SameFinancialFacts(existing.AcceptedRow!, row))
                    {
                        throw new BankingRuleException($"Row {row.Sequence} conflicts with the accepted occurrence. An explicit source revision is required.");
                    }
                    else if (identity == null && resolution?.Kind != StatementRowResolutionKind.LinkExisting)
                    {
                        throw new BankingRuleException("A weak-identity overlap requires an explicit link or new-occurrence decision.");
                    }
                    db.BankStatementOccurrences.Add(new BankStatementOccurrence
                    {
                        BankTransactionId = existing.Id, StatementRowId = row.Id
                    });
                    if (resolution != null && resolution.Kind != StatementRowResolutionKind.AcceptRevision)
                        AddImportDecision(userId, accountId, importId, row.Id, existing.Id, resolution.Reason, new { Linked = true });
                }
                else
                {
                    if (resolution != null && resolution.Kind != StatementRowResolutionKind.NewOccurrence)
                        throw new BankingRuleException("Select an existing occurrence before linking or revising it.");
                    var occurrence = new BankTransaction
                    {
                        BankAccountId = accountId, AcceptedRowId = row.Id, ReliableIdentity = identity,
                        Fingerprint = fingerprint, CreatedAt = clock.UtcNow
                    };
                    db.BankTransactions.Add(occurrence);
                    await db.SaveChangesAsync(cancellationToken);
                    usedExisting.Add(occurrence.Id);
                    db.BankStatementOccurrences.Add(new BankStatementOccurrence
                    {
                        BankTransactionId = occurrence.Id, StatementRowId = row.Id
                    });
                    changedDates.Add(clock.GetBusinessDate(StatementValidation.LedgerTime(row)));
                    if (resolution != null)
                        AddImportDecision(userId, accountId, importId, row.Id, occurrence.Id,
                            resolution.Reason, new { NewOccurrence = true });
                }
            }
            var acceptedInRange = await db.BankTransactions.Include(t => t.AcceptedRow)
                .Where(t => t.BankAccountId == accountId && t.AcceptedRow!.LedgerDate >= import.CoverageFromDate &&
                    t.AcceptedRow.LedgerDate <= import.CoverageToDate).ToListAsync(cancellationToken);
            var omitted = acceptedInRange.Any(t => StatementValidation.LedgerTime(t.AcceptedRow!) >= import.CoverageFrom &&
                StatementValidation.LedgerTime(t.AcceptedRow!) < import.CoverageTo && !usedExisting.Contains(t.Id));
            if (omitted)
            {
                changedDates.Add(import.CoverageFromDate!.Value);
                changedDates.Add(import.CoverageToDate!.Value);
            }
            if (changedDates.Count > 0)
            {
                var from = changedDates.Min();
                var to = changedDates.Max();
                var affected = await db.BankReconciliationReviews.Where(r => r.BankAccountId == accountId &&
                    ((r.FromDate <= to && r.ToDate >= from) ||
                     db.OpeningBankVariances.Any(b => b.BankAccountId == accountId &&
                         b.OpeningSnapshot!.CashCount!.CashSession!.SessionDate >= r.FromDate &&
                         b.OpeningSnapshot.CashCount.CashSession.SessionDate <= r.ToDate &&
                         b.PreviousClosingCount != null && b.PreviousClosingCount.CashSession!.SessionDate >= from &&
                         b.PreviousClosingCount.CashSession.SessionDate <= to))).ToListAsync(cancellationToken);
                foreach (var review in affected)
                {
                    review.Version++;
                    if (review.Status is BankReconciliationStatus.Reconciled or BankReconciliationStatus.WithExceptions)
                    {
                        review.Status = BankReconciliationStatus.NeedsReview;
                        db.BankReconciliationDecisions.Add(new BankReconciliationDecision
                        {
                            BankAccountId = accountId, ReviewId = review.Id, Kind = ReconciliationDecisionKind.ReviewInvalidated,
                            DetailsJson = JsonSerializer.Serialize(new { ImportId = importId }),
                            Reason = "New or revised bank evidence affects this review.",
                            PerformedByUserId = userId, PerformedAt = clock.UtcNow
                        });
                    }
                }
            }
            account.Version++;
            import.Status = StatementImportStatus.Committed;
            import.CoverageVerified = !omitted;
            import.ErrorMessage = omitted
                ? "This complete-period source omits previously accepted bank occurrences. Its rows are retained, but its coverage cannot certify a closing until corrected bank evidence resolves the contradiction."
                : null;
            import.CommittedAt = clock.UtcNow;
            import.CommitRequestHash = hash;
            import.Version++;
            await db.SaveChangesAsync(cancellationToken);
            var result = await ImportDtoAsync(importId, cancellationToken);
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

    private void AddImportDecision(
        string userId, long accountId, long importId, long rowId, long occurrenceId, string reason, object details) =>
        db.BankReconciliationDecisions.Add(new BankReconciliationDecision
        {
            BankAccountId = accountId, BankTransactionId = occurrenceId, Kind = ReconciliationDecisionKind.ImportCorrected,
            DetailsJson = JsonSerializer.Serialize(new { ImportId = importId, RowId = rowId, Details = details }),
            Reason = reason.Trim(), PerformedByUserId = userId, PerformedAt = clock.UtcNow
        });

    private async Task ValidateCoverageSequenceAtCommitAsync(
        BankStatementImport import, CancellationToken cancellationToken)
    {
        if (!import.CoverageFromDate.HasValue || !import.CoverageToDate.HasValue)
            throw new BankingRuleException("The statement preview has no valid transaction-date coverage.");

        var from = import.CoverageFromDate.Value;
        var to = import.CoverageToDate.Value;
        var revisesCoverage = await db.BankStatementImports.AnyAsync(i =>
            i.BankAccountId == import.BankAccountId && i.Id != import.Id &&
            i.Status == StatementImportStatus.Committed &&
            i.CoverageFromDate == from && i.CoverageToDate == to, cancellationToken);
        var overlapsExistingCoverage = await db.BankStatementImports.AnyAsync(i =>
            i.BankAccountId == import.BankAccountId && i.Id != import.Id &&
            i.Status == StatementImportStatus.Committed &&
            i.CoverageFromDate <= to && i.CoverageToDate >= from, cancellationToken);
        if (overlapsExistingCoverage && !revisesCoverage)
            throw new BankingRuleException("The selected statement period overlaps committed coverage; submit a revision for the exact existing period.");
        var predecessor = await db.BankStatementImports.Where(i =>
                i.BankAccountId == import.BankAccountId && i.Id != import.Id &&
                i.Status == StatementImportStatus.Committed && i.CoverageToDate < from)
            .OrderByDescending(i => i.CoverageToDate).FirstOrDefaultAsync(cancellationToken);

        if (predecessor != null)
        {
            if (!predecessor.CoverageVerified || predecessor.CoverageToDate!.Value.AddDays(1) != from)
                throw new BankingRuleException("The immediately preceding statement coverage must be verified before committing this period.");
            if (!predecessor.ClosingBalance.HasValue || predecessor.ClosingBalance != import.OpeningBalance)
                throw new BankingRuleException("The statement opening balance no longer matches the latest committed closing balance.");
        }
        else if (!revisesCoverage && await db.BankStatementImports.AnyAsync(i =>
                     i.BankAccountId == import.BankAccountId && i.Id != import.Id &&
                     i.Status == StatementImportStatus.Committed && i.CoverageVerified &&
                     i.CoverageFromDate > to, cancellationToken))
        {
            throw new BankingRuleException("Reconcile statement dates in order; an earlier period is already committed after this date range.");
        }

        if (!revisesCoverage && await db.BankStatementImports.AnyAsync(i =>
                i.BankAccountId == import.BankAccountId && i.Id != import.Id &&
                (i.Status == StatementImportStatus.Uploaded || i.Status == StatementImportStatus.Processing ||
                 i.Status == StatementImportStatus.ReadyForReview) &&
                i.RequestedFromDate < from, cancellationToken))
            throw new BankingRuleException("Commit the earlier staged statement period before this one.");
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 2000)
            throw new BankingRuleException("Provide a review reason between 10 and 2,000 characters.");
    }
}
