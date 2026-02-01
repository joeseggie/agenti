using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Agents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "AgentManagement")]
public class AgentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AgentService _sut;
    private readonly Branch _testBranch;

    public AgentServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Seed required data
        _testBranch = new Branch
        {
            Id = 1,
            Name = "Test Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(_testBranch);
        _dbContext.SaveChanges();

        _sut = new AgentService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetAgentsAsync Tests

    [Fact]
    public async Task GetAgentsAsync_WithMultipleAgents_ReturnsOrderedByCode()
    {
        // Arrange
        var user1 = UserBuilder.Default().WithEmail("user1@test.com").Build();
        var user2 = UserBuilder.Default().WithEmail("user2@test.com").Build();
        _dbContext.Users.AddRange(user1, user2);
        await _dbContext.SaveChangesAsync();

        var agent1 = AgentBuilder.Default()
            .WithId(1)
            .WithCode("ZZAA")
            .WithUserId(user1.Id)
            .WithBranchId(_testBranch.Id)
            .Build();

        var agent2 = AgentBuilder.Default()
            .WithId(2)
            .WithCode("AABB")
            .WithUserId(user2.Id)
            .WithBranchId(_testBranch.Id)
            .Build();

        _dbContext.Agents.AddRange(agent1, agent2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAgentsAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Code.Should().Be("AABB"); // Ordered by code
        result[1].Code.Should().Be("ZZAA");
    }

    [Fact]
    public async Task GetAgentsAsync_CalculatesWalletCountAndBalance_Correctly()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var wallet1 = WalletBuilder.Default()
            .WithId(1)
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithBalance(1000m)
            .Build();

        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithBalance(2500m)
            .Build();

        var wallet3 = WalletBuilder.Default()
            .WithId(3)
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithBalance(500m)
            .IsInactive()
            .Build();

        _dbContext.Wallets.AddRange(wallet1, wallet2, wallet3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAgentsAsync();

        // Assert
        result.Should().ContainSingle();
        result[0].WalletCount.Should().Be(2); // Only active wallets
        result[0].TotalBalance.Should().Be(3500m); // Only active wallet balances
    }

    #endregion

    #region GetAgentAsync Tests

    [Fact]
    public async Task GetAgentAsync_WithExistingAgent_ReturnsDetails()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithFirstName("John")
            .WithLastName("Doe")
            .WithEmail("john@test.com")
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithCode("JODO")
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAgentAsync(agent.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Code.Should().Be("JODO");
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.Email.Should().Be("john@test.com");
    }

    [Fact]
    public async Task GetAgentAsync_WithNonExistentAgent_ReturnsNull()
    {
        // Act
        var result = await _sut.GetAgentAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetAvailableUsersAsync Tests

    [Fact]
    public async Task GetAvailableUsersAsync_ReturnsOnlyActiveUsersWithoutAgents()
    {
        // Arrange
        var userWithAgent = UserBuilder.Default()
            .WithEmail("agent@test.com")
            .WithAgentId(1)
            .Build();

        var inactiveUser = UserBuilder.Default()
            .WithEmail("inactive@test.com")
            .IsInactive()
            .Build();

        var availableUser = UserBuilder.Default()
            .WithEmail("available@test.com")
            .Build();

        _dbContext.Users.AddRange(userWithAgent, inactiveUser, availableUser);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAvailableUsersAsync();

        // Assert
        result.Should().ContainSingle();
        result[0].Email.Should().Be("available@test.com");
    }

    #endregion

    #region CreateAgentAsync Tests

    [Fact]
    public async Task CreateAgentAsync_WithValidData_ReturnsSuccessAndGeneratesCode()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithFirstName("John")
            .WithLastName("Doe")
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var model = new AgentFormModel
        {
            UserId = user.Id,
            BranchId = _testBranch.Id,
            IsActive = true,
            Code = string.Empty // Code is generated, not provided
        };

        // Act
        var result = await _sut.CreateAgentAsync(model);

        // Assert
        result.Success.Should().BeTrue();
        result.Id.Should().BeGreaterThan(0);

        var agent = await _dbContext.Agents.FindAsync(result.Id);
        agent.Should().NotBeNull();
        agent!.Code.Should().Be("JODO"); // John Doe → JODO

        var updatedUser = await _dbContext.Users.FindAsync(user.Id);
        updatedUser!.AgentId.Should().Be(agent.Id);
        updatedUser.BranchId.Should().Be(_testBranch.Id);
    }

    [Theory]
    [InlineData("John", "Doe", "JODO")]
    [InlineData("Alice", "Smith", "ALSM")]
    [InlineData("A", "B", "AXBX")]
    [InlineData("X", "", "XXXX")]
    [InlineData("", "Y", "XXYX")]
    [InlineData("Michael", "O'Brien", "MIOB")]
    [InlineData("José", "García", "JOGA")]
    public async Task CreateAgentAsync_GeneratesCorrectCode_FromDifferentNames(
        string firstName, string lastName, string expectedCode)
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithFirstName(firstName)
            .WithLastName(lastName)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var model = new AgentFormModel
        {
            UserId = user.Id,
            BranchId = _testBranch.Id,
            IsActive = true,
            Code = string.Empty
        };

        // Act
        var result = await _sut.CreateAgentAsync(model);

        // Assert
        result.Success.Should().BeTrue();
        var agent = await _dbContext.Agents.FindAsync(result.Id);
        agent!.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task CreateAgentAsync_WithDuplicateBaseName_GeneratesNumericSuffix()
    {
        // Arrange
        var user1 = UserBuilder.Default()
            .WithFirstName("John")
            .WithLastName("Doe")
            .WithEmail("john1@test.com")
            .Build();
        _dbContext.Users.Add(user1);
        await _dbContext.SaveChangesAsync();

        // Create first agent with JODO code
        var agent1 = AgentBuilder.Default()
            .WithCode("JODO")
            .WithUserId(user1.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent1);
        await _dbContext.SaveChangesAsync();

        // Create second user with same name
        var user2 = UserBuilder.Default()
            .WithFirstName("John")
            .WithLastName("Doe")
            .WithEmail("john2@test.com")
            .Build();
        _dbContext.Users.Add(user2);
        await _dbContext.SaveChangesAsync();

        var model = new AgentFormModel
        {
            UserId = user2.Id,
            BranchId = _testBranch.Id,
            IsActive = true,
            Code = string.Empty
        };

        // Act
        var result = await _sut.CreateAgentAsync(model);

        // Assert
        result.Success.Should().BeTrue();
        var agent2 = await _dbContext.Agents.FindAsync(result.Id);
        agent2!.Code.Should().Be("JOD1"); // First 3 letters + numeric suffix
    }

    [Fact]
    public async Task CreateAgentAsync_WithoutUserId_ReturnsError()
    {
        // Arrange
        var model = new AgentFormModel
        {
            UserId = null,
            BranchId = _testBranch.Id,
            IsActive = true,
            Code = string.Empty
        };

        // Act
        var result = await _sut.CreateAgentAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("select a user");
    }

    [Fact]
    public async Task CreateAgentAsync_WithNonExistentUser_ReturnsError()
    {
        // Arrange
        var model = new AgentFormModel
        {
            UserId = "non-existent-user-id",
            BranchId = _testBranch.Id,
            IsActive = true,
            Code = string.Empty
        };

        // Act
        var result = await _sut.CreateAgentAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task CreateAgentAsync_WhenUserAlreadyHasAgent_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithAgentId(999)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var model = new AgentFormModel
        {
            UserId = user.Id,
            BranchId = _testBranch.Id,
            IsActive = true,
            Code = string.Empty
        };

        // Act
        var result = await _sut.CreateAgentAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already linked to an agent");
    }

    #endregion

    #region UpdateAgentAsync Tests

    [Fact]
    public async Task UpdateAgentAsync_WithValidData_UpdatesAgentAndUser()
    {
        // Arrange
        var user = UserBuilder.Default()
            .WithBranchId(1)
            .Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithCode("ORIG")
            .WithUserId(user.Id)
            .WithBranchId(1)
            .WithUser(user)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var newBranch = new Branch
        {
            Id = 2,
            Name = "New Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(newBranch);
        await _dbContext.SaveChangesAsync();

        var model = new AgentFormModel
        {
            Id = agent.Id,
            UserId = user.Id,
            Code = "NEWC",
            BranchId = newBranch.Id,
            IsActive = false
        };

        // Act
        var result = await _sut.UpdateAgentAsync(model);

        // Assert
        result.Success.Should().BeTrue();

        var updatedAgent = await _dbContext.Agents
            .Include(a => a.User)
            .FirstAsync(a => a.Id == agent.Id);
        updatedAgent.Code.Should().Be("NEWC");
        updatedAgent.BranchId.Should().Be(newBranch.Id);
        updatedAgent.IsActive.Should().BeFalse();
        updatedAgent.User!.BranchId.Should().Be(newBranch.Id); // User branch also updated
    }

    [Fact]
    public async Task UpdateAgentAsync_WithoutId_ReturnsError()
    {
        // Arrange
        var model = new AgentFormModel
        {
            Id = null,
            Code = "TEST",
            BranchId = _testBranch.Id,
            IsActive = true
        };

        // Act
        var result = await _sut.UpdateAgentAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ID is required");
    }

    [Fact]
    public async Task UpdateAgentAsync_WithNonExistentAgent_ReturnsError()
    {
        // Arrange
        var model = new AgentFormModel
        {
            Id = 999,
            Code = "TEST",
            BranchId = _testBranch.Id,
            IsActive = true
        };

        // Act
        var result = await _sut.UpdateAgentAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateAgentAsync_WithDuplicateCode_ReturnsError()
    {
        // Arrange
        var user1 = UserBuilder.Default().WithEmail("user1@test.com").Build();
        var user2 = UserBuilder.Default().WithEmail("user2@test.com").Build();
        _dbContext.Users.AddRange(user1, user2);
        await _dbContext.SaveChangesAsync();

        var agent1 = AgentBuilder.Default()
            .WithId(1)
            .WithCode("AAAA")
            .WithUserId(user1.Id)
            .WithBranchId(_testBranch.Id)
            .Build();

        var agent2 = AgentBuilder.Default()
            .WithId(2)
            .WithCode("BBBB")
            .WithUserId(user2.Id)
            .WithBranchId(_testBranch.Id)
            .Build();

        _dbContext.Agents.AddRange(agent1, agent2);
        await _dbContext.SaveChangesAsync();

        var model = new AgentFormModel
        {
            Id = agent2.Id,
            Code = "AAAA", // Try to use agent1's code
            BranchId = _testBranch.Id,
            IsActive = true
        };

        // Act
        var result = await _sut.UpdateAgentAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already exists");
    }

    #endregion

    #region ToggleAgentStatusAsync Tests

    [Fact]
    public async Task ToggleAgentStatusAsync_TogglesActiveStatus()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var originalStatus = agent.IsActive;

        // Act
        var result = await _sut.ToggleAgentStatusAsync(agent.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedAgent = await _dbContext.Agents.FindAsync(agent.Id);
        updatedAgent!.IsActive.Should().Be(!originalStatus);
    }

    [Fact]
    public async Task ToggleAgentStatusAsync_WithNonExistentAgent_ReturnsError()
    {
        // Act
        var result = await _sut.ToggleAgentStatusAsync(999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region AddWalletAsync Tests

    [Fact]
    public async Task AddWalletAsync_WithValidData_CreatesWalletWithInitialBalance()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var model = new WalletFormModel
        {
            AgentId = agent.Id,
            WalletTypeId = walletType.Id,
            Name = "My Cash Wallet",
            Currency = "UGX",
            InitialBalance = 5000m,
            IsActive = true
        };

        // Act
        var result = await _sut.AddWalletAsync(model);

        // Assert
        result.Success.Should().BeTrue();
        result.Id.Should().BeGreaterThan(0);

        var wallet = await _dbContext.Wallets.FindAsync(result.Id);
        wallet.Should().NotBeNull();
        wallet!.Name.Should().Be("My Cash Wallet");
        wallet.Balance.Should().Be(5000m);
        wallet.AgentId.Should().Be(agent.Id);
    }

    [Fact]
    public async Task AddWalletAsync_WithNonExistentAgent_ReturnsError()
    {
        // Arrange
        var model = new WalletFormModel
        {
            AgentId = 999,
            WalletTypeId = 1,
            Name = "Test Wallet",
            Currency = "UGX",
            InitialBalance = 0,
            IsActive = true
        };

        // Act
        var result = await _sut.AddWalletAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Agent not found");
    }

    [Fact]
    public async Task AddWalletAsync_WithNonExistentWalletType_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var model = new WalletFormModel
        {
            AgentId = agent.Id,
            WalletTypeId = 999,
            Name = "Test Wallet",
            Currency = "UGX",
            InitialBalance = 0,
            IsActive = true
        };

        // Act
        var result = await _sut.AddWalletAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Wallet type not found");
    }

    [Fact]
    public async Task AddWalletAsync_WithInactiveWalletType_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Inactive Type",
            Type = WalletTypeEnum.Custom,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var model = new WalletFormModel
        {
            AgentId = agent.Id,
            WalletTypeId = walletType.Id,
            Name = "Test Wallet",
            Currency = "UGX",
            InitialBalance = 0,
            IsActive = true
        };

        // Act
        var result = await _sut.AddWalletAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("inactive wallet type");
    }

    [Fact]
    public async Task AddWalletAsync_WhenAgentAlreadyHasWalletType_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        // Create first wallet
        var existingWallet = WalletBuilder.Default()
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .Build();
        _dbContext.Wallets.Add(existingWallet);
        await _dbContext.SaveChangesAsync();

        var model = new WalletFormModel
        {
            AgentId = agent.Id,
            WalletTypeId = walletType.Id, // Same type
            Name = "Another Cash Wallet",
            Currency = "UGX",
            InitialBalance = 0,
            IsActive = true
        };

        // Act
        var result = await _sut.AddWalletAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already has a wallet of type");
    }

    #endregion

    #region UpdateWalletAsync Tests

    [Fact]
    public async Task UpdateWalletAsync_WithValidData_UpdatesWallet()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var wallet = WalletBuilder.Default()
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithName("Original Name")
            .WithBalance(1000m)
            .Build();
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        var model = new WalletFormModel
        {
            Id = wallet.Id,
            AgentId = agent.Id,
            WalletTypeId = walletType.Id,
            Name = "Updated Name",
            Currency = "USD",
            InitialBalance = 0, // Should not update balance
            IsActive = false
        };

        // Act
        var result = await _sut.UpdateWalletAsync(model);

        // Assert
        result.Success.Should().BeTrue();

        var updatedWallet = await _dbContext.Wallets.FindAsync(wallet.Id);
        updatedWallet!.Name.Should().Be("Updated Name");
        updatedWallet.Currency.Should().Be("USD");
        updatedWallet.IsActive.Should().BeFalse();
        updatedWallet.Balance.Should().Be(1000m); // Balance unchanged
    }

    [Fact]
    public async Task UpdateWalletAsync_WithoutId_ReturnsError()
    {
        // Arrange
        var model = new WalletFormModel
        {
            Id = null,
            AgentId = 1,
            WalletTypeId = 1,
            Name = "Test",
            Currency = "UGX",
            InitialBalance = 0,
            IsActive = true
        };

        // Act
        var result = await _sut.UpdateWalletAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ID is required");
    }

    [Fact]
    public async Task UpdateWalletAsync_WithNonExistentWallet_ReturnsError()
    {
        // Arrange
        var model = new WalletFormModel
        {
            Id = 999,
            AgentId = 1,
            WalletTypeId = 1,
            Name = "Test",
            Currency = "UGX",
            InitialBalance = 0,
            IsActive = true
        };

        // Act
        var result = await _sut.UpdateWalletAsync(model);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region ToggleWalletStatusAsync Tests

    [Fact]
    public async Task ToggleWalletStatusAsync_TogglesActiveStatus()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var wallet = WalletBuilder.Default()
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .Build();
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        var originalStatus = wallet.IsActive;

        // Act
        var result = await _sut.ToggleWalletStatusAsync(wallet.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedWallet = await _dbContext.Wallets.FindAsync(wallet.Id);
        updatedWallet!.IsActive.Should().Be(!originalStatus);
    }

    [Fact]
    public async Task ToggleWalletStatusAsync_WithNonExistentWallet_ReturnsError()
    {
        // Act
        var result = await _sut.ToggleWalletStatusAsync(999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region DeleteWalletAsync Tests

    [Fact]
    public async Task DeleteWalletAsync_WithZeroBalanceAndNoHistory_SucceedsAndRemovesWallet()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var wallet = WalletBuilder.Default()
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithBalance(0m) // Zero balance
            .Build();
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeleteWalletAsync(wallet.Id);

        // Assert
        result.Success.Should().BeTrue();

        var deletedWallet = await _dbContext.Wallets.FindAsync(wallet.Id);
        deletedWallet.Should().BeNull();
    }

    [Fact]
    public async Task DeleteWalletAsync_WithNonZeroBalance_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);
        await _dbContext.SaveChangesAsync();

        var wallet = WalletBuilder.Default()
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithBalance(1000m) // Non-zero balance
            .Build();
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeleteWalletAsync(wallet.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("non-zero balance");
    }

    [Fact]
    public async Task DeleteWalletAsync_WithNonExistentWallet_ReturnsError()
    {
        // Act
        var result = await _sut.DeleteWalletAsync(999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region GetAgentWalletsAsync Tests

    [Fact]
    public async Task GetAgentWalletsAsync_ReturnsOrderedWalletsByTypeAndName()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var walletType1 = new WalletType
        {
            Id = 1,
            Name = "B Type",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var walletType2 = new WalletType
        {
            Id = 2,
            Name = "A Type",
            Type = WalletTypeEnum.MobileMoney,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WalletTypes.AddRange(walletType1, walletType2);
        await _dbContext.SaveChangesAsync();

        var wallet1 = WalletBuilder.Default()
            .WithId(1)
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType1.Id)
            .WithName("Z Wallet")
            .WithWalletType(walletType1)
            .Build();

        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(agent.Id)
            .WithWalletTypeId(walletType2.Id)
            .WithName("A Wallet")
            .WithWalletType(walletType2)
            .Build();

        _dbContext.Wallets.AddRange(wallet1, wallet2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAgentWalletsAsync(agent.Id);

        // Assert
        result.Should().HaveCount(2);
        result[0].WalletTypeName.Should().Be("A Type"); // Ordered by type name first
        result[1].WalletTypeName.Should().Be("B Type");
    }

    #endregion

    #region GetAvailableWalletTypesForAgentAsync Tests

    [Fact]
    public async Task GetAvailableWalletTypesForAgentAsync_ReturnsOnlyUnassignedActiveTypes()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var agent = AgentBuilder.Default()
            .WithUserId(user.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(agent);
        await _dbContext.SaveChangesAsync();

        var assignedType = new WalletType
        {
            Id = 1,
            Name = "Assigned Type",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var inactiveType = new WalletType
        {
            Id = 2,
            Name = "Inactive Type",
            Type = WalletTypeEnum.MobileMoney,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var availableType = new WalletType
        {
            Id = 3,
            Name = "Available Type",
            Type = WalletTypeEnum.Bank,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WalletTypes.AddRange(assignedType, inactiveType, availableType);
        await _dbContext.SaveChangesAsync();

        // Create wallet with assigned type
        var wallet = WalletBuilder.Default()
            .WithAgentId(agent.Id)
            .WithWalletTypeId(assignedType.Id)
            .Build();
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAvailableWalletTypesForAgentAsync(agent.Id);

        // Assert
        result.Should().ContainSingle();
        result[0].Name.Should().Be("Available Type");
    }

    #endregion

    #region GetBranchesAsync Tests

    [Fact]
    public async Task GetBranchesAsync_ReturnsOrderedBranches()
    {
        // Arrange
        var branch2 = new Branch
        {
            Id = 2,
            Name = "Z Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(branch2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetBranchesAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Test Branch"); // Ordered alphabetically
        result[1].Name.Should().Be("Z Branch");
    }

    #endregion

    #region GetWalletTypesAsync Tests

    [Fact]
    public async Task GetWalletTypesAsync_ReturnsOnlyActiveTypes()
    {
        // Arrange
        var activeType = new WalletType
        {
            Id = 1,
            Name = "Active Type",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var inactiveType = new WalletType
        {
            Id = 2,
            Name = "Inactive Type",
            Type = WalletTypeEnum.MobileMoney,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WalletTypes.AddRange(activeType, inactiveType);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetWalletTypesAsync();

        // Assert
        result.Should().ContainSingle();
        result[0].Name.Should().Be("Active Type");
    }

    #endregion
}
