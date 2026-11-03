namespace EastSeat.Agenti.Shared.Domain.Entities;

public class CashCountSnapshot : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long CashCountId { get; set; }
    public int RegisterVersion { get; set; }
    public decimal TotalAmount { get; set; }
    public string WalletsJson { get; set; } = string.Empty;
    public string CapturedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public CashCount? CashCount { get; set; }
}
