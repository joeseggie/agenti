namespace EastSeat.Agenti.Shared.Domain.Entities;

public sealed class ClosingReconciliationRun : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long ClosingCountId { get; set; }
    public long OpeningCountId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string InputHash { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string PerformedByUserId { get; set; } = string.Empty;
    public DateTimeOffset PerformedAt { get; set; }
    public List<ClosingWalletResult> Wallets { get; set; } = [];
}

public sealed class ClosingWalletResult : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public long WalletId { get; set; }
    public long BankAccountId { get; set; }
    public long StatementId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountIdentifier { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal ExpectedClosing { get; set; }
    public decimal SubmittedClosing { get; set; }
    public decimal StatementOpening { get; set; }
    public decimal StatementClosing { get; set; }
    public decimal Difference { get; set; }
    public bool Succeeded { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class ClosingApprovalDecision : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long ClosingCountId { get; set; }
    public long RunId { get; set; }
    public bool IsException { get; set; }
    public string? Reason { get; set; }
    public string ApprovedByUserId { get; set; } = string.Empty;
    public DateTimeOffset ApprovedAt { get; set; }
}
