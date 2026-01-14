using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Agents;

/// <summary>
/// DTO for branch information.
/// </summary>
public class BranchDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// DTO for ApplicationUsers available to become agents (no AgentId assigned yet).
/// </summary>
public class AvailableUserDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string DisplayName => !string.IsNullOrEmpty(FullName) ? $"{FullName} ({Email})" : Email;
}

/// <summary>
/// DTO for displaying agent in a list.
/// </summary>
public class AgentListItemDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public long? BranchId { get; set; }
    public bool IsActive { get; set; }
    public int WalletCount { get; set; }
    public decimal TotalBalance { get; set; }
}

/// <summary>
/// DTO for agent details including wallets.
/// </summary>
public class AgentDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public long? BranchId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<AgentWalletDto> Wallets { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
    public decimal TotalBalance => Wallets.Sum(w => w.Balance);
}

/// <summary>
/// DTO for wallet information within agent context.
/// </summary>
public class AgentWalletDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public WalletTypeEnum WalletType { get; set; }
    public string Currency { get; set; } = "UGX";
    public decimal Balance { get; set; }
    public bool IsActive { get; set; }
    public bool SupportsDenominations { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public string WalletTypeIcon => WalletType switch
    {
        WalletTypeEnum.Cash => "💵",
        WalletTypeEnum.MobileMoney => "📱",
        WalletTypeEnum.Bank => "🏦",
        _ => "💰"
    };
}

/// <summary>
/// Form model for creating/editing an agent.
/// Profile info (name, email, phone) comes from the linked ApplicationUser.
/// Code is auto-generated from the user's name for new agents.
/// </summary>
public class AgentFormModel
{
    public long? Id { get; set; }

    /// <summary>
    /// The ApplicationUser Id to link this agent to (required for new agents).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Agent code - auto-generated for new agents, editable for existing agents.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public long? BranchId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Form model for creating/editing a wallet for an agent.
/// </summary>
public class WalletFormModel
{
    public long? Id { get; set; }
    public long AgentId { get; set; }
    public long WalletTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "UGX";
    public decimal InitialBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// DTO for wallet type selection.
/// </summary>
public class WalletTypeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WalletTypeEnum Type { get; set; }
    public bool SupportsDenominations { get; set; }
}

/// <summary>
/// Result of save operations.
/// </summary>
public class SaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? Id { get; set; }

    public static SaveResult Ok(long id) => new() { Success = true, Id = id };
    public static SaveResult Error(string message) => new() { Success = false, ErrorMessage = message };
}
