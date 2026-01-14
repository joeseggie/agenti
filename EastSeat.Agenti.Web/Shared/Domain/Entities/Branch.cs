namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a tenant branch. Each branch has exactly one vault.
/// </summary>
public class Branch
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public Vault? Vault { get; set; }
}
