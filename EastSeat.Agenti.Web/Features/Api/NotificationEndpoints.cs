using System.Security.Claims;
using EastSeat.Agenti.Web.Features.Notifications;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for in-app notifications.
/// </summary>
public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (ClaimsPrincipal user, INotificationService notificationService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Results.Unauthorized();

            var notifications = await notificationService.GetNotificationsAsync(userId);
            return Results.Ok(ApiResponse<List<NotificationListItemDto>>.Ok(notifications));
        })
        .RequireAuthorization()
        .WithName("GetNotifications")
        .WithSummary("Get notifications for the authenticated user");

        group.MapGet("/unread-count", async (ClaimsPrincipal user, INotificationService notificationService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Results.Unauthorized();

            var count = await notificationService.GetUnreadCountAsync(userId);
            return Results.Ok(ApiResponse<UnreadCountDto>.Ok(new UnreadCountDto { Count = count }));
        })
        .RequireAuthorization()
        .WithName("GetUnreadNotificationCount")
        .WithSummary("Get unread notification count for the authenticated user");

        group.MapPost("/{notificationId:guid}/read", async (
            Guid notificationId,
            ClaimsPrincipal user,
            INotificationService notificationService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Results.Unauthorized();

            var result = await notificationService.MarkAsReadAsync(notificationId, userId);
            return result.Success
                ? Results.Ok(ApiResponse<NotificationSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<NotificationSaveResult>.Fail(result.ErrorMessage ?? "Failed to mark notification as read."));
        })
        .RequireAuthorization()
        .WithName("MarkNotificationAsRead")
        .WithSummary("Mark a single notification as read");

        group.MapPost("/read-all", async (ClaimsPrincipal user, INotificationService notificationService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Results.Unauthorized();

            var result = await notificationService.MarkAllAsReadAsync(userId);
            return Results.Ok(ApiResponse<NotificationSaveResult>.Ok(result));
        })
        .RequireAuthorization()
        .WithName("MarkAllNotificationsAsRead")
        .WithSummary("Mark all notifications as read for the authenticated user");

        group.MapPost("/", async (
            CreateNotificationDto dto,
            ClaimsPrincipal user,
            INotificationService notificationService) =>
        {
            var senderUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (senderUserId is null)
                return Results.Unauthorized();

            var result = await notificationService.SendNotificationAsync(senderUserId, dto);
            return result.Success
                ? Results.Created($"/api/notifications", ApiResponse<NotificationSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<NotificationSaveResult>.Fail(result.ErrorMessage ?? "Failed to send notification."));
        })
        .RequireAuthorization()
        .WithName("SendNotification")
        .WithSummary("Send a notification to another user");

        return group;
    }
}
