using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankReconciliationResult : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long ReviewId { get; set; }
    public long CashSessionId { get; set; }
    public long SnapshotId { get; set; }
    public int RegisterVersion { get; set; }
    public BankReconciliationStatus Status { get; set; }
    public string BasisHash { get; set; } = string.Empty;
    public Guid CommandId { get; set; }
    public string ReportJson { get; set; } = string.Empty;
    public long? SupersedesResultId { get; set; }
    public string ReviewedByUserId { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; set; }
    public BankReconciliationReview? Review { get; set; }
    public CashSession? CashSession { get; set; }
    public CashCountSnapshot? Snapshot { get; set; }
    public BankReconciliationResult? SupersedesResult { get; set; }
}
