using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a wallet instance (e.g., "Main Cash Drawer", "Stanbic Account #123")
/// </summary>
public class Wallet
{
    public long Id { get; set; }
    public long WalletTypeId { get; set; }
    public long? AgentId { get; set; }
    public long? BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "UGX";
    public decimal Balance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public Agent? Agent { get; set; }
    public WalletType? WalletType { get; set; }
    public ICollection<Transaction> TransactionsFrom { get; set; } = new List<Transaction>();
    public ICollection<Transaction> TransactionsTo { get; set; } = new List<Transaction>();
}
