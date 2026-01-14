using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents the central cash vault for a branch.
/// </summary>
public class Vault
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public decimal CurrentBalance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation
    public Branch? Branch { get; set; }
    public ICollection<VaultTransaction> Transactions { get; set; } = new List<VaultTransaction>();
}
