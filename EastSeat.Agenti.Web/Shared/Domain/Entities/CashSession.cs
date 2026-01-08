using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a daily cash session for an agent.
/// </summary>
public class CashSession
{
    public long Id { get; set; }
    public long AgentId { get; set; }
    public long? BranchId { get; set; }
    public DateOnly SessionDate { get; set; }
    public CashSessionStatus Status { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? LockedAt { get; set; }
    public DateTime? UnlockedAt { get; set; }

    // Navigation properties
    public ICollection<CashCount> CashCounts { get; set; } = new List<CashCount>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Discrepancy> Discrepancies { get; set; } = new List<Discrepancy>();
}
