namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents an audit log entry for all system actions.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string Action { get; set; } = string.Empty; // Create, Update, Delete, etc.
    public string? Changes { get; set; } // JSON format for before/after values
    public DateTimeOffset CreatedAt { get; set; }
    public string? IpAddress { get; set; }
}
