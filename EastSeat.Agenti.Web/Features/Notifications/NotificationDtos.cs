using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Notifications;

public class NotificationListItemDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string Message { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; }
    public NotificationType? Type { get; set; }
    public string? LinkUrl { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Optional vault transaction public ID for notifications that require an approve/reject action.
    /// </summary>
    public Guid? TransactionId { get; set; }
}

public class CreateNotificationDto
{
    public string RecipientUserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    /// <summary>
    /// Optional vault transaction public ID to associate with this notification.
    /// </summary>
    public Guid? TransactionId { get; set; }
}

public class UnreadCountDto
{
    public int Count { get; set; }
}

public class NotificationSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? Id { get; set; }

    public static NotificationSaveResult Ok(Guid id) => new() { Success = true, Id = id };
    public static NotificationSaveResult Error(string message) => new() { Success = false, ErrorMessage = message };
}
