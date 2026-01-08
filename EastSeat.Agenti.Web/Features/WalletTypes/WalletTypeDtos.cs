using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.WalletTypes;

/// <summary>
/// DTO for displaying wallet type in a list.
/// </summary>
public class WalletTypeListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WalletTypeEnum Type { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public bool SupportsDenominations { get; set; }
    public int WalletCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets a friendly display name for the wallet type enum.
    /// </summary>
    public string TypeDisplayName => Type switch
    {
        WalletTypeEnum.Cash => "Cash",
        WalletTypeEnum.MobileMoney => "Mobile Money",
        WalletTypeEnum.Bank => "Bank",
        WalletTypeEnum.Custom => "Custom",
        _ => Type.ToString()
    };

    /// <summary>
    /// Gets an icon for the wallet type.
    /// </summary>
    public string TypeIcon => Type switch
    {
        WalletTypeEnum.Cash => "💵",
        WalletTypeEnum.MobileMoney => "📱",
        WalletTypeEnum.Bank => "🏦",
        WalletTypeEnum.Custom => "💰",
        _ => "💰"
    };

    /// <summary>
    /// Whether this wallet type can be deleted (no wallets created with it).
    /// </summary>
    public bool CanDelete => WalletCount == 0 && !IsSystem;
}

/// <summary>
/// Form model for creating/editing a wallet type.
/// </summary>
public class WalletTypeFormModel
{
    public long? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WalletTypeEnum Type { get; set; } = WalletTypeEnum.Custom;
    public bool SupportsDenominations { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Result of save operations.
/// </summary>
public class WalletTypeSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? Id { get; set; }

    public static WalletTypeSaveResult Ok(long id) => new() { Success = true, Id = id };
    public static WalletTypeSaveResult Error(string message) => new() { Success = false, ErrorMessage = message };
}
