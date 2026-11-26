using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.ClosingReconciliation;

public sealed record ClosingWalletDto(
    long WalletId, string WalletName, WalletTypeEnum Type, string Currency, decimal Amount,
    long? BankAccountId, long? StatementId, bool StatementRequired = true);

public sealed record ClosingWalletResultDto(
    string WalletName, string BankName, string AccountIdentifier, string Currency, long? StatementId, int? StatementRevision,
    decimal OpeningBalance, decimal TotalDebits, decimal TotalCredits, decimal ExpectedClosing,
    decimal SubmittedClosing, decimal StatementOpening, decimal StatementClosing, decimal Difference,
    bool Succeeded, string Explanation, StatementBalanceSource BalanceSource = StatementBalanceSource.BankReported,
    long? OpeningCountId = null, bool StatementWaived = false);

public sealed record ClosingReportDto(
    long Id, bool Succeeded, string PerformedByUserId, DateTimeOffset PerformedAt,
    List<ClosingWalletResultDto> Wallets);

public sealed record ClosingApprovalDto(bool IsException, string? Reason, string ApprovedByUserId, DateTimeOffset ApprovedAt);

public sealed record ClosingDiscrepancyDto(decimal RecordedVariance, string? Explanation);

public sealed record ClosingReviewDto(
    long ClosingCountId, long CashSessionId, string AgentName, DateOnly BusinessDate,
    CashCountStatus Status, List<ClosingWalletDto> Wallets, ClosingReportDto? Report, ClosingApprovalDto? Approval,
    SessionWorkflowVersion WorkflowVersion = SessionWorkflowVersion.BankReconciliation,
    ClosingDiscrepancyDto? PendingDiscrepancy = null);

public interface IClosingReconciliationService
{
    Task<ClosingReviewDto> GetAsync(string userId, long closingCountId, CancellationToken cancellationToken = default);
    Task<ClosingReviewDto> ReconcileAsync(string userId, long closingCountId, CancellationToken cancellationToken = default);
    Task<ClosingReviewDto> ApproveAsync(string userId, long closingCountId, bool exception,
        string? reason = null, bool acknowledgeFailures = false, CancellationToken cancellationToken = default);
}
