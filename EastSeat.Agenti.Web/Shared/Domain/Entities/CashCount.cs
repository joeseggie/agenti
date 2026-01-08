namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a cash count (opening or closing) for a session.
/// </summary>
public class CashCount
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public bool IsOpening { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    // Navigation properties
    public CashSession? CashSession { get; set; }
    public ICollection<CashCountDetail> Details { get; set; } = new List<CashCountDetail>();
}
