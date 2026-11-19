using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed record DailyBankStatementDto(
    long Id, long BankAccountId, DateOnly StatementDate, int Revision, string FileName,
    decimal OpeningBalance, decimal TotalDebits, decimal TotalCredits, decimal ClosingBalance,
    int RowCount, DateTimeOffset UploadedAt, bool IsUsed, string? ReplacementReason,
    StatementBalanceSource BalanceSource, long? OpeningCountId);

public sealed record DailyStatementUpload(
    long BankAccountId, DateOnly StatementDate, bool ConfirmAccountAndCompleteDay,
    long? ExpectedPreviousId, string? ReplacementReason);

public interface IDailyBankStatementService
{
    Task<BankEvidenceContentDto> GetEvidenceAsync(string userId, long statementId, CancellationToken cancellationToken = default);
    Task<List<DailyBankStatementDto>> GetAsync(string userId, long accountId, DateOnly date, CancellationToken cancellationToken = default);
    Task<DailyBankStatementDto> UploadAsync(string userId, DailyStatementUpload request, string fileName,
        Stream content, CancellationToken cancellationToken = default);
}
