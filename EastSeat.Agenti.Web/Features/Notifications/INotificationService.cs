using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Notifications;

public interface INotificationService
{
    Task CreateAsync(string userId, string title, string message, NotificationType type, string? linkUrl = null);
    Task NotifyBranchAdminsAsync(long branchId, string title, string message, NotificationType type, string? linkUrl = null);
    Task<int> GetUnreadCountAsync(string userId);
    Task<List<NotificationDto>> GetNotificationsAsync(string userId, int take = 20);
    Task MarkAsReadAsync(long notificationId, string userId);
    Task MarkAllAsReadAsync(string userId);
}
