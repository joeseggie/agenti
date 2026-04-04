using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a daily cash session for a branch. One session per branch per day,
/// shared by all agents performing cash counts that day.
/// </summary>
public class CashSession
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly SessionDate { get; set; }
    public CashSessionStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public DateTimeOffset? UnblockedAt { get; set; }

    // Navigation properties
    public Branch? Branch { get; set; }
    public ICollection<CashCount> CashCounts { get; set; } = new List<CashCount>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Discrepancy> Discrepancies { get; set; } = new List<Discrepancy>();
}
