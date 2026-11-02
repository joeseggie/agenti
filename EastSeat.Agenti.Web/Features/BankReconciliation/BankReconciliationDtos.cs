using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public enum BankLegKind
{
    CustomerMovement,
    BankRun,
    WalletAdjustment,
    ExpectedSettlement
}

public sealed record BankLegDto(
    BankLegKind Kind, long Id, long CashSessionId, long? CustomerTransactionId,
    decimal SignedAmount, string Currency, string? Reference, string? CustomerIdentifier,
    DateTimeOffset OccurredAt, bool BankOrigin, bool PendingApproval);

public sealed record BankLedgerItemDto(
    long Id, int Version, long SourceRowId, long EvidenceFileId, DateTimeOffset OccurredAt,
    DateTimeOffset LedgerAt, decimal SignedAmount, decimal RunningBalance, string Currency,
    string? TransactionNumber, string? Reference, string? CustomerIdentifier, string Narration,
    bool HasPreciseTimestamp, BankTransactionClassification Classification, long? RelatedCustomerTransactionId);

public sealed record BankMatchDto(long Id, long BankTransactionId, BankLegKind Kind, long SourceId, int Version);

public sealed record BankExceptionDto(
    long Id, long CashSessionId, BankExceptionKind Kind, decimal Amount,
    long? CustomerMovementId, long? BankRunId, long? WalletAdjustmentId, long? BankTransactionId,
    long? CustomerTransactionId, string Explanation, long EvidenceFileId,
    bool BankExplanationVerified, bool? ReflectedInClosingBalance, bool IsResolved, int Version);

public sealed record BankCorrectionRequestDto(
    long Id, long CashCountId, long CashSessionId, long AgentId, string Reason,
    List<long> AllowedMovementIds, bool AllowMissingTransactions, bool IsOpen, int Version);

public sealed record WalletReconciliationDto(
    long WalletId, string WalletName, decimal Opening, decimal RegisterExpected,
    decimal BankAdjustments, decimal ExpectedClosing, decimal ReportedClosing, decimal Variance);

public sealed record ReconciliationAdjustmentDto(string Kind, decimal Amount, string Explanation, long? BankTransactionId, long? ExceptionId);

public sealed class SessionReconciliationDto
{
    public long CashSessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public long? ClosingCountId { get; set; }
    public long? ClosingSnapshotId { get; set; }
    public int RegisterVersion { get; set; }
    public CashCountStatus? ApprovalStatus { get; set; }
    public BankReconciliationStatus Status { get; set; }
    public string BasisHash { get; set; } = string.Empty;
    public bool CanApprove { get; set; }
    public DateTimeOffset? OpeningObservedAt { get; set; }
    public DateTimeOffset? ClosingObservedAt { get; set; }
    public decimal? StatementOpeningBalance { get; set; }
    public decimal? StatementClosingBalance { get; set; }
    public decimal? ReportedBankClosingBalance { get; set; }
    public decimal ExplainedBankDifference { get; set; }
    public decimal? UnexplainedBankDifference { get; set; }
    public List<string> BlockingReasons { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public List<WalletReconciliationDto> Wallets { get; set; } = [];
    public List<ReconciliationAdjustmentDto> Adjustments { get; set; } = [];
}

public sealed class BankReconciliationReviewDto
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int Version { get; set; }
    public BankReconciliationStatus Status { get; set; }
    public List<SessionReconciliationDto> Sessions { get; set; } = [];
    public List<BankLedgerItemDto> BankEntries { get; set; } = [];
    public List<BankLegDto> RegisterEntries { get; set; } = [];
    public List<BankMatchDto> Matches { get; set; } = [];
    public List<BankExceptionDto> Exceptions { get; set; } = [];
    public List<BankCorrectionRequestDto> CorrectionRequests { get; set; } = [];
    public List<string> PeriodIssues { get; set; } = [];
}

public sealed record BankReviewListItemDto(
    long Id, long BankAccountId, DateOnly FromDate, DateOnly ToDate, BankReconciliationStatus Status, int Version);

public sealed class BankMatchForm
{
    public int ExpectedReviewVersion { get; set; }
    public long BankTransactionId { get; set; }
    public BankLegKind Kind { get; set; }
    public long SourceId { get; set; }
    public long? RelatedMovementId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class BankClassificationForm
{
    public int ExpectedReviewVersion { get; set; }
    public long BankTransactionId { get; set; }
    public BankTransactionClassification Classification { get; set; }
    public long EvidenceFileId { get; set; }
    public long? RelatedCustomerTransactionId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class BankExceptionForm
{
    public int ExpectedReviewVersion { get; set; }
    public long CashSessionId { get; set; }
    public BankExceptionKind Kind { get; set; }
    public decimal Amount { get; set; }
    public long? CustomerMovementId { get; set; }
    public long? BankRunId { get; set; }
    public long? WalletAdjustmentId { get; set; }
    public long? BankTransactionId { get; set; }
    public long? CustomerTransactionId { get; set; }
    public long? PendingTransactionId { get; set; }
    public long EvidenceFileId { get; set; }
    public bool BankExplanationVerified { get; set; }
    public bool? ReflectedInClosingBalance { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class ClosingCorrectionForm
{
    public int ExpectedReviewVersion { get; set; }
    public long CashCountId { get; set; }
    public List<long> MovementIds { get; set; } = [];
    public bool AllowMissingTransactions { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public interface IBankReconciliationService
{
    Task<List<BankReviewListItemDto>> GetReviewsAsync(string userId, long accountId, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> CreateAsync(string userId, long accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> GetAsync(string userId, long reviewId, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> AutoMatchAsync(string userId, long reviewId, int expectedVersion, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> MatchAsync(string userId, long reviewId, BankMatchForm form, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> UnmatchAsync(string userId, long reviewId, long matchId, int expectedVersion, string reason, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> ClassifyAsync(string userId, long reviewId, BankClassificationForm form, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> RecordExceptionAsync(string userId, long reviewId, BankExceptionForm form, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> ResolveExceptionAsync(string userId, long reviewId, long exceptionId, long evidenceBankTransactionId, int expectedVersion, string reason, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> ReturnForCorrectionAsync(string userId, long reviewId, ClosingCorrectionForm form, CancellationToken cancellationToken = default);
    Task<BankReconciliationReviewDto> ApproveClosingAsync(string userId, long reviewId, long cashSessionId, Guid commandId, int expectedVersion, string basisHash, CancellationToken cancellationToken = default);
    Task<List<BankCorrectionRequestDto>> GetMyCorrectionsAsync(string userId, CancellationToken cancellationToken = default);
}
