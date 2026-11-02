using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class BankStatementOptions
{
    public int MaximumRows { get; set; } = 10_000;
    public int ProcessingTimeoutSeconds { get; set; } = 120;
    public int LeaseSeconds { get; set; } = 300;
}

public sealed record BankStatementImportDto(
    long Id, long BankAccountId, long EvidenceFileId, string FileName, StatementImportStatus Status,
    string ProfileKey, int ProfileVersion, int Version, int Revision,
    DateTimeOffset? CoverageFrom, DateTimeOffset? CoverageTo, decimal? OpeningBalance,
    decimal? ClosingBalance, bool CoverageVerified, string? ErrorMessage, DateTimeOffset CreatedAt);

public sealed record StatementRowDto(
    long Id, int Sequence, string SourceLocation, string? TransactionNumber, string? Reference,
    string? CustomerIdentifier, string Narration, string Currency, decimal SignedAmount, decimal RunningBalance,
    DateTimeOffset OccurredAt, DateTimeOffset? PostedAt, DateOnly? ValueDate, bool HasPreciseTimestamp,
    string? ReliableIdentity, List<long> PossibleExistingTransactionIds, bool RequiresIdentityDecision);

public sealed record BankStatementPreviewDto(BankStatementImportDto Import, List<StatementRowDto> Rows, List<string> Errors);

public enum StatementRowResolutionKind
{
    NewOccurrence,
    LinkExisting,
    AcceptRevision
}

public sealed record StatementRowResolution(
    long RowId, StatementRowResolutionKind Kind, long? ExistingTransactionId, string Reason);

public sealed class CommitStatementForm
{
    public int ExpectedVersion { get; set; }
    public List<StatementRowResolution> Resolutions { get; set; } = [];
}

public sealed class CorrectStatementRowForm
{
    public int ExpectedVersion { get; set; }
    public string? TransactionNumber { get; set; }
    public string? Reference { get; set; }
    public string? CustomerIdentifier { get; set; }
    public string Narration { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal SignedAmount { get; set; }
    public decimal RunningBalance { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public DateOnly? ValueDate { get; set; }
    public bool HasPreciseTimestamp { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public interface IBankStatementService
{
    Task<List<BankStatementImportDto>> GetImportsAsync(string userId, long accountId, CancellationToken cancellationToken = default);
    Task<BankStatementImportDto> StartAsync(string userId, long accountId, long evidenceId, bool reprocess = false, CancellationToken cancellationToken = default);
    Task<BankStatementPreviewDto> GetPreviewAsync(string userId, long importId, CancellationToken cancellationToken = default);
    Task<BankStatementPreviewDto> CorrectRowAsync(string userId, long importId, long rowId, CorrectStatementRowForm form, CancellationToken cancellationToken = default);
    Task<BankStatementImportDto> CommitAsync(string userId, long importId, CommitStatementForm form, CancellationToken cancellationToken = default);
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}
