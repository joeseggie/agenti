using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Notifications;

public interface INotificationService
{
    // General notification operations (from main)
    Task<List<NotificationListItemDto>> GetNotificationsAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
    Task<NotificationSaveResult> SendNotificationAsync(string? senderUserId, CreateNotificationDto dto);
    Task<NotificationSaveResult> MarkAsReadAsync(Guid notificationId, string userId);
    Task<NotificationSaveResult> MarkAllAsReadAsync(string userId);

    // Cash count workflow notifications
    Task CreateSystemNotificationAsync(string recipientUserId, string title, string message, NotificationType type, string? linkUrl = null);
    Task NotifyBranchAdminsAsync(long branchId, string title, string message, NotificationType type, string? linkUrl = null);
}
