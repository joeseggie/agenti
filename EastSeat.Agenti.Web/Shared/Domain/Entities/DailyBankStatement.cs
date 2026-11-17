namespace EastSeat.Agenti.Shared.Domain.Entities;

public sealed class DailyBankStatement : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public DateOnly StatementDate { get; set; }
    public int Revision { get; set; }
    public long? SupersedesId { get; set; }
    public string? ReplacementReason { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string AccountIdentifier { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string SchemaHash { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public byte[] Content { get; set; } = [];
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
    public List<DailyBankStatementRow> Rows { get; set; } = [];
}

public sealed class DailyBankStatementRow : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long StatementId { get; set; }
    public int Sequence { get; set; }
    public DateOnly TransactionDate { get; set; }
    public TimeOnly? TransactionTime { get; set; }
    public string? Reference { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}
