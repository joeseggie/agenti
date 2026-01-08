using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents an agent in the system who handles transactions and cash counts.
/// All agents must be linked to an ApplicationUser for authentication.
/// Agent profile info (name, email, phone) comes from the linked ApplicationUser.
/// </summary>
public class Agent
{
    public long Id { get; set; }

    /// <summary>
    /// Required link to the ApplicationUser for authentication.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
    public long? BranchId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public ApplicationUser? User { get; set; }
    public ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();
    public ICollection<CashSession> CashSessions { get; set; } = new List<CashSession>();
}
