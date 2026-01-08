using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a transaction (movement of funds between wallets).
/// </summary>
public class Transaction
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public long FromWalletId { get; set; }
    public long ToWalletId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public long? RecordedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ReversalTransactionId { get; set; }

    // Navigation properties
    public CashSession? CashSession { get; set; }
    public Wallet? FromWallet { get; set; }
    public Wallet? ToWallet { get; set; }
    public Transaction? ReversalTransaction { get; set; }
}
