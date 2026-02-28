using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Setup;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="SetupService"/>.
/// </summary>
public class SetupServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<RoleManager<IdentityRole>> _roleManagerMock;
    private readonly Mock<ILogger<SetupService>> _loggerMock;
    private readonly SetupService _setupService;

    public SetupServiceTests()
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

        // Mock RoleManager
        var roleStore = new Mock<IRoleStore<IdentityRole>>();
        _roleManagerMock = new Mock<RoleManager<IdentityRole>>(
            roleStore.Object, null, null, null, null);

        _loggerMock = new Mock<ILogger<SetupService>>();

        _setupService = new SetupService(
            _dbContext,
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region IsSetupCompleteAsync Tests

    [Fact]
    public async Task IsSetupCompleteAsync_WithNoSetup_ReturnsFalse()
    {
        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsSetupCompleteAsync_WithOnlyConfigFlag_ReturnsFalse()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeFalse(); // Missing admin, branch, and vault
    }

    [Fact]
    public async Task IsSetupCompleteAsync_WithConfigAndAdmin_ButNoBranch_ReturnsFalse()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });

        var admin = UserBuilder.Default()
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeFalse(); // Missing branch and vault
    }

    [Fact(Skip = "Branch auto-creates Vault via SaveChangesAsync override. Tested in integration tests.")]
    public async Task IsSetupCompleteAsync_WithConfigAdminAndBranch_ButNoVault_ReturnsFalse()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });

        var admin = UserBuilder.Default()
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(admin);

        var branch = new Branch { Id = 1, Name = "Main Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeFalse(); // Missing vault
    }

    [Fact]
    public async Task IsSetupCompleteAsync_WithAllRequirements_ReturnsTrue()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });

        var admin = UserBuilder.Default()
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(admin);

        var branch = new Branch { Id = 1, Name = "Main Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);

        var vault = VaultBuilder.Default().WithBranchId(branch.Id).Build();
        _dbContext.Vaults.Add(vault);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsSetupCompleteAsync_WithInactiveAdmin_ReturnsFalse()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });

        var inactiveAdmin = UserBuilder.Default()
            .WithRole(UserRole.Admin)
            .IsInactive()
            .Build();
        _dbContext.Users.Add(inactiveAdmin);

        var branch = new Branch { Id = 1, Name = "Main Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);

        var vault = VaultBuilder.Default().WithBranchId(branch.Id).Build();
        _dbContext.Vaults.Add(vault);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeFalse(); // Admin must be active
    }

    [Fact]
    public async Task IsSetupCompleteAsync_WithConfigSetToFalse_ReturnsFalse()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "false" });

        var admin = UserBuilder.Default()
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(admin);

        var branch = new Branch { Id = 1, Name = "Main Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);

        var vault = VaultBuilder.Default().WithBranchId(branch.Id).Build();
        _dbContext.Vaults.Add(vault);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeFalse(); // Config flag is false
    }

    [Fact]
    public async Task IsSetupCompleteAsync_WithNonAdminUser_ReturnsFalse()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });

        var agent = UserBuilder.Default()
            .WithRole(UserRole.Agent)
            .Build();
        _dbContext.Users.Add(agent);

        var branch = new Branch { Id = 1, Name = "Main Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);

        var vault = VaultBuilder.Default().WithBranchId(branch.Id).Build();
        _dbContext.Vaults.Add(vault);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _setupService.IsSetupCompleteAsync();

        // Assert
        result.Should().BeFalse(); // No admin user
    }

    #endregion

    #region CleanupDatabaseAsync Tests

    [Fact(Skip = "ExecuteDeleteAsync not supported in-memory. Tested in integration tests.")]
    public async Task CleanupDatabaseAsync_WithNoData_CompletesSuccessfully()
    {
        // Arrange
        _userManagerMock.Setup(x => x.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _setupService.CleanupDatabaseAsync();

        // Assert - Should complete without errors
        var users = await _dbContext.Users.ToListAsync();
        users.Should().BeEmpty();
    }

    [Fact(Skip = "ExecuteDeleteAsync not supported in-memory. Tested in integration tests.")]
    public async Task CleanupDatabaseAsync_WithSetupConfig_ResetsToFalse()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _setupService.CleanupDatabaseAsync();

        // Assert
        var config = await _dbContext.AppConfigs.FirstOrDefaultAsync(c => c.Key == "SetupComplete");
        config.Should().NotBeNull();
        config!.Value.Should().Be("false");
    }

    [Fact(Skip = "ExecuteDeleteAsync not supported in-memory. Tested in integration tests.")]
    public async Task CleanupDatabaseAsync_WithUsers_DeletesAllUsers()
    {
        // Arrange
        var user1 = UserBuilder.Default().WithEmail("user1@test.com").Build();
        var user2 = UserBuilder.Default().WithId("user-2").WithEmail("user2@test.com").Build();
        _dbContext.Users.AddRange(user1, user2);
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _setupService.CleanupDatabaseAsync();

        // Assert
        _userManagerMock.Verify(x => x.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Exactly(2));
    }

    [Fact(Skip = "ExecuteDeleteAsync not supported in-memory. Tested in integration tests.")]
    public async Task CleanupDatabaseAsync_WithBranchesAndVaults_DeletesAll()
    {
        // Arrange
        var branch = new Branch { Id = 1, Name = "Test Branch", CreatedAt = DateTimeOffset.UtcNow };
        var vault = VaultBuilder.Default().WithBranchId(branch.Id).Build();

        _dbContext.Branches.Add(branch);
        _dbContext.Vaults.Add(vault);
        await _dbContext.SaveChangesAsync();

        _userManagerMock.Setup(x => x.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _setupService.CleanupDatabaseAsync();

        // Assert
        var branches = await _dbContext.Branches.ToListAsync();
        var vaults = await _dbContext.Vaults.ToListAsync();

        branches.Should().BeEmpty();
        vaults.Should().BeEmpty();
    }

    #endregion

    #region CreateInitialAdminAndSetupAsync Tests

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_WhenAlreadyComplete_DoesNothing()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "true" });

        var admin = UserBuilder.Default()
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(admin);

        var branch = new Branch { Id = 1, Name = "Main Branch", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Branches.Add(branch);

        var vault = VaultBuilder.Default().WithBranchId(branch.Id).Build();
        _dbContext.Vaults.Add(vault);
        await _dbContext.SaveChangesAsync();

        // Act
        await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "Admin", "User", "New Branch");

        // Assert
        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        _roleManagerMock.Verify(x => x.CreateAsync(It.IsAny<IdentityRole>()), Times.Never);
    }

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_CreatesAdminRole_WhenNotExists()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync("Admin"))
            .ReturnsAsync(false);
        _roleManagerMock.Setup(x => x.CreateAsync(It.IsAny<IdentityRole>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        // Act
        await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "John", "Doe", "Main Branch");

        // Assert
        _roleManagerMock.Verify(x => x.CreateAsync(It.Is<IdentityRole>(r => r.Name == "Admin")), Times.Once);
    }

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_CreatesUser_WithCorrectProperties()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        ApplicationUser? createdUser = null;
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((user, password) => createdUser = user)
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "Jane", "Smith", "Main Branch");

        // Assert
        createdUser.Should().NotBeNull();
        createdUser!.Email.Should().Be("admin@test.com");
        createdUser.UserName.Should().Be("admin@test.com");
        createdUser.FirstName.Should().Be("Jane");
        createdUser.LastName.Should().Be("Smith");
        createdUser.Role.Should().Be(UserRole.Admin);
        createdUser.IsActive.Should().BeTrue();
        createdUser.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_CreatesBranch_WithCorrectName()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        // Act
        await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "John", "Doe", "Head Office");

        // Assert
        var branch = await _dbContext.Branches.FirstOrDefaultAsync();
        branch.Should().NotBeNull();
        branch!.Name.Should().Be("Head Office");
    }

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_AssignsAdminRole_ToUser()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        // Act
        await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "John", "Doe", "Main Branch");

        // Assert
        _userManagerMock.Verify(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Once);
    }

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_SetsSetupCompleteFlag_ToTrue()
    {
        // Arrange
        _dbContext.AppConfigs.Add(new AppConfig { Key = "SetupComplete", Value = "false" });
        await _dbContext.SaveChangesAsync();

        _roleManagerMock.Setup(x => x.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        // Act
        await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "John", "Doe", "Main Branch");

        // Assert
        var config = await _dbContext.AppConfigs.FirstOrDefaultAsync(c => c.Key == "SetupComplete");
        config.Should().NotBeNull();
        config!.Value.Should().Be("true");
    }

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_WhenUserCreationFails_ThrowsException()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "User creation failed" }));
        _userManagerMock.Setup(x => x.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act & Assert
        var act = async () => await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "John", "Doe", "Main Branch");

        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("*User creation failed*");
    }

    [Fact]
    public async Task CreateInitialAdminAndSetupAsync_WhenRoleCreationFails_ThrowsException()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync("Admin"))
            .ReturnsAsync(false);
        _roleManagerMock.Setup(x => x.CreateAsync(It.IsAny<IdentityRole>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role creation failed" }));

        // Act & Assert
        var act = async () => await _setupService.CreateInitialAdminAndSetupAsync(
            "admin@test.com", "Password123!", "John", "Doe", "Main Branch");

        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("*Role creation failed*");
    }

    #endregion
}
