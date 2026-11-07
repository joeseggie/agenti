namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed record ReviewVersionRequest(int ExpectedVersion);
public sealed record ReviewReasonRequest(int ExpectedVersion, string Reason);
public sealed record CreateBankReviewRequest(long BankAccountId, DateOnly FromDate, DateOnly ToDate);
public sealed record ApproveBankClosingRequest(long CashSessionId, Guid CommandId, int ExpectedVersion, string BasisHash);
public sealed record ResolveBankExceptionRequest(long EvidenceBankTransactionId, int ExpectedVersion, string Reason);
public sealed record ResubmitBankCorrectionsRequest(int ExpectedVersion);
public sealed record BankWorkflowRequest(bool Enabled, int ExpectedVersion);
public sealed record BankingCapabilitiesDto(int ContractVersion, long MaximumEvidenceBytes, string BusinessTimeZone);
