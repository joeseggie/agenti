namespace EastSeat.Agenti.Shared.Domain.Entities;

public class OpeningBankVariance : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public long OpeningSnapshotId { get; set; }
    public long? PreviousClosingSnapshotId { get; set; }
    public long? PreviousClosingCountId { get; set; }
    public decimal PreviousBankBalance { get; set; }
    public decimal ObservedBankBalance { get; set; }
    public DateTimeOffset? PreviousObservedAt { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
    public CashCountSnapshot? OpeningSnapshot { get; set; }
    public CashCountSnapshot? PreviousClosingSnapshot { get; set; }
    public CashCount? PreviousClosingCount { get; set; }
}
