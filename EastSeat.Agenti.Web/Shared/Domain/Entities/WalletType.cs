using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a wallet type in the system (Cash, Bank, Mobile Money, etc.)
/// </summary>
public class WalletType
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WalletTypeEnum Type { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public bool SupportsDenominations { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();
}
