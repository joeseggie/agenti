using System.Data;
using System.Security.Cryptography;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class DailyBankStatementService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock,
    UniversalBankCsvStatementProfile parser, IOptions<BankEvidenceOptions> options) : IDailyBankStatementService
{
    public async Task<BankEvidenceContentDto> GetEvidenceAsync(
        string userId, long statementId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (actor.Role is not (EastSeat.Agenti.Shared.Domain.Enums.UserRole.Admin or EastSeat.Agenti.Shared.Domain.Enums.UserRole.Supervisor))
            throw new UnauthorizedAccessException("Only a branch reviewer may read bank statement evidence.");
        var statement = await db.DailyBankStatements.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == statementId && s.BankAccount!.BranchId == actor.BranchId, cancellationToken)
            ?? throw new UnauthorizedAccessException("You do not have access to this statement.");
        return new BankEvidenceContentDto(new BankEvidenceInfoDto(statement.Id, statement.BankAccountId,
            statement.FileName, UniversalBankCsvStatementProfile.CsvContentType, statement.Content.LongLength,
            true, statement.UploadedAt), statement.Content);
    }

    public async Task<List<DailyBankStatementDto>> GetAsync(
        string userId, long accountId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        return await db.DailyBankStatements.AsNoTracking()
            .Where(s => s.BankAccountId == accountId && s.StatementDate == date)
            .OrderByDescending(s => s.Revision)
            .Select(s => new DailyBankStatementDto(s.Id, s.BankAccountId, s.StatementDate, s.Revision, s.FileName,
                s.OpeningBalance, s.TotalDebits, s.TotalCredits, s.ClosingBalance, s.Rows.Count, s.UploadedAt,
                db.ClosingWalletResults.Any(r => r.StatementId == s.Id), s.ReplacementReason,
                s.BalanceSource, s.OpeningCountId))
            .ToListAsync(cancellationToken);
    }

    public async Task<DailyBankStatementDto> UploadAsync(
        string userId, DailyStatementUpload request, string fileName, Stream content,
        CancellationToken cancellationToken = default)
    {
        var scope = await access.RequireAccountAsync(userId, request.BankAccountId, adminOnly: true, cancellationToken);
        if (!request.ConfirmAccountAndCompleteDay)
            throw new BankingRuleException("Confirm that the file belongs to this bank account and covers the complete selected day.");
        if (request.StatementDate == default || request.StatementDate > clock.Today)
            throw new BankingRuleException("Select one valid, non-future statement date.");
        var safeName = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (safeName.Length is 0 or > 256 || safeName.Any(char.IsControl) ||
            !string.Equals(Path.GetExtension(safeName), ".csv", StringComparison.OrdinalIgnoreCase))
            throw new BankingRuleException("Upload a CSV using the shipped Agenti bank statement template.");
        var limit = options.Value.MaxFileBytes;
        if (limit <= 0 || limit > int.MaxValue)
            throw new BankingRuleException("The statement file size limit is not configured correctly.");
        using var buffer = new MemoryStream();
        var block = new byte[16384];
        int length;
        while ((length = await content.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + length > limit)
                throw new BankingRuleException($"The file exceeds the {limit:N0}-byte upload limit.");
            await buffer.WriteAsync(block.AsMemory(0, length), cancellationToken);
        }
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var account = await db.BankAccounts.SingleAsync(a => a.Id == request.BankAccountId, cancellationToken);
            await db.Entry(account).ReloadAsync(cancellationToken);
            var previous = await db.DailyBankStatements.AsNoTracking()
                .Where(s => s.BankAccountId == account.Id && s.StatementDate == request.StatementDate)
                .OrderByDescending(s => s.Revision).FirstOrDefaultAsync(cancellationToken);
            if (previous?.ContentHash == hash)
            {
                await transaction.CommitAsync(cancellationToken);
                return (await GetAsync(userId, account.Id, request.StatementDate, cancellationToken))[0];
            }
            if (await db.ClosingWalletResults.AnyAsync(r => r.BankAccountId == account.Id &&
                    db.DailyBankStatements.Any(s => s.Id == r.StatementId && s.StatementDate == request.StatementDate), cancellationToken))
                throw new BankingRuleException("This day's statement has already been used in a recorded reconciliation and cannot be replaced.");
            if (request.ExpectedPreviousId != previous?.Id)
                throw new BankingConflictException("The selected statement revision changed. Refresh before uploading.");
            if (previous != null && (string.IsNullOrWhiteSpace(request.ReplacementReason) ||
                    request.ReplacementReason.Trim().Length is < 10 or > 2000))
                throw new BankingRuleException("Replacing a statement requires a reason between 10 and 2,000 characters.");

            var opening = account.AllowMissingRunningBalances
                ? await FindApprovedOpeningAsync(scope, request.StatementDate, cancellationToken) : null;
            buffer.Position = 0;
            ParsedStatement parsed;
            try
            {
                parsed = await parser.ParseAsync(buffer, UniversalBankCsvStatementProfile.CsvContentType,
                    new StatementAccountContext(account.AccountIdentifier, account.Currency, account.TimeZoneId,
                        account.BalanceBasis, request.StatementDate, request.StatementDate, opening?.Amount,
                        account.AllowMissingRunningBalances), cancellationToken);
            }
            catch (StatementParsingException exception)
            {
                throw new BankingRuleException(exception.Message);
            }
            if (parsed.Entries.Count == 0)
                throw new BankingRuleException("Empty statements are not supported. Upload a complete single-day statement with transactions.");
            const decimal maximum = 9_999_999_999_999_999.99m;
            if (parsed.Entries.Any(e => e.ValueDate != request.StatementDate ||
                    Math.Abs(e.SignedAmount) > maximum || Math.Abs(e.RunningBalance) > maximum ||
                    e.Narration.Length > 4000 || e.Reference?.Length > 256))
                throw new BankingRuleException("All rows must match the selected date and supported amount/text limits.");
            var debits = parsed.Entries.Where(e => e.SignedAmount < 0).Sum(e => -e.SignedAmount);
            var credits = parsed.Entries.Where(e => e.SignedAmount > 0).Sum(e => e.SignedAmount);
            if (debits > maximum || credits > maximum || Math.Abs(parsed.OpeningBalance) > maximum)
                throw new BankingRuleException("Statement totals exceed the supported monetary range.");
            var zone = TimeZoneInfo.FindSystemTimeZoneById(account.TimeZoneId);
            var statement = new DailyBankStatement
            {
                BankAccountId = account.Id, StatementDate = request.StatementDate,
                Revision = (previous?.Revision ?? 0) + 1, SupersedesId = previous?.Id,
                ReplacementReason = previous == null ? null : request.ReplacementReason!.Trim(),
                Currency = account.Currency, AccountIdentifier = account.AccountIdentifier,
                FileName = safeName, ContentHash = hash, Content = bytes,
                SchemaHash = parser.SchemaHash, SchemaVersion = parser.Version,
                BalanceSource = parsed.BalanceSource,
                OpeningCountId = parsed.BalanceSource == StatementBalanceSource.ApprovedOpening ? opening?.CountId : null,
                OpeningBalance = parsed.OpeningBalance, ClosingBalance = parsed.ClosingBalance,
                TotalDebits = debits, TotalCredits = credits,
                UploadedByUserId = userId, UploadedAt = clock.UtcNow,
                Rows = parsed.Entries.Select((e, index) => new DailyBankStatementRow
                {
                    Sequence = index + 1, TransactionDate = request.StatementDate,
                    TransactionTime = e.HasPreciseTimestamp
                        ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.OccurredAt, zone).DateTime) : null,
                    Reference = e.Reference, Description = e.Narration,
                    Debit = e.SignedAmount < 0 ? -e.SignedAmount : 0,
                    Credit = e.SignedAmount > 0 ? e.SignedAmount : 0, RunningBalance = e.RunningBalance
                }).ToList()
            };
            account.Version++;
            db.DailyBankStatements.Add(statement);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DailyBankStatementDto(statement.Id, account.Id, statement.StatementDate, statement.Revision,
                safeName, statement.OpeningBalance, debits, credits, statement.ClosingBalance,
                statement.Rows.Count, statement.UploadedAt, false, statement.ReplacementReason,
                statement.BalanceSource, statement.OpeningCountId);
        }
        catch
        {
            await BankingConcurrency.RollbackAfterConflictAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<(long CountId, decimal Amount)?> FindApprovedOpeningAsync(
        BankAccountScope account, DateOnly date, CancellationToken cancellationToken)
    {
        var openings = await db.CashCounts.AsNoTracking().Where(c => c.IsOpening &&
            c.AgentId == account.AgentId && c.CountDate == date && c.Status == CashCountStatus.Approved &&
            c.CashSession!.BranchId == account.BranchId && c.CashSession.SessionDate == date)
            .Select(c => c.Id).Take(2).ToListAsync(cancellationToken);
        if (openings.Count > 1)
            throw new BankingRuleException("Multiple approved openings exist for this agent and statement date; investigate before uploading.");
        if (openings.Count == 0)
            return null;
        var wallets = await CashCountWalletReader.ReadAsync(db, openings[0], cancellationToken);
        var wallet = wallets.SingleOrDefault(w => w.Id == account.WalletId &&
            w.Type == WalletTypeEnum.Bank && w.Currency == account.Currency &&
            (!w.BankAccountId.HasValue || w.BankAccountId == account.Id));
        return wallet == null ? null : (openings[0], wallet.Amount);
    }
}
