using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents an in-app notification sent to a user from another user or the system.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    /// <summary>
    /// The user who receives this notification.
    /// </summary>
    public string RecipientUserId { get; set; } = string.Empty;

    /// <summary>
    /// The user who sent this notification. Null indicates a system notification.
    /// </summary>
    public string? SenderUserId { get; set; }

    public string? Title { get; set; }
    public string Message { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public NotificationType? Type { get; set; }
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// Optional vault transaction ID for notifications that require approval action.
    /// </summary>
    public Guid? TransactionId { get; set; }

    // Navigation properties
    public ApplicationUser? Recipient { get; set; }
    public ApplicationUser? Sender { get; set; }
}
