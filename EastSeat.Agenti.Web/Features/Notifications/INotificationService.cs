namespace EastSeat.Agenti.Web.Features.Notifications;

public interface INotificationService
{
    Task<List<NotificationListItemDto>> GetNotificationsAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
    Task<NotificationSaveResult> SendNotificationAsync(string? senderUserId, CreateNotificationDto dto);
    Task<NotificationSaveResult> MarkAsReadAsync(Guid notificationId, string userId);
    Task<NotificationSaveResult> MarkAllAsReadAsync(string userId);
}
