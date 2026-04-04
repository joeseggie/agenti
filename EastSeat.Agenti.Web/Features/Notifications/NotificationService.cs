using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Notifications;

public class NotificationService(ApplicationDbContext dbContext) : INotificationService
{
    public async Task<List<NotificationListItemDto>> GetNotificationsAsync(string userId)
    {
        return await dbContext.Notifications
            .Where(n => n.RecipientUserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationListItemDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Priority = n.Priority,
                Type = n.Type,
                LinkUrl = n.LinkUrl,
                SenderName = n.Sender != null
                    ? (n.Sender.FirstName + " " + n.Sender.LastName).Trim()
                    : "System",
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                TransactionId = n.TransactionId
            })
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await dbContext.Notifications
            .CountAsync(n => n.RecipientUserId == userId && !n.IsRead);
    }

    public async Task<NotificationSaveResult> SendNotificationAsync(string? senderUserId, CreateNotificationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Message))
            return NotificationSaveResult.Error("Message is required.");

        if (string.IsNullOrWhiteSpace(dto.RecipientUserId))
            return NotificationSaveResult.Error("Recipient is required.");

        var recipientExists = await dbContext.Users.AnyAsync(u => u.Id == dto.RecipientUserId);
        if (!recipientExists)
            return NotificationSaveResult.Error("Recipient not found.");

        if (senderUserId != null)
        {
            var senderExists = await dbContext.Users.AnyAsync(u => u.Id == senderUserId);
            if (!senderExists)
                return NotificationSaveResult.Error("Sender not found.");
        }

        var notification = new Notification
        {
            RecipientUserId = dto.RecipientUserId,
            SenderUserId = senderUserId,
            Message = dto.Message,
            Priority = dto.Priority,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            TransactionId = dto.TransactionId
        };

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        return NotificationSaveResult.Ok(notification.Id);
    }

    public async Task<NotificationSaveResult> MarkAsReadAsync(Guid notificationId, string userId)
    {
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId);

        if (notification == null)
            return NotificationSaveResult.Error("Notification not found.");

        if (notification.IsRead)
            return NotificationSaveResult.Ok(notification.Id);

        notification.IsRead = true;
        notification.ReadAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        return NotificationSaveResult.Ok(notification.Id);
    }

    public async Task<NotificationSaveResult> MarkAllAsReadAsync(string userId)
    {
        var now = DateTimeOffset.UtcNow;

        var unreadNotifications = await dbContext.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await dbContext.SaveChangesAsync();

        return NotificationSaveResult.Ok(Guid.Empty);
    }

    public async Task CreateSystemNotificationAsync(string recipientUserId, string title, string message, NotificationType type, string? linkUrl = null)
    {
        dbContext.Notifications.Add(new Notification
        {
            RecipientUserId = recipientUserId,
            SenderUserId = null,
            Title = title,
            Message = message,
            Type = type,
            LinkUrl = linkUrl,
            Priority = type == NotificationType.SessionBlocked ? NotificationPriority.High : NotificationPriority.Normal,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    public async Task NotifyBranchAdminsAsync(long branchId, string title, string message, NotificationType type, string? linkUrl = null)
    {
        var adminUserIds = await dbContext.Users
            .Where(u => u.BranchId == branchId &&
                        (u.Role == UserRole.Admin || u.Role == UserRole.Supervisor) &&
                        u.IsActive && !u.IsDeleted)
            .Select(u => u.Id)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var priority = type == NotificationType.SessionBlocked ? NotificationPriority.High : NotificationPriority.Normal;

        foreach (var userId in adminUserIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                RecipientUserId = userId,
                SenderUserId = null,
                Title = title,
                Message = message,
                Type = type,
                LinkUrl = linkUrl,
                Priority = priority,
                IsRead = false,
                CreatedAt = now
            });
        }

        if (adminUserIds.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }
    }
}
