using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="UserService"/>.
/// </summary>
public class UserServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Mock UserManager
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null, null, null, null, null, null, null, null);

        _userService = new UserService(_dbContext, _userManagerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_WithNoUsers_ReturnsEmptyList()
    {
        // Act
        var result = await _userService.GetAllAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithMultipleUsers_ReturnsOrderedByLastNameThenFirstName()
    {
        // Arrange
        var user1 = UserBuilder.Default().WithId("1").WithFirstName("Alice").WithLastName("Smith").Build();
        var user2 = UserBuilder.Default().WithId("2").WithFirstName("Bob").WithLastName("Johnson").Build();
        var user3 = UserBuilder.Default().WithId("3").WithFirstName("Charlie").WithLastName("Smith").Build();

        _dbContext.Users.AddRange(user1, user2, user3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.GetAllAsync();

        // Assert
        result.Should().HaveCount(3);
        result[0].FullName.Should().Be("Bob Johnson");
        result[1].FullName.Should().Be("Alice Smith");
        result[2].FullName.Should().Be("Charlie Smith");
    }

    [Fact]
    public async Task GetAllAsync_WithSearchByEmail_ReturnsMatchingUsers()
    {
        // Arrange
        var user1 = UserBuilder.Default().WithId("1").WithEmail("alice@test.com").Build();
        var user2 = UserBuilder.Default().WithId("2").WithEmail("bob@test.com").Build();
        var user3 = UserBuilder.Default().WithId("3").WithEmail("alice@example.com").Build();

        _dbContext.Users.AddRange(user1, user2, user3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.GetAllAsync("alice");

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(u => u.Email == "alice@test.com");
        result.Should().Contain(u => u.Email == "alice@example.com");
    }

    [Fact]
    public async Task GetAllAsync_WithSearchByFirstName_ReturnsMatchingUsers()
    {
        // Arrange
        var user1 = UserBuilder.Default().WithId("1").WithFirstName("John").Build();
        var user2 = UserBuilder.Default().WithId("2").WithFirstName("Jane").Build();
        var user3 = UserBuilder.Default().WithId("3").WithFirstName("Johnny").Build();

        _dbContext.Users.AddRange(user1, user2, user3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.GetAllAsync("john");

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(u => u.FullName.Contains("John"));
        result.Should().Contain(u => u.FullName.Contains("Johnny"));
    }

    [Fact]
    public async Task GetAllAsync_SearchIsCaseInsensitive()
    {
        // Arrange
        var user = UserBuilder.Default().WithEmail("Alice@Test.COM").Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.GetAllAsync("alice");

        // Assert
        result.Should().HaveCount(1);
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_WithExistingUser_ReturnsUserDetail()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithFirstName("John")
            .WithLastName("Doe")
            .WithEmail("john@test.com")
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.GetByIdAsync("user-123");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("user-123");
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.Email.Should().Be("john@test.com");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentUser_ReturnsNull()
    {
        // Act
        var result = await _userService.GetByIdAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_WithNullModel_ReturnsError()
    {
        // Act
        var result = await _userService.CreateUserAsync(null!, "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Invalid request");
    }

    [Fact]
    public async Task CreateUserAsync_WithDuplicateEmail_ReturnsError()
    {
        // Arrange
        var existingUser = UserBuilder.Default().WithEmail("existing@test.com").Build();
        _userManagerMock.Setup(x => x.FindByEmailAsync("existing@test.com"))
            .ReturnsAsync(existingUser);

        var model = new CreateUserModel
        {
            Email = "existing@test.com",
            FirstName = "Test",
            LastName = "User",
            BranchId = 1
        };

        // Act
        var result = await _userService.CreateUserAsync(model, "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateUserAsync_WithNonExistentBranch_ReturnsError()
    {
        // Arrange
        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var model = new CreateUserModel
        {
            Email = "new@test.com",
            FirstName = "Test",
            LastName = "User",
            BranchId = 999 // Non-existent branch
        };

        // Act
        var result = await _userService.CreateUserAsync(model, "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Branch with ID 999 does not exist");
    }

    [Fact]
    public async Task CreateUserAsync_WithValidData_CreatesUserSuccessfully()
    {
        // Arrange
        var branch = new Branch { Id = 1, Name = "Test Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var model = new CreateUserModel
        {
            Email = "new@test.com",
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "1234567890",
            BranchId = 1
        };

        // Act
        var result = await _userService.CreateUserAsync(model, "admin-123");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().NotBeNullOrEmpty();
        result.TemporaryPassword.Should().NotBeNullOrEmpty();
        result.TemporaryPassword!.Length.Should().Be(16);
        result.InviteToken.Should().NotBeNullOrEmpty();

        // Verify audit log was created
        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be(UserAuditAction.Created);
        auditLog.PerformedByUserId.Should().Be("admin-123");
    }

    [Fact]
    public async Task CreateUserAsync_WithNullPerformedByUserId_CreatesUserWithNullAuditActor()
    {
        // Arrange
        var branch = new Branch { Id = 1, Name = "Test Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var model = new CreateUserModel
        {
            Email = "new@test.com",
            FirstName = "Test",
            LastName = "User",
            BranchId = 1
        };

        // Act - passing null performedByUserId (e.g., when authentication context is unavailable)
        var result = await _userService.CreateUserAsync(model, null);

        // Assert
        result.Success.Should().BeTrue();

        // Verify audit log was created with null PerformedByUserId (no FK violation)
        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be(UserAuditAction.Created);
        auditLog.PerformedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task CreateUserAsync_WhenUserManagerFails_ReturnsError()
    {
        // Arrange
        var branch = new Branch { Id = 1, Name = "Test Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));

        var model = new CreateUserModel
        {
            Email = "new@test.com",
            FirstName = "Test",
            LastName = "User",
            BranchId = 1
        };

        // Act
        var result = await _userService.CreateUserAsync(model, "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Password too weak");
    }

    #endregion

    #region UpdateProfileAsync Tests

    [Fact]
    public async Task UpdateProfileAsync_WithNullModel_ReturnsError()
    {
        // Act
        var result = await _userService.UpdateProfileAsync(null!, "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Invalid request");
    }

    [Fact]
    public async Task UpdateProfileAsync_WithNonExistentUser_ReturnsError()
    {
        // Arrange
        var model = new UserFormModel { Id = "non-existent", FirstName = "Test", LastName = "User" };

        // Act
        var result = await _userService.UpdateProfileAsync(model, "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User not found");
    }

    [Fact]
    public async Task UpdateProfileAsync_WithValidData_UpdatesUser()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithId("user-123")
            .WithFirstName("Old")
            .WithLastName("Name")
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var model = new UserFormModel
        {
            Id = "user-123",
            FirstName = "New",
            LastName = "Name",
            PhoneNumber = "9876543210"
        };

        // Act
        var result = await _userService.UpdateProfileAsync(model, "admin-123");

        // Assert
        result.Success.Should().BeTrue();

        var updatedUser = await _dbContext.Users.FindAsync("user-123");
        updatedUser!.FirstName.Should().Be("New");
        updatedUser.LastName.Should().Be("Name");
        updatedUser.PhoneNumber.Should().Be("9876543210");
        updatedUser.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region ChangeRoleAsync Tests

    [Fact]
    public async Task ChangeRoleAsync_WhenChangingOwnRole_ReturnsError()
    {
        // Act
        var result = await _userService.ChangeRoleAsync("user-123", UserRole.Admin, "user-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("You cannot change your own role.");
    }

    [Fact]
    public async Task ChangeRoleAsync_WithNonExistentUser_ReturnsError()
    {
        // Arrange
        _userManagerMock.Setup(x => x.FindByIdAsync("non-existent"))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _userService.ChangeRoleAsync("non-existent", UserRole.Admin, "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User not found");
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenRoleIsSame_ReturnsSuccess()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").WithRole(UserRole.Agent).Build();
        _userManagerMock.Setup(x => x.FindByIdAsync("user-123")).ReturnsAsync(user);

        // Act
        var result = await _userService.ChangeRoleAsync("user-123", UserRole.Agent, "admin-123");

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenDemotingLastAdmin_ReturnsError()
    {
        // Arrange
        var admin = UserBuilder.Default().WithId("admin-123").WithRole(UserRole.Admin).Build();
        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.FindByIdAsync("admin-123")).ReturnsAsync(admin);

        // Act
        var result = await _userService.ChangeRoleAsync("admin-123", UserRole.Agent, "other-admin");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Cannot remove the last remaining Admin.");
    }

    [Fact]
    public async Task ChangeRoleAsync_WithValidData_ChangesRoleAndLogsAudit()
    {
        // Arrange
        var admin1 = UserBuilder.Default().WithId("admin-1").WithRole(UserRole.Admin).Build();
        var admin2 = UserBuilder.Default().WithId("admin-2").WithRole(UserRole.Admin).Build();
        _dbContext.Users.AddRange(admin1, admin2);
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.FindByIdAsync("admin-2")).ReturnsAsync(admin2);
        _userManagerMock.Setup(x => x.RemoveFromRoleAsync(admin2, "Admin"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(admin2, "Agent"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(admin2))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _userService.ChangeRoleAsync("admin-2", UserRole.Agent, "admin-1");

        // Assert
        result.Success.Should().BeTrue();

        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be(UserAuditAction.RoleChanged);
        auditLog.OldValue.Should().Be("Admin");
        auditLog.NewValue.Should().Be("Agent");
    }

    #endregion

    #region DeactivateAsync Tests

    [Fact]
    public async Task DeactivateAsync_WhenDeactivatingOwnAccount_ReturnsError()
    {
        // Act
        var result = await _userService.DeactivateAsync("user-123", "user-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("You cannot deactivate your own account.");
    }

    [Fact]
    public async Task DeactivateAsync_WithNonExistentUser_ReturnsError()
    {
        // Act
        var result = await _userService.DeactivateAsync("non-existent", "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User not found");
    }

    [Fact]
    public async Task DeactivateAsync_WhenAlreadyInactive_ReturnsSuccess()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").IsInactive().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.DeactivateAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateAsync_WhenDeactivatingLastAdmin_ReturnsError()
    {
        // Arrange
        var admin = UserBuilder.Default().WithId("admin-123").WithRole(UserRole.Admin).Build();
        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.DeactivateAsync("admin-123", "other-admin");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Cannot deactivate the last remaining Admin.");
    }

    [Fact]
    public async Task DeactivateAsync_WithValidUser_DeactivatesAndLogsAudit()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.DeactivateAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeTrue();

        var deactivatedUser = await _dbContext.Users.FindAsync("user-123");
        deactivatedUser!.IsActive.Should().BeFalse();

        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be(UserAuditAction.Deactivated);
    }

    #endregion

    #region ReactivateAsync Tests

    [Fact]
    public async Task ReactivateAsync_WithNonExistentUser_ReturnsError()
    {
        // Act
        var result = await _userService.ReactivateAsync("non-existent", "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User not found");
    }

    [Fact]
    public async Task ReactivateAsync_WhenAlreadyActive_ReturnsSuccess()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.ReactivateAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ReactivateAsync_WithInactiveUser_ReactivatesAndLogsAudit()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").IsInactive().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.ReactivateAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeTrue();

        var reactivatedUser = await _dbContext.Users.FindAsync("user-123");
        reactivatedUser!.IsActive.Should().BeTrue();

        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be(UserAuditAction.Reactivated);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WhenDeletingOwnAccount_ReturnsError()
    {
        // Act
        var result = await _userService.DeleteAsync("user-123", "user-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("You cannot delete your own account.");
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentUser_ReturnsError()
    {
        // Act
        var result = await _userService.DeleteAsync("non-existent", "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User not found");
    }

    [Fact]
    public async Task DeleteAsync_WhenDeletingLastAdmin_ReturnsError()
    {
        // Arrange
        var admin = UserBuilder.Default().WithId("admin-123").WithRole(UserRole.Admin).Build();
        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.DeleteAsync("admin-123", "other-admin");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Cannot delete the last remaining Admin.");
    }

    [Fact]
    public async Task DeleteAsync_WhenUserLinkedToAgent_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").WithAgentId(1).Build();
        var agent = AgentBuilder.Default().WithId(1).WithUser(user).Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.DeleteAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Cannot delete a user linked to an Agent");
    }

    [Fact(Skip = "Foreign key constraint with DeleteBehavior.Restrict not supported in-memory. Tested in integration tests.")]
    public async Task DeleteAsync_WithValidUser_DeletesAndLogsAudit()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.DeleteAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeTrue();

        var deletedUser = await _dbContext.Users.FindAsync("user-123");
        deletedUser.Should().BeNull();

        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be(UserAuditAction.Deleted);
    }

    #endregion

    #region ResetPasswordAsync Tests

    [Fact]
    public async Task ResetPasswordAsync_WhenResettingOwnPassword_ReturnsError()
    {
        // Act
        var result = await _userService.ResetPasswordAsync("user-123", "user-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("You cannot reset your own password.");
    }

    [Fact]
    public async Task ResetPasswordAsync_WithNonExistentUser_ReturnsError()
    {
        // Arrange
        _userManagerMock.Setup(x => x.FindByIdAsync("non-existent"))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _userService.ResetPasswordAsync("non-existent", "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User not found");
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidUser_ResetsPasswordAndLogsAudit()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").Build();
        _userManagerMock.Setup(x => x.FindByIdAsync("user-123")).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.RemovePasswordAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddPasswordAsync(user, It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _userService.ResetPasswordAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeTrue();
        result.NewPassword.Should().NotBeNullOrEmpty();
        result.NewPassword!.Length.Should().Be(16);

        var auditLog = await _dbContext.UserAuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be(UserAuditAction.PasswordReset);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenRemovePasswordFails_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default().WithId("user-123").Build();
        _userManagerMock.Setup(x => x.FindByIdAsync("user-123")).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.RemovePasswordAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Remove failed" }));

        // Act
        var result = await _userService.ResetPasswordAsync("user-123", "admin-123");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Remove failed");
    }

    #endregion
}
