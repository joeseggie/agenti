using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "Notifications")]
public class NotificationServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly NotificationService _sut;
    private readonly ApplicationUser _recipientUser;
    private readonly ApplicationUser _senderUser;

    public NotificationServiceTests()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContextFactory<ApplicationDbContext>(opts =>
            opts.UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        _serviceProvider = services.BuildServiceProvider();
        _dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        _dbContext = _dbContextFactory.CreateDbContext();

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

        _sut = new NotificationService(_dbContextFactory);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
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
        result.Id.Should().NotBe(Guid.Empty);

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

        await using var verifyCtx = _dbContextFactory.CreateDbContext();
        var updated = await verifyCtx.Notifications.FindAsync(notification.Id);
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

        await using var verifyCtx = _dbContextFactory.CreateDbContext();
        var recipientNotifications = await verifyCtx.Notifications
            .Where(n => n.RecipientUserId == _recipientUser.Id)
            .ToListAsync();
        recipientNotifications.Should().AllSatisfy(n =>
        {
            n.IsRead.Should().BeTrue();
            n.ReadAt.Should().NotBeNull();
        });

        // Other user's notification should remain unread
        var otherNotification = await verifyCtx.Notifications
            .FirstAsync(n => n.RecipientUserId == _senderUser.Id);
        otherNotification.IsRead.Should().BeFalse();
    }

    #endregion

    #region CreateSystemNotificationAsync Tests

    [Fact]
    public async Task CreateSystemNotificationAsync_CreatesNotificationWithCorrectFields()
    {
        // Act
        await _sut.CreateSystemNotificationAsync(
            _recipientUser.Id, "Alert Title", "Alert body", NotificationType.CountPendingApproval, "/some/link");

        // Assert
        var saved = await _dbContext.Notifications.FirstAsync();
        saved.RecipientUserId.Should().Be(_recipientUser.Id);
        saved.SenderUserId.Should().BeNull();
        saved.Title.Should().Be("Alert Title");
        saved.Message.Should().Be("Alert body");
        saved.Type.Should().Be(NotificationType.CountPendingApproval);
        saved.LinkUrl.Should().Be("/some/link");
        saved.Priority.Should().Be(NotificationPriority.Normal);
        saved.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task CreateSystemNotificationAsync_SessionBlocked_SetsHighPriority()
    {
        // Act
        await _sut.CreateSystemNotificationAsync(
            _recipientUser.Id, "Blocked", "Session blocked", NotificationType.SessionBlocked);

        // Assert
        var saved = await _dbContext.Notifications.FirstAsync();
        saved.Priority.Should().Be(NotificationPriority.High);
    }

    [Fact]
    public async Task CreateSystemNotificationAsync_NullLinkUrl_SavesWithoutLink()
    {
        // Act
        await _sut.CreateSystemNotificationAsync(
            _recipientUser.Id, "Title", "Message", NotificationType.CountApproved);

        // Assert
        var saved = await _dbContext.Notifications.FirstAsync();
        saved.LinkUrl.Should().BeNull();
    }

    #endregion

    #region NotifyBranchAdminsAsync Tests

    [Fact]
    public async Task NotifyBranchAdminsAsync_SendsToAdminsAndSupervisors()
    {
        // Arrange
        var branch = new EastSeat.Agenti.Shared.Domain.Entities.Branch
        {
            Id = 10,
            Name = "Test Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(branch);

        var admin = UserBuilder.Default()
            .WithId("admin-1")
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(10)
            .Build();

        var supervisor = UserBuilder.Default()
            .WithId("supervisor-1")
            .WithEmail("supervisor@test.com")
            .WithRole(UserRole.Supervisor)
            .WithBranchId(10)
            .Build();

        var agent = UserBuilder.Default()
            .WithId("agent-1")
            .WithEmail("agent@test.com")
            .WithRole(UserRole.Agent)
            .WithBranchId(10)
            .Build();

        _dbContext.Users.AddRange(admin, supervisor, agent);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.NotifyBranchAdminsAsync(10, "Test", "Test message", NotificationType.CountPendingApproval);

        // Assert — only admin and supervisor should get notifications
        var notifications = await _dbContext.Notifications
            .Where(n => n.Title == "Test")
            .ToListAsync();

        notifications.Should().HaveCount(2);
        notifications.Select(n => n.RecipientUserId).Should().BeEquivalentTo(new[] { "admin-1", "supervisor-1" });
        notifications.Should().AllSatisfy(n =>
        {
            n.SenderUserId.Should().BeNull();
            n.Message.Should().Be("Test message");
            n.Type.Should().Be(NotificationType.CountPendingApproval);
            n.Priority.Should().Be(NotificationPriority.Normal);
        });
    }

    [Fact]
    public async Task NotifyBranchAdminsAsync_SessionBlocked_SetsHighPriority()
    {
        // Arrange
        var branch = new EastSeat.Agenti.Shared.Domain.Entities.Branch
        {
            Id = 11,
            Name = "Branch 2",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(branch);

        var admin = UserBuilder.Default()
            .WithId("admin-hp")
            .WithEmail("admin-hp@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(11)
            .Build();

        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.NotifyBranchAdminsAsync(11, "Blocked", "Session blocked", NotificationType.SessionBlocked);

        // Assert
        var saved = await _dbContext.Notifications.FirstAsync(n => n.RecipientUserId == "admin-hp");
        saved.Priority.Should().Be(NotificationPriority.High);
    }

    [Fact]
    public async Task NotifyBranchAdminsAsync_NoAdminsInBranch_DoesNotSave()
    {
        // Arrange
        var branch = new EastSeat.Agenti.Shared.Domain.Entities.Branch
        {
            Id = 12,
            Name = "Empty Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();

        var countBefore = await _dbContext.Notifications.CountAsync();

        // Act
        await _sut.NotifyBranchAdminsAsync(12, "Title", "Message", NotificationType.CountApproved);

        // Assert
        var countAfter = await _dbContext.Notifications.CountAsync();
        countAfter.Should().Be(countBefore);
    }

    [Fact]
    public async Task NotifyBranchAdminsAsync_ExcludesInactiveUsers()
    {
        // Arrange
        var branch = new EastSeat.Agenti.Shared.Domain.Entities.Branch
        {
            Id = 13,
            Name = "Mixed Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(branch);

        var activeAdmin = UserBuilder.Default()
            .WithId("active-admin")
            .WithEmail("active@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(13)
            .Build();

        var inactiveAdmin = UserBuilder.Default()
            .WithId("inactive-admin")
            .WithEmail("inactive@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(13)
            .IsInactive()
            .Build();

        _dbContext.Users.AddRange(activeAdmin, inactiveAdmin);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.NotifyBranchAdminsAsync(13, "Title", "Message", NotificationType.CountApproved);

        // Assert
        var notifications = await _dbContext.Notifications
            .Where(n => n.Title == "Title")
            .ToListAsync();

        notifications.Should().HaveCount(1);
        notifications[0].RecipientUserId.Should().Be("active-admin");
    }

    #endregion
}
