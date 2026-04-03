using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "Notifications")]
public class NotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _sut;
    private readonly ApplicationUser _recipientUser;
    private readonly ApplicationUser _senderUser;

    public NotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        _recipientUser = UserBuilder.Default()
            .WithId("recipient-1")
            .WithFirstName("Jane")
            .WithLastName("Doe")
            .WithEmail("jane@test.com")
            .Build();

        _senderUser = UserBuilder.Default()
            .WithId("sender-1")
            .WithFirstName("John")
            .WithLastName("Smith")
            .WithEmail("john@test.com")
            .Build();

        _dbContext.Users.AddRange(_recipientUser, _senderUser);
        _dbContext.SaveChanges();

        _sut = new NotificationService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetNotificationsAsync Tests

    [Fact]
    public async Task GetNotificationsAsync_ReturnsNotificationsForUser_OrderedByDateDesc()
    {
        // Arrange
        var older = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .WithMessage("Older notification")
            .WithCreatedAt(DateTimeOffset.UtcNow.AddHours(-2))
            .Build();

        var newer = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .WithMessage("Newer notification")
            .WithCreatedAt(DateTimeOffset.UtcNow.AddHours(-1))
            .Build();

        _dbContext.Notifications.AddRange(older, newer);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetNotificationsAsync(_recipientUser.Id);

        // Assert
        result.Should().HaveCount(2);
        result[0].Message.Should().Be("Newer notification");
        result[1].Message.Should().Be("Older notification");
    }

    [Fact]
    public async Task GetNotificationsAsync_DoesNotReturnOtherUsersNotifications()
    {
        // Arrange
        var notification = NotificationBuilder.Default()
            .WithRecipientUserId(_senderUser.Id)
            .WithSenderUserId(_recipientUser.Id)
            .WithMessage("Not for recipient")
            .Build();

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetNotificationsAsync(_recipientUser.Id);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNotificationsAsync_SystemNotification_ShowsSystemAsSenderName()
    {
        // Arrange
        var notification = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .AsSystemNotification()
            .WithMessage("System alert")
            .Build();

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetNotificationsAsync(_recipientUser.Id);

        // Assert
        result.Should().HaveCount(1);
        result[0].SenderName.Should().Be("System");
    }

    [Fact]
    public async Task GetNotificationsAsync_UserNotification_ShowsSenderFullName()
    {
        // Arrange
        var notification = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .WithMessage("User message")
            .Build();

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetNotificationsAsync(_recipientUser.Id);

        // Assert
        result.Should().HaveCount(1);
        result[0].SenderName.Should().Be("John Smith");
    }

    #endregion

    #region GetUnreadCountAsync Tests

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var unread1 = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .Build();

        var unread2 = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .AsSystemNotification()
            .Build();

        var read = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .AsRead()
            .Build();

        _dbContext.Notifications.AddRange(unread1, unread2, read);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.GetUnreadCountAsync(_recipientUser.Id);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsZero_WhenAllRead()
    {
        // Arrange
        var read = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .AsRead()
            .Build();

        _dbContext.Notifications.Add(read);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.GetUnreadCountAsync(_recipientUser.Id);

        // Assert
        count.Should().Be(0);
    }

    #endregion

    #region SendNotificationAsync Tests

    [Fact]
    public async Task SendNotificationAsync_CreatesNotification_WithSender()
    {
        // Arrange
        var dto = new CreateNotificationDto
        {
            RecipientUserId = _recipientUser.Id,
            Message = "Hello from sender",
            Priority = NotificationPriority.High
        };

        // Act
        var result = await _sut.SendNotificationAsync(_senderUser.Id, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Id.Should().BeGreaterThan(0);

        var saved = await _dbContext.Notifications.FirstAsync();
        saved.RecipientUserId.Should().Be(_recipientUser.Id);
        saved.SenderUserId.Should().Be(_senderUser.Id);
        saved.Message.Should().Be("Hello from sender");
        saved.Priority.Should().Be(NotificationPriority.High);
        saved.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task SendNotificationAsync_SystemNotification_HasNullSender()
    {
        // Arrange
        var dto = new CreateNotificationDto
        {
            RecipientUserId = _recipientUser.Id,
            Message = "System maintenance scheduled",
            Priority = NotificationPriority.Urgent
        };

        // Act
        var result = await _sut.SendNotificationAsync(null, dto);

        // Assert
        result.Success.Should().BeTrue();

        var saved = await _dbContext.Notifications.FirstAsync();
        saved.SenderUserId.Should().BeNull();
        saved.Priority.Should().Be(NotificationPriority.Urgent);
    }

    [Fact]
    public async Task SendNotificationAsync_FailsForNonExistentRecipient()
    {
        // Arrange
        var dto = new CreateNotificationDto
        {
            RecipientUserId = "non-existent-user",
            Message = "This should fail"
        };

        // Act
        var result = await _sut.SendNotificationAsync(_senderUser.Id, dto);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Recipient not found");
    }

    [Fact]
    public async Task SendNotificationAsync_FailsForEmptyMessage()
    {
        // Arrange
        var dto = new CreateNotificationDto
        {
            RecipientUserId = _recipientUser.Id,
            Message = ""
        };

        // Act
        var result = await _sut.SendNotificationAsync(_senderUser.Id, dto);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Message is required");
    }

    #endregion

    #region MarkAsReadAsync Tests

    [Fact]
    public async Task MarkAsReadAsync_SetsIsReadAndReadAt()
    {
        // Arrange
        var notification = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .Build();

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.MarkAsReadAsync(notification.Id, _recipientUser.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updated = await _dbContext.Notifications.FindAsync(notification.Id);
        updated!.IsRead.Should().BeTrue();
        updated.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkAsReadAsync_FailsForWrongUser()
    {
        // Arrange
        var notification = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .Build();

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.MarkAsReadAsync(notification.Id, _senderUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task MarkAsReadAsync_AlreadyRead_ReturnsSuccess()
    {
        // Arrange
        var notification = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .AsRead()
            .Build();

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.MarkAsReadAsync(notification.Id, _recipientUser.Id);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region MarkAllAsReadAsync Tests

    [Fact]
    public async Task MarkAllAsReadAsync_MarksAllUnreadForUser()
    {
        // Arrange
        var unread1 = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .WithSenderUserId(_senderUser.Id)
            .WithMessage("Unread 1")
            .Build();

        var unread2 = NotificationBuilder.Default()
            .WithRecipientUserId(_recipientUser.Id)
            .AsSystemNotification()
            .WithMessage("Unread 2")
            .Build();

        // Notification for another user - should not be affected
        var otherUserNotification = NotificationBuilder.Default()
            .WithRecipientUserId(_senderUser.Id)
            .WithSenderUserId(_recipientUser.Id)
            .WithMessage("Other user's notification")
            .Build();

        _dbContext.Notifications.AddRange(unread1, unread2, otherUserNotification);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.MarkAllAsReadAsync(_recipientUser.Id);

        // Assert
        result.Success.Should().BeTrue();

        var recipientNotifications = await _dbContext.Notifications
            .Where(n => n.RecipientUserId == _recipientUser.Id)
            .ToListAsync();
        recipientNotifications.Should().AllSatisfy(n =>
        {
            n.IsRead.Should().BeTrue();
            n.ReadAt.Should().NotBeNull();
        });

        // Other user's notification should remain unread
        var otherNotification = await _dbContext.Notifications
            .FirstAsync(n => n.RecipientUserId == _senderUser.Id);
        otherNotification.IsRead.Should().BeFalse();
    }

    #endregion
}
