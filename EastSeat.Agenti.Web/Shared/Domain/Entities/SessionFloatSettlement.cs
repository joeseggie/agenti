using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class SessionFloatSettlement : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long SnapshotId { get; set; }
    public long CashCountId { get; set; }
    public long? VaultTransactionId { get; set; }
    public FloatSettlementStage Stage { get; set; }
    public decimal VaultAmount { get; set; }
    public decimal ReportedAmount { get; set; }
    public decimal BankVariance { get; set; }
    public string PostedByUserId { get; set; } = string.Empty;
    public DateTimeOffset PostedAt { get; set; }
    public CashCountSnapshot? Snapshot { get; set; }
    public CashCount? CashCount { get; set; }
    public VaultTransaction? VaultTransaction { get; set; }
}
