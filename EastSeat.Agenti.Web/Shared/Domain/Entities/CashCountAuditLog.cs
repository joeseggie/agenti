using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Immutable audit log entry for a cash count.
/// Records every lifecycle change (created, saved, submitted, approved, rejected, unapproved)
/// so an agent and admin can review the full history of changes for a session.
/// </summary>
public class CashCountAuditLog
{
    public long Id { get; set; }

    /// <summary>Cash count being audited.</summary>
    public long CashCountId { get; set; }
    public CashCount? CashCount { get; set; }

    /// <summary>The cash session the cash count belongs to (denormalized for fast querying).</summary>
    public long CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }

    /// <summary>The agent owning the cash count (denormalized for fast querying).</summary>
    public long AgentId { get; set; }
    public Agent? Agent { get; set; }

    /// <summary>True if the audited count is the opening count, false for closing.</summary>
    public bool IsOpening { get; set; }

    /// <summary>The action that occurred.</summary>
    public CashCountAuditAction Action { get; set; }

    /// <summary>Status before the action (null for the initial create).</summary>
    public CashCountStatus? PreviousStatus { get; set; }

    /// <summary>Status after the action.</summary>
    public CashCountStatus NewStatus { get; set; }

    /// <summary>Total amount snapshot at the time of the action.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Optional explanation / notes / rejection reason captured with the action.</summary>
    public string? Notes { get; set; }

    /// <summary>User who performed the action (null for system / auto actions).</summary>
    public string? PerformedByUserId { get; set; }
    public ApplicationUser? PerformedByUser { get; set; }

    public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;
}
