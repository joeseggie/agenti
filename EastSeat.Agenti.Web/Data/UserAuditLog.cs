using System;

namespace EastSeat.Agenti.Web.Data;

public class UserAuditLog
{
    public int Id { get; set; }

    // Target user being acted upon
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    // Action metadata
    public UserAuditAction Action { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    // Actor who performed the action
    public string PerformedByUserId { get; set; } = string.Empty;
    public ApplicationUser? PerformedByUser { get; set; }

    public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;
}
