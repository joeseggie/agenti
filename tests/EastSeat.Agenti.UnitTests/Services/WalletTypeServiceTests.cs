using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.WalletTypes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="WalletTypeService"/>.
/// </summary>
public class WalletTypeServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly WalletTypeService _walletTypeService;

    public WalletTypeServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _walletTypeService = new WalletTypeService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetWalletTypesAsync Tests

    [Fact]
    public async Task GetWalletTypesAsync_WithNoWalletTypes_ReturnsEmptyList()
    {
        // Act
        var result = await _walletTypeService.GetWalletTypesAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWalletTypesAsync_WithMultipleWalletTypes_ReturnsAllOrderedByName()
    {
        // Arrange
        var walletType1 = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Zebra Type")
            .WithType(WalletTypeEnum.Cash)
            .Build();

        var walletType2 = WalletTypeBuilder.Default()
            .WithId(2)
            .WithName("Alpha Type")
            .WithType(WalletTypeEnum.Bank)
            .Build();

        var walletType3 = WalletTypeBuilder.Default()
            .WithId(3)
            .WithName("Bravo Type")
            .WithType(WalletTypeEnum.MobileMoney)
            .Build();

        _dbContext.WalletTypes.AddRange(walletType1, walletType2, walletType3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.GetWalletTypesAsync();

        // Assert
        result.Should().HaveCount(3);
        result[0].Name.Should().Be("Alpha Type");
        result[1].Name.Should().Be("Bravo Type");
        result[2].Name.Should().Be("Zebra Type");
    }

    [Fact]
    public async Task GetWalletTypesAsync_IncludesWalletCount()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Test Type")
            .Build();

        var wallet1 = WalletBuilder.Default()
            .WithId(1)
            .WithName("Wallet 1")
            .WithWalletType(walletType)
            .Build();

        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithName("Wallet 2")
            .WithWalletType(walletType)
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.AddRange(wallet1, wallet2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.GetWalletTypesAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].WalletCount.Should().Be(2);
    }

    [Fact]
    public async Task GetWalletTypesAsync_ReturnsAllProperties()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow.AddDays(-10);
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Test Type")
            .WithDescription("Test Description")
            .WithType(WalletTypeEnum.Custom)
            .IsSystem()
            .IsInactive()
            .WithSupportsDenominations(true)
            .WithCreatedAt(createdAt)
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.GetWalletTypesAsync();

        // Assert
        result.Should().HaveCount(1);
        var dto = result[0];
        dto.Id.Should().Be(1);
        dto.Name.Should().Be("Test Type");
        dto.Description.Should().Be("Test Description");
        dto.Type.Should().Be(WalletTypeEnum.Custom);
        dto.IsSystem.Should().BeTrue();
        dto.IsActive.Should().BeFalse();
        dto.SupportsDenominations.Should().BeTrue();
        dto.CreatedAt.Should().Be(createdAt);
    }

    #endregion

    #region GetWalletTypeAsync Tests

    [Fact]
    public async Task GetWalletTypeAsync_WithExistingId_ReturnsWalletType()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Test Type")
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.GetWalletTypeAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Test Type");
    }

    [Fact]
    public async Task GetWalletTypeAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _walletTypeService.GetWalletTypeAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWalletTypeAsync_IncludesWalletCount()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Test Type")
            .Build();

        var wallet = WalletBuilder.Default()
            .WithId(1)
            .WithName("Wallet 1")
            .WithWalletType(walletType)
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.GetWalletTypeAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.WalletCount.Should().Be(1);
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesWalletType()
    {
        // Arrange
        var model = new WalletTypeFormModel
        {
            Name = "New Type",
            Description = "New Description",
            Type = WalletTypeEnum.MobileMoney,
            SupportsDenominations = true,
            IsActive = true
        };

        // Act
        var result = await _walletTypeService.CreateAsync(model);

        // Assert
        result.Success.Should().BeTrue();
        result.Id.Should().BeGreaterThan(0);

        var createdType = await _dbContext.WalletTypes.FindAsync(result.Id);
        createdType.Should().NotBeNull();
        createdType!.Name.Should().Be("New Type");
        createdType.Description.Should().Be("New Description");
        createdType.Type.Should().Be(WalletTypeEnum.MobileMoney);
        createdType.SupportsDenominations.Should().BeTrue();
        createdType.IsActive.Should().BeTrue();
        createdType.IsSystem.Should().BeFalse(); // User-created types are not system
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ReturnsError()
    {
        // Arrange
        var existingType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Existing Type")
            .Build();

        _dbContext.WalletTypes.Add(existingType);
        await _dbContext.SaveChangesAsync();

        var model = new WalletTypeFormModel
        {
            Name = "Existing Type",
            Description = "New Description",
            Type = WalletTypeEnum.Cash
        };

        // Act
        var result = await _walletTypeService.CreateAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedAtTimestamp()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;
        var model = new WalletTypeFormModel
        {
            Name = "New Type",
            Description = "Description",
            Type = WalletTypeEnum.Cash
        };

        // Act
        var result = await _walletTypeService.CreateAsync(model);
        var after = DateTimeOffset.UtcNow;

        // Assert
        var createdType = await _dbContext.WalletTypes.FindAsync(result.Id);
        createdType!.CreatedAt.Should().BeOnOrAfter(before);
        createdType.CreatedAt.Should().BeOnOrBefore(after);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithValidData_UpdatesWalletType()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Original Name")
            .WithDescription("Original Description")
            .WithType(WalletTypeEnum.Cash)
            .WithSupportsDenominations(false)
            .IsInactive()
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var model = new WalletTypeFormModel
        {
            Id = 1,
            Name = "Updated Name",
            Description = "Updated Description",
            Type = WalletTypeEnum.Bank,
            SupportsDenominations = true,
            IsActive = true
        };

        // Act
        var result = await _walletTypeService.UpdateAsync(model);

        // Assert
        result.Success.Should().BeTrue();

        var updatedType = await _dbContext.WalletTypes.FindAsync(1L);
        updatedType.Should().NotBeNull();
        updatedType!.Name.Should().Be("Updated Name");
        updatedType.Description.Should().Be("Updated Description");
        updatedType.Type.Should().Be(WalletTypeEnum.Bank);
        updatedType.SupportsDenominations.Should().BeTrue();
        updatedType.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_WithoutId_ReturnsError()
    {
        // Arrange
        var model = new WalletTypeFormModel
        {
            Name = "Name",
            Description = "Description"
        };

        // Act
        var result = await _walletTypeService.UpdateAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ID is required");
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ReturnsError()
    {
        // Arrange
        var model = new WalletTypeFormModel
        {
            Id = 999,
            Name = "Name",
            Description = "Description"
        };

        // Act
        var result = await _walletTypeService.UpdateAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateAsync_WithDuplicateName_ReturnsError()
    {
        // Arrange
        var walletType1 = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .Build();

        var walletType2 = WalletTypeBuilder.Default()
            .WithId(2)
            .WithName("Type 2")
            .Build();

        _dbContext.WalletTypes.AddRange(walletType1, walletType2);
        await _dbContext.SaveChangesAsync();

        var model = new WalletTypeFormModel
        {
            Id = 2,
            Name = "Type 1", // Duplicate of Type 1
            Description = "Description"
        };

        // Act
        var result = await _walletTypeService.UpdateAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already exists");
    }

    [Fact]
    public async Task UpdateAsync_WithSameName_Succeeds()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .WithDescription("Original Description")
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var model = new WalletTypeFormModel
        {
            Id = 1,
            Name = "Type 1", // Same name
            Description = "Updated Description"
        };

        // Act
        var result = await _walletTypeService.UpdateAsync(model);

        // Assert
        result.Success.Should().BeTrue();

        var updatedType = await _dbContext.WalletTypes.FindAsync(1L);
        updatedType!.Description.Should().Be("Updated Description");
    }

    [Fact]
    public async Task UpdateAsync_SetsUpdatedAtTimestamp()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;
        var model = new WalletTypeFormModel
        {
            Id = 1,
            Name = "Updated Name",
            Description = "Description"
        };

        // Act
        var result = await _walletTypeService.UpdateAsync(model);
        var after = DateTimeOffset.UtcNow;

        // Assert
        var updatedType = await _dbContext.WalletTypes.FindAsync(1L);
        updatedType!.UpdatedAt.Should().NotBeNull();
        updatedType.UpdatedAt!.Value.Should().BeOnOrAfter(before);
        updatedType.UpdatedAt.Value.Should().BeOnOrBefore(after);
    }

    #endregion

    #region ToggleStatusAsync Tests

    [Fact]
    public async Task ToggleStatusAsync_WithActiveWalletType_MakesInactive()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .Build(); // IsActive = true by default

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.ToggleStatusAsync(1);

        // Assert
        result.Success.Should().BeTrue();

        var toggledType = await _dbContext.WalletTypes.FindAsync(1L);
        toggledType!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleStatusAsync_WithInactiveWalletType_MakesActive()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .IsInactive()
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.ToggleStatusAsync(1);

        // Assert
        result.Success.Should().BeTrue();

        var toggledType = await _dbContext.WalletTypes.FindAsync(1L);
        toggledType!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleStatusAsync_WithNonExistentId_ReturnsError()
    {
        // Act
        var result = await _walletTypeService.ToggleStatusAsync(999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ToggleStatusAsync_SetsUpdatedAtTimestamp()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;

        // Act
        var result = await _walletTypeService.ToggleStatusAsync(1);
        var after = DateTimeOffset.UtcNow;

        // Assert
        var toggledType = await _dbContext.WalletTypes.FindAsync(1L);
        toggledType!.UpdatedAt.Should().NotBeNull();
        toggledType.UpdatedAt!.Value.Should().BeOnOrAfter(before);
        toggledType.UpdatedAt.Value.Should().BeOnOrBefore(after);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithValidWalletType_DeletesSuccessfully()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.DeleteAsync(1);

        // Assert
        result.Success.Should().BeTrue();

        var deletedType = await _dbContext.WalletTypes.FindAsync(1L);
        deletedType.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ReturnsError()
    {
        // Act
        var result = await _walletTypeService.DeleteAsync(999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteAsync_WithSystemWalletType_ReturnsError()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("System Type")
            .IsSystem()
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.DeleteAsync(1);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("System wallet types cannot be deleted");

        // Verify it wasn't deleted
        var walletTypeStillExists = await _dbContext.WalletTypes.FindAsync(1L);
        walletTypeStillExists.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithExistingWallets_ReturnsError()
    {
        // Arrange
        var walletType = WalletTypeBuilder.Default()
            .WithId(1)
            .WithName("Type 1")
            .Build();

        var wallet = WalletBuilder.Default()
            .WithId(1)
            .WithName("Wallet 1")
            .WithWalletType(walletType)
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _walletTypeService.DeleteAsync(1);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cannot delete wallet type with existing wallets");

        // Verify it wasn't deleted
        var walletTypeStillExists = await _dbContext.WalletTypes.FindAsync(1L);
        walletTypeStillExists.Should().NotBeNull();
    }

    #endregion
}
