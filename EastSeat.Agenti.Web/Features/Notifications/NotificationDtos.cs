using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Notifications;

public class NotificationListItemDto
{
    public long Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateNotificationDto
{
    public string RecipientUserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
}

public class UnreadCountDto
{
    public int Count { get; set; }
}

public class NotificationSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? Id { get; set; }

    public static NotificationSaveResult Ok(long id) => new() { Success = true, Id = id };
    public static NotificationSaveResult Error(string message) => new() { Success = false, ErrorMessage = message };
}
