using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a cash count (opening or closing) for an agent within a session.
/// Each agent can have one opening and one closing count per session.
/// </summary>
public class CashCount
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public long AgentId { get; set; }
    public bool IsOpening { get; set; }
    public CashCountStatus Status { get; set; }
    public DateOnly CountDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Explanation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectedByUserId { get; set; }
    public string? RejectionReason { get; set; }

    // Navigation properties
    public CashSession? CashSession { get; set; }
    public Agent? Agent { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
    public ApplicationUser? RejectedByUser { get; set; }
    public ICollection<CashCountDetail> Details { get; set; } = new List<CashCountDetail>();
}
