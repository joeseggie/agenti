using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Notifications;

public class NotificationService(ApplicationDbContext dbContext) : INotificationService
{
    public async Task CreateAsync(string userId, string title, string message, NotificationType type, string? linkUrl = null)
    {
        dbContext.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            LinkUrl = linkUrl,
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
        foreach (var userId in adminUserIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                LinkUrl = linkUrl,
                IsRead = false,
                CreatedAt = now
            });
        }

        if (adminUserIds.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync();
    }

    public async Task<List<NotificationDto>> GetNotificationsAsync(string userId, int take = 20)
    {
        return await dbContext.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type,
                LinkUrl = n.LinkUrl,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();
    }

    public async Task MarkAsReadAsync(long notificationId, string userId)
    {
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification != null && !notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(string userId)
    {
        var unread = await dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }

        if (unread.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }
    }
}
