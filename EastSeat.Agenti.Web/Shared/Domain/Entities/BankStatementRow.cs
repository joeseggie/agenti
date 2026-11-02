namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankStatementRow : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long ImportId { get; set; }
    public int Sequence { get; set; }
    public int Revision { get; set; }
    public string SourceLocation { get; set; } = string.Empty;
    public string RawData { get; set; } = string.Empty;
    public string? TransactionNumber { get; set; }
    public string? Reference { get; set; }
    public string? CustomerIdentifier { get; set; }
    public string Narration { get; set; } = string.Empty;
    public string Currency { get; set; } = "UGX";
    public decimal SignedAmount { get; set; }
    public decimal RunningBalance { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateOnly OccurrenceDate { get; set; }
    public DateOnly LedgerDate { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public DateOnly? ValueDate { get; set; }
    public bool HasPreciseTimestamp { get; set; }
    public long? SupersedesRowId { get; set; }
    public string? CorrectionReason { get; set; }
    public string? CorrectedByUserId { get; set; }
    public DateTimeOffset? CorrectedAt { get; set; }
    public BankStatementImport? Import { get; set; }
    public BankStatementRow? SupersedesRow { get; set; }
}
