namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a line item in a cash count (wallet balance at a specific time).
/// </summary>
public class CashCountDetail
{
    public long Id { get; set; }
    public long CashCountId { get; set; }
    public long WalletId { get; set; }
    public decimal Amount { get; set; }
    public string? Denominations { get; set; } // JSON format for denomination breakdown

    // Navigation properties
    public CashCount? CashCount { get; set; }
    public Wallet? Wallet { get; set; }
}
