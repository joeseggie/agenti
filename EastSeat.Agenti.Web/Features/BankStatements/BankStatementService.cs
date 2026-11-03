using System.Data;
using System.Linq.Expressions;
using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed partial class BankStatementService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock,
    StatementProfileCatalog profiles, IOptions<BankStatementOptions> options,
    ILogger<BankStatementService> logger) : IBankStatementService
{
    public async Task<List<BankStatementImportDto>> GetImportsAsync(
        string userId, long accountId, CancellationToken cancellationToken = default)
    {
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        return await db.BankStatementImports.AsNoTracking().Where(i => i.BankAccountId == accountId)
            .OrderByDescending(i => i.Id).Select(ImportProjection).ToListAsync(cancellationToken);
    }

    public async Task<BankStatementImportDto> StartAsync(
        string userId, long accountId, long evidenceId, bool reprocess = false, CancellationToken cancellationToken = default)
    {
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var file = await db.BankEvidenceFiles.AsNoTracking().Where(f => f.Id == evidenceId &&
                    f.BankAccountId == accountId && f.IsStatement)
                .Select(f => new { f.Id, f.ContentHash, f.ContentType }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new BankingRuleException("Select statement evidence belonging to this account.");
            var existing = await db.BankStatementImports.AsNoTracking()
                .Where(i => i.BankAccountId == accountId && i.EvidenceFile!.ContentHash == file.ContentHash)
                .OrderByDescending(i => i.Id).FirstOrDefaultAsync(cancellationToken);
            if (existing != null && !reprocess)
            {
                var existingDto = await ImportDtoAsync(existing.Id, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existingDto;
            }
            if (existing?.Status is StatementImportStatus.Uploaded or StatementImportStatus.Processing)
                throw new BankingRuleException("This source file is already queued or being processed.");
            var account = await db.BankAccounts.SingleAsync(a => a.Id == accountId, cancellationToken);
            var profile = profiles.Find(account.ImportProfileKey);
            var usable = account.ImportProfileVerified && profile?.IsVerified == true &&
                         profile.BankIdentifier == account.BankIdentifier &&
                         profile.SupportedContentTypes.Contains(file.ContentType);
            var import = new BankStatementImport
            {
                BankAccountId = accountId, EvidenceFileId = existing?.EvidenceFileId ?? evidenceId,
                Revision = existing == null ? 0 : existing.Revision + 1, PreviousImportId = existing?.Id,
                ProfileKey = profile?.Key ?? string.Empty, ProfileVersion = profile?.Version ?? 0,
                Status = usable ? StatementImportStatus.Uploaded : StatementImportStatus.Failed,
                ErrorMessage = usable ? null : "No verified profile supports this bank and file format. Verify representative bank samples before parsing or activation.",
                UploadedByUserId = userId, CreatedAt = clock.UtcNow
            };
            account.Version++;
            db.BankStatementImports.Add(import);
            await db.SaveChangesAsync(cancellationToken);
            var result = await ImportDtoAsync(import.Id, cancellationToken);
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

    public async Task<BankStatementPreviewDto> GetPreviewAsync(
        string userId, long importId, CancellationToken cancellationToken = default)
    {
        var import = await db.BankStatementImports.AsNoTracking().Include(i => i.BankAccount).Include(i => i.Rows)
            .SingleOrDefaultAsync(i => i.Id == importId, cancellationToken)
            ?? throw new BankingRuleException("Statement import not found.");
        await access.RequireAccountAsync(userId, import.BankAccountId, adminOnly: true, cancellationToken);
        var dto = await ImportDtoAsync(importId, cancellationToken);
        if (import.Status is StatementImportStatus.Uploaded or StatementImportStatus.Processing or StatementImportStatus.Failed)
            return new BankStatementPreviewDto(dto, [], import.ErrorMessage == null ? [] : [import.ErrorMessage]);
        var rows = StatementValidation.EffectiveRows(import.Rows);
        if (import.Status == StatementImportStatus.Committed)
        {
            var occurrences = await db.BankStatementOccurrences.AsNoTracking()
                .Where(o => o.StatementRow!.ImportId == import.Id)
                .Select(o => new { o.StatementRowId, o.BankTransactionId, o.BankTransaction!.ReliableIdentity })
                .ToDictionaryAsync(o => o.StatementRowId, cancellationToken);
            var committedRows = rows.Select(row =>
            {
                occurrences.TryGetValue(row.Id, out var occurrence);
                return new StatementRowDto(row.Id, row.Sequence, row.SourceLocation, row.TransactionNumber,
                    row.Reference, row.CustomerIdentifier, row.Narration, row.Currency, row.SignedAmount,
                    row.RunningBalance, row.OccurredAt, row.PostedAt, row.ValueDate, row.HasPreciseTimestamp,
                    occurrence?.ReliableIdentity, occurrence == null ? [] : [occurrence.BankTransactionId], false);
            }).ToList();
            return new BankStatementPreviewDto(dto, committedRows,
                string.IsNullOrWhiteSpace(import.ErrorMessage) ? [] : [import.ErrorMessage]);
        }
        var profile = RequireProfile(import);
        var candidates = await LoadCandidatesAsync(import.BankAccountId, rows, profile, cancellationToken);
        var coveredIntervals = await CoveredIntervalsAsync(import.BankAccountId, import.Id, cancellationToken);
        var previews = rows.Select(row =>
        {
            var identity = Identity(profile, row);
            var fingerprint = StatementValidation.Fingerprint(row);
            var possible = candidates.Where(c => identity != null
                ? c.ReliableIdentity == identity : c.Fingerprint == fingerprint).ToList();
            var decision = identity != null
                ? possible.Any(p => !StatementValidation.SameFacts(p.AcceptedRow!, row))
                : possible.Count > 0 || coveredIntervals.Any(i => StatementValidation.LedgerTime(row) >= i.From &&
                    StatementValidation.LedgerTime(row) < i.To);
            return new StatementRowDto(row.Id, row.Sequence, row.SourceLocation, row.TransactionNumber,
                row.Reference, row.CustomerIdentifier, row.Narration, row.Currency, row.SignedAmount,
                row.RunningBalance, row.OccurredAt, row.PostedAt, row.ValueDate, row.HasPreciseTimestamp,
                identity, possible.Select(p => p.Id).ToList(), decision);
        }).ToList();
        return new BankStatementPreviewDto(dto, previews,
            StatementValidation.Check(import, rows, import.BankAccount!.AccountIdentifier,
                import.BankAccount.Currency, import.BankAccount.BalanceBasis));
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (settings.MaximumRows <= 0 || settings.ProcessingTimeoutSeconds <= 0 ||
            settings.LeaseSeconds <= settings.ProcessingTimeoutSeconds)
            throw new BankingRuleException("Statement-processing row limits, timeout and lease duration must be configured consistently.");
        long importId;
        Guid leaseId;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                var import = await db.BankStatementImports.Where(i => i.Status == StatementImportStatus.Uploaded)
                    .OrderBy(i => i.Id).FirstOrDefaultAsync(cancellationToken);
                if (import == null)
                {
                    var processing = await db.BankStatementImports.Where(i => i.Status == StatementImportStatus.Processing)
                        .OrderBy(i => i.Id).ToListAsync(cancellationToken);
                    import = processing.FirstOrDefault(i => !i.LeaseExpiresAt.HasValue || i.LeaseExpiresAt < clock.UtcNow);
                }
                if (import == null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }
                leaseId = Guid.NewGuid();
                import.LeaseId = leaseId;
                import.LeaseExpiresAt = clock.UtcNow.AddSeconds(settings.LeaseSeconds);
                import.Status = StatementImportStatus.Processing;
                import.ErrorMessage = null;
                import.Version++;
                importId = import.Id;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return false;
            }
        }

        try
        {
            db.ChangeTracker.Clear();
            var import = await db.BankStatementImports.AsNoTracking().Include(i => i.BankAccount).Include(i => i.EvidenceFile)
                .SingleAsync(i => i.Id == importId, cancellationToken);
            var profile = RequireProfile(import);
            var account = import.BankAccount!;
            if (profile.BankIdentifier != account.BankIdentifier || profile.BalanceBasis != account.BalanceBasis ||
                !account.ImportProfileVerified || !profile.SupportedContentTypes.Contains(import.EvidenceFile!.ContentType))
                throw new StatementParsingException("The verified profile no longer agrees with the account or source file.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.ProcessingTimeoutSeconds));
            await using var content = new MemoryStream(import.EvidenceFile.Content, writable: false);
            var parsed = await profile.ParseAsync(content, import.EvidenceFile.ContentType,
                new StatementAccountContext(account.AccountIdentifier, account.Currency, account.TimeZoneId, account.BalanceBasis),
                timeout.Token);
            if (parsed.Entries.Count > settings.MaximumRows)
                throw new StatementParsingException("The statement exceeds the configured row limit. Export a smaller interval.");
            if (parsed.CoverageFrom == default || parsed.CoverageTo <= parsed.CoverageFrom ||
                parsed.CoverageTo > clock.UtcNow || parsed.AccountIdentifier.Length > 256 || parsed.Currency.Length != 3 ||
                Math.Abs(parsed.OpeningBalance) > 9_999_999_999_999_999.99m ||
                Math.Abs(parsed.ClosingBalance) > 9_999_999_999_999_999.99m ||
                decimal.Round(parsed.OpeningBalance, 2) != parsed.OpeningBalance ||
                decimal.Round(parsed.ClosingBalance, 2) != parsed.ClosingBalance)
                throw new StatementParsingException("The statement header has invalid balances, account metadata, or unsupported/future coverage.");
            var rows = new List<BankStatementRow>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < parsed.Entries.Count; index++)
            {
                var entry = parsed.Entries[index];
                StatementValidation.ValidateEntry(entry);
                var row = NewRow(import.Id, index + 1, entry);
                var identity = Identity(profile, row);
                if (identity != null && !identities.Add(identity))
                    throw new StatementParsingException("The source repeats a supposedly unique bank identifier. The bank profile needs review; no financial rows were committed.");
                rows.Add(row);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var current = await db.BankStatementImports.SingleAsync(i => i.Id == importId, cancellationToken);
            if (current.LeaseId != leaseId || current.Status != StatementImportStatus.Processing)
                return false;
            current.ReportedAccountIdentifier = parsed.AccountIdentifier.Trim();
            current.Currency = parsed.Currency;
            current.BalanceBasis = parsed.BalanceBasis;
            current.CoverageFrom = parsed.CoverageFrom.ToUniversalTime();
            current.CoverageTo = parsed.CoverageTo.ToUniversalTime();
            current.CoverageFromDate = clock.GetBusinessDate(parsed.CoverageFrom);
            current.CoverageToDate = clock.GetBusinessDate(parsed.CoverageTo.AddTicks(-1));
            current.OpeningBalance = parsed.OpeningBalance;
            current.ClosingBalance = parsed.ClosingBalance;
            current.CoverageDeclaredComplete = parsed.CompleteCoverage;
            var errors = StatementValidation.Check(current, rows, account.AccountIdentifier, account.Currency, account.BalanceBasis);
            current.CoverageVerified = errors.Count == 0;
            current.ErrorMessage = errors.Count == 0 ? null : string.Join(" ", errors.Take(5));
            current.Status = StatementImportStatus.ReadyForReview;
            current.LeaseId = null;
            current.LeaseExpiresAt = null;
            current.Version++;
            db.BankStatementRows.AddRange(rows);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Statement import {ImportId} failed with {ExceptionType}.", importId, exception.GetType().Name);
            db.ChangeTracker.Clear();
            var failed = await db.BankStatementImports.SingleAsync(i => i.Id == importId, CancellationToken.None);
            if (failed.LeaseId == leaseId && failed.Status == StatementImportStatus.Processing)
            {
                failed.Status = StatementImportStatus.Failed;
                failed.ErrorMessage = exception is StatementParsingException parseError ? parseError.Message :
                    exception is OperationCanceledException ? "Statement processing exceeded its configured time limit." :
                    "Statement processing failed. The original source is retained; no financial import was committed.";
                failed.LeaseId = null;
                failed.LeaseExpiresAt = null;
                failed.Version++;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            return true;
        }
    }

    private static readonly Expression<Func<BankStatementImport, BankStatementImportDto>> ImportProjection = i =>
        new BankStatementImportDto(i.Id, i.BankAccountId, i.EvidenceFileId, i.EvidenceFile!.FileName, i.Status,
            i.ProfileKey, i.ProfileVersion, i.Version, i.Revision, i.CoverageFrom, i.CoverageTo,
            i.OpeningBalance, i.ClosingBalance, i.CoverageVerified, i.ErrorMessage, i.CreatedAt);

    private Task<BankStatementImportDto> ImportDtoAsync(long id, CancellationToken cancellationToken) =>
        db.BankStatementImports.AsNoTracking().Where(i => i.Id == id)
            .Select(ImportProjection).SingleAsync(cancellationToken);

    private IStatementImportProfile RequireProfile(BankStatementImport import)
    {
        var profile = profiles.Find(import.ProfileKey);
        if (profile?.IsVerified != true || profile.Version != import.ProfileVersion)
            throw new StatementParsingException("The verified profile version used by this import is not available.");
        return profile;
    }

    private static string? Identity(IStatementImportProfile profile, BankStatementRow row)
    {
        var value = profile.GetReliableIdentity(StatementValidation.ToParsed(row));
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.Length > 400)
            throw new StatementParsingException("The profile generated an oversized bank identity.");
        return value;
    }

    private async Task<List<BankTransaction>> LoadCandidatesAsync(
        long accountId, IReadOnlyList<BankStatementRow> rows, IStatementImportProfile profile, CancellationToken cancellationToken)
    {
        var identities = rows.Select(r => Identity(profile, r)).Where(i => i != null).Select(i => i!).Distinct().ToList();
        var fingerprints = rows.Select(StatementValidation.Fingerprint).Distinct().ToList();
        return await db.BankTransactions.Include(t => t.AcceptedRow)
            .Where(t => t.BankAccountId == accountId &&
                ((t.ReliableIdentity != null && identities.Contains(t.ReliableIdentity)) || fingerprints.Contains(t.Fingerprint)))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<(DateTimeOffset From, DateTimeOffset To)>> CoveredIntervalsAsync(
        long accountId, long excludingImportId, CancellationToken cancellationToken)
    {
        var intervals = await db.BankStatementImports.AsNoTracking()
            .Where(i => i.BankAccountId == accountId && i.Id != excludingImportId &&
                i.Status == StatementImportStatus.Committed && i.CoverageVerified &&
                i.CoverageFrom.HasValue && i.CoverageTo.HasValue)
            .Select(i => new { From = i.CoverageFrom!.Value, To = i.CoverageTo!.Value })
            .ToListAsync(cancellationToken);
        return intervals.Select(i => (i.From, i.To)).ToList();
    }

    private BankStatementRow NewRow(long importId, int sequence, ParsedStatementEntry entry) => new()
    {
        ImportId = importId, Sequence = sequence, SourceLocation = entry.SourceLocation, RawData = entry.RawData,
        TransactionNumber = entry.TransactionNumber?.Trim(), Reference = entry.Reference?.Trim(),
        CustomerIdentifier = entry.CustomerIdentifier?.Trim(), Narration = entry.Narration,
        Currency = entry.Currency, SignedAmount = entry.SignedAmount, RunningBalance = entry.RunningBalance,
        OccurredAt = entry.OccurredAt.ToUniversalTime(), PostedAt = entry.PostedAt?.ToUniversalTime(),
        OccurrenceDate = clock.GetBusinessDate(entry.OccurredAt),
        LedgerDate = clock.GetBusinessDate(entry.PostedAt ?? entry.OccurredAt),
        ValueDate = entry.ValueDate, HasPreciseTimestamp = entry.HasPreciseTimestamp
    };
}
