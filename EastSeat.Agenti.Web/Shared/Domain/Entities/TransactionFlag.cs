using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Records a flag raised by an agent indicating that a transaction was made in error
/// and requires investigation by a supervisor or admin.
/// Transactions are immutable; flags are a separate audit overlay.
/// </summary>
public class TransactionFlag
{
    public long Id { get; set; }

    /// <summary>The flagged transaction.</summary>
    public long TransactionId { get; set; }

    /// <summary>User ID (ApplicationUser) who raised the flag.</summary>
    public string FlaggedByUserId { get; set; } = string.Empty;

    public DateTimeOffset FlaggedAt { get; set; }

    /// <summary>Agent's description of the error (minimum 10 characters).</summary>
    public string Reason { get; set; } = string.Empty;

    public TransactionFlagStatus Status { get; set; } = TransactionFlagStatus.PendingReview;

    /// <summary>Notes added by the supervisor/admin during or after investigation.</summary>
    public string? InvestigationNotes { get; set; }

    /// <summary>User ID (ApplicationUser) who resolved or dismissed the flag.</summary>
    public string? ResolvedByUserId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    // Navigation properties
    public Transaction? Transaction { get; set; }
    public ApplicationUser? FlaggedByUser { get; set; }
    public ApplicationUser? ResolvedByUser { get; set; }
}
