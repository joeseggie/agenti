using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankReconciliationException
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public long CashSessionId { get; set; }
    public long? CustomerTransactionId { get; set; }
    public long? PendingTransactionId { get; set; }
    public long? BankTransactionId { get; set; }
    public long? CustomerMovementId { get; set; }
    public long? BankRunId { get; set; }
    public long? WalletAdjustmentId { get; set; }
    public decimal Amount { get; set; }
    public bool BankExplanationVerified { get; set; }
    public bool? ReflectedInClosingBalance { get; set; }
    public BankExceptionKind Kind { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public long EvidenceFileId { get; set; }
    public bool IsResolved { get; set; }
    public int Version { get; set; }
    public string RecordedByUserId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
    public CashSession? CashSession { get; set; }
    public CustomerTransaction? CustomerTransaction { get; set; }
    public PendingTransaction? PendingTransaction { get; set; }
    public BankTransaction? BankTransaction { get; set; }
    public CustomerMovement? CustomerMovement { get; set; }
    public BankRun? BankRun { get; set; }
    public WalletAdjustment? WalletAdjustment { get; set; }
    public BankEvidenceFile? EvidenceFile { get; set; }
}
