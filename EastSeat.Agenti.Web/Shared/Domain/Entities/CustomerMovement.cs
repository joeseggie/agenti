using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class CustomerMovement : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long CustomerTransactionId { get; set; }
    public long CashSessionId { get; set; }
    public CustomerMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal CustomerSettledAmount { get; set; }
    public CustomerTransactionKind? TransactionKind { get; set; }
    public string? CustomerIdentifier { get; set; }
    public decimal CashEffect { get; set; }
    public decimal BankEffect { get; set; }
    public string? BankReference { get; set; }
    public bool ReferenceUnavailable { get; set; }
    public string? Notes { get; set; }
    public string? SupportTicketNumber { get; set; }
    public long? EvidenceFileId { get; set; }
    public long? RelatedMovementId { get; set; }
    public long? SupersedesMovementId { get; set; }
    public long? CorrectionRequestId { get; set; }
    public bool ReversalVerifiedByAgent { get; set; }
    public Guid CommandId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string RecordedByUserId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateOnly OccurrenceDate { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public CustomerTransaction? CustomerTransaction { get; set; }
    public CashSession? CashSession { get; set; }
    public BankEvidenceFile? EvidenceFile { get; set; }
    public CustomerMovement? RelatedMovement { get; set; }
    public CustomerMovement? SupersedesMovement { get; set; }
    public CashCountCorrectionRequest? CorrectionRequest { get; set; }
}
