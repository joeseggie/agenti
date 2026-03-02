using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Users;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="LoginTelemetryService"/>.
/// </summary>
public class LoginTelemetryServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ILogger<LoginTelemetryService>> _loggerMock;
    private readonly TelemetryClient _telemetryClient;
    private readonly LoginTelemetryService _service;
    private readonly LoginTelemetryService _serviceWithoutTelemetry;

    public LoginTelemetryServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _loggerMock = new Mock<ILogger<LoginTelemetryService>>();

        var channel = new Mock<ITelemetryChannel>();
        channel.Setup(c => c.Send(It.IsAny<ITelemetry>()));
        var config = new TelemetryConfiguration { TelemetryChannel = channel.Object };
        _telemetryClient = new TelemetryClient(config);

        _service = new LoginTelemetryService(_dbContext, _loggerMock.Object, _telemetryClient);
        _serviceWithoutTelemetry = new LoginTelemetryService(_dbContext, _loggerMock.Object, null);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region RecordLoginSuccessAsync Tests

    [Fact]
    public async Task RecordLoginSuccessAsync_CreatesAuditLogEntry()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            UserName = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _service.RecordLoginSuccessAsync(
            "user-1", "test@example.com", "BlazorWeb",
            "192.168.1.1", "Mozilla/5.0", "Admin", 1, 150.5);

        // Assert
        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.UserId.Should().Be("user-1");
        auditLog.Action.Should().Be(UserAuditAction.LoginSuccess);
        auditLog.NewValue.Should().Contain("Admin");
        auditLog.NewValue.Should().Contain("1");
    }

    [Fact]
    public async Task RecordLoginSuccessAsync_IncludesIpAndUserAgentInContext()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user-2",
            Email = "test2@example.com",
            FirstName = "Test",
            LastName = "Two",
            UserName = "test2@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _service.RecordLoginSuccessAsync(
            "user-2", "test2@example.com", "ApiJwt",
            "10.0.0.1", "AgentiAndroid/1.0", "Agent", 2, null);

        // Assert
        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.OldValue.Should().Contain("10.0.0.1");
        auditLog.OldValue.Should().Contain("AgentiAndroid/1.0");
        auditLog.OldValue.Should().Contain("ApiJwt");
    }

    #endregion

    #region RecordLoginFailureAsync Tests

    [Fact]
    public async Task RecordLoginFailureAsync_CreatesAuditLogForExistingUser()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user-3",
            Email = "fail@example.com",
            FirstName = "Fail",
            LastName = "User",
            UserName = "fail@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _service.RecordLoginFailureAsync(
            "fail@example.com", "InvalidCredentials", "BlazorWeb",
            "192.168.1.100", "Mozilla/5.0", 200.0);

        // Assert
        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.UserId.Should().Be("user-3");
        auditLog.Action.Should().Be(UserAuditAction.LoginFailed);
        auditLog.NewValue.Should().Be("InvalidCredentials");
    }

    [Fact]
    public async Task RecordLoginFailureAsync_DoesNotCreateAuditLogForUnknownUser()
    {
        // Act
        await _service.RecordLoginFailureAsync(
            "unknown@example.com", "UserNotFound", "ApiJwt",
            "192.168.1.200", "Mozilla/5.0", 50.0);

        // Assert
        var auditLogs = await _dbContext.UserAuditLogs.CountAsync();
        auditLogs.Should().Be(0);
    }

    [Fact]
    public async Task RecordLoginFailureAsync_StoresFailureReasonInNewValue()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user-4",
            Email = "locked@example.com",
            FirstName = "Locked",
            LastName = "User",
            UserName = "locked@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _service.RecordLoginFailureAsync(
            "locked@example.com", "AccountLockedOut", "BlazorWeb",
            "10.0.0.5", "Chrome/120", null);

        // Assert
        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.NewValue.Should().Be("AccountLockedOut");
    }

    #endregion

    #region Null TelemetryClient Tests

    [Fact]
    public async Task RecordLoginSuccessAsync_WithNullTelemetryClient_DoesNotThrow()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user-5",
            Email = "notelemetry@example.com",
            FirstName = "No",
            LastName = "Telemetry",
            UserName = "notelemetry@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = () => _serviceWithoutTelemetry.RecordLoginSuccessAsync(
            "user-5", "notelemetry@example.com", "BlazorWeb",
            "127.0.0.1", null, "Agent", null, 100.0);

        // Assert
        await act.Should().NotThrowAsync();
        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordLoginFailureAsync_WithNullTelemetryClient_DoesNotThrow()
    {
        // Act
        var act = () => _serviceWithoutTelemetry.RecordLoginFailureAsync(
            "unknown@example.com", "InvalidCredentials", "ApiJwt",
            null, null, null);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void RecordTokenIssued_WithNullTelemetryClient_DoesNotThrow()
    {
        // Act
        var act = () => _serviceWithoutTelemetry.RecordTokenIssued(
            "user-1", "test@example.com", "ApiJwt", 60);

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
