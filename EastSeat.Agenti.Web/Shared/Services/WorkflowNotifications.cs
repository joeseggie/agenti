using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Shared.Services;

public static class WorkflowNotifications
{
    public static void AddForUser(
        ApplicationDbContext db, string recipient, string title, string message,
        NotificationType type, string link, DateTimeOffset now)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = recipient, Title = title, Message = message,
            Type = type, LinkUrl = link, Priority = NotificationPriority.Normal, CreatedAt = now
        });
    }

    public static async Task AddForBranchAsync(
        ApplicationDbContext db, long branchId, string title, string message, NotificationType type,
        string link, DateTimeOffset now, bool includeSupervisors = false, CancellationToken cancellationToken = default)
    {
        var recipientIds = await db.Users.Where(u => u.BranchId == branchId && u.IsActive &&
                (u.Role == UserRole.Admin || (includeSupervisors && u.Role == UserRole.Supervisor)))
            .Select(u => u.Id).ToListAsync(cancellationToken);
        foreach (var recipient in recipientIds)
            AddForUser(db, recipient, title, message, type, link, now);
    }
}
