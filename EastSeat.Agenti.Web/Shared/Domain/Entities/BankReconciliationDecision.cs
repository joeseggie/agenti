using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankReconciliationDecision : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public long? ReviewId { get; set; }
    public long? CashSessionId { get; set; }
    public long? BankTransactionId { get; set; }
    public ReconciliationDecisionKind Kind { get; set; }
    public string DetailsJson { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PerformedByUserId { get; set; } = string.Empty;
    public DateTimeOffset PerformedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
    public BankReconciliationReview? Review { get; set; }
    public CashSession? CashSession { get; set; }
    public BankTransaction? BankTransaction { get; set; }
}
