using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a discrepancy in cash count requiring supervisor approval.
/// </summary>
public class Discrepancy
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public long CashCountId { get; set; }
    public DiscrepancyStatus Status { get; set; }
    public decimal ExpectedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal Variance { get; set; }
    public string? Reason { get; set; }
    public string? Explanation { get; set; }
    public long? ExplainedByUserId { get; set; }
    public DateTime? ExplainedAt { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalNotes { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public CashSession? CashSession { get; set; }
    public CashCount? CashCount { get; set; }
}
