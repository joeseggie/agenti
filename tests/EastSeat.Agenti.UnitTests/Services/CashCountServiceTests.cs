using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.Vaults;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "CashCount")]
public class CashCountServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IVaultService> _vaultServiceMock;
    private readonly CashCountService _sut;
    private readonly Branch _testBranch;
    private readonly ApplicationUser _testUser;
    private readonly Agent _testAgent;
    private readonly WalletType _cashWalletType;
    private readonly Wallet _testWallet;

    public CashCountServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Setup mock vault service
        _vaultServiceMock = new Mock<IVaultService>();

        // Seed required data
        _testBranch = new Branch
        {
            Id = 1,
            Name = "Test Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(_testBranch);

        _testUser = UserBuilder.Default()
            .WithEmail("agent@test.com")
            .WithRole(UserRole.Agent)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(_testUser);

        _testAgent = AgentBuilder.Default()
            .WithUserId(_testUser.Id)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Agents.Add(_testAgent);

        _testUser.AgentId = _testAgent.Id;

        _cashWalletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            SupportsDenominations = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(_cashWalletType);

        _testWallet = WalletBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithWalletTypeId(_cashWalletType.Id)
            .WithBalance(0m)
            .Build();
        _dbContext.Wallets.Add(_testWallet);

        _dbContext.SaveChanges();

        _sut = new CashCountService(_dbContext, _vaultServiceMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetCurrentSessionAsync Tests

    [Fact]
    public async Task GetCurrentSessionAsync_WithNoOpenSession_ReturnsCanPerformOpeningCount()
    {
        // Act
        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        // Assert
        result.Should().NotBeNull();
        result.StatusText.Should().Be("No open session");
        result.StatusColor.Should().Be("info");
        result.CanPerformOpeningCount.Should().BeTrue();
        result.CanPerformClosingCount.Should().BeFalse();
        result.SessionId.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithOpenSessionAndNoOpeningCount_ReturnsSessionOpen()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        // Assert
        result.Should().NotBeNull();
        result.StatusText.Should().Be("Session Open");
        result.StatusColor.Should().Be("success");
        result.SessionId.Should().Be(session.Id);
        result.CanPerformOpeningCount.Should().BeFalse(); // Session already open
        result.CanPerformClosingCount.Should().BeFalse(); // No opening count yet
        result.HasOpeningCount.Should().BeFalse();
        result.HasClosingCount.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithOpeningCountSubmitted_ReturnsCanPerformClosingCount()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var openingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsOpening()
            .AsSubmitted()
            .Build();
        _dbContext.CashCounts.Add(openingCount);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        // Assert
        result.Should().NotBeNull();
        result.CanPerformOpeningCount.Should().BeFalse();
        result.CanPerformClosingCount.Should().BeTrue();
        result.HasOpeningCount.Should().BeTrue();
        result.HasClosingCount.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithUserNotAgent_ReturnsError()
    {
        // Arrange
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetCurrentSessionAsync(nonAgentUser.Id);

        // Assert
        result.Should().NotBeNull();
        result.StatusText.Should().Be("User not configured as an agent");
        result.StatusColor.Should().Be("error");
        result.CanPerformOpeningCount.Should().BeFalse();
        result.CanPerformClosingCount.Should().BeFalse();
    }

    #endregion

    #region InitializeCashCountFormAsync Tests

    [Fact]
    public async Task InitializeCashCountFormAsync_ReturnsFormWithActiveWallets()
    {
        // Arrange
        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(_testAgent.Id)
            .WithWalletTypeId(_cashWalletType.Id)
            .WithName("Wallet 2")
            .WithBalance(500m)
            .Build();
        _dbContext.Wallets.Add(wallet2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.InitializeCashCountFormAsync(_testUser.Id, isOpening: true);

        // Assert
        result.Should().NotBeNull();
        result.IsOpening.Should().BeTrue();
        result.WalletEntries.Should().HaveCount(2);
        result.WalletEntries[0].ExpectedBalance.Should().Be(0m);
        result.WalletEntries[1].ExpectedBalance.Should().Be(500m);
        result.WalletEntries.All(w => w.CountedAmount == 0).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeCashCountFormAsync_ExcludesInactiveWallets()
    {
        // Arrange
        var inactiveWallet = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(_testAgent.Id)
            .WithWalletTypeId(_cashWalletType.Id)
            .IsInactive()
            .Build();
        _dbContext.Wallets.Add(inactiveWallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.InitializeCashCountFormAsync(_testUser.Id, isOpening: true);

        // Assert
        result.WalletEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task InitializeCashCountFormAsync_WithUserNotAgent_ReturnsEmptyForm()
    {
        // Arrange
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.InitializeCashCountFormAsync(nonAgentUser.Id, isOpening: true);

        // Assert
        result.WalletEntries.Should().BeEmpty();
    }

    #endregion

    #region SaveCashCountAsync Tests

    [Fact]
    public async Task SaveCashCountAsync_ForOpeningCount_CreatesNewSessionAndCount()
    {
        // Arrange
        var form = new CashCountFormModel
        {
            IsOpening = true,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new()
                {
                    WalletId = _testWallet.Id,
                    CountedAmount = 1000m
                }
            }
        };

        // Act
        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        // Assert
        result.Success.Should().BeTrue();
        result.CashCountId.Should().BeGreaterThan(0);
        result.CashSessionId.Should().BeGreaterThan(0);

        var session = await _dbContext.CashSessions.FindAsync(result.CashSessionId);
        session.Should().NotBeNull();
        session!.Status.Should().Be(CashSessionStatus.Open);

        var count = await _dbContext.CashCounts
            .Include(c => c.Details)
            .FirstAsync(c => c.Id == result.CashCountId);
        count.IsOpening.Should().BeTrue();
        count.TotalAmount.Should().Be(1000m);
        count.Details.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveCashCountAsync_ForClosingCount_RequiresOpenSession()
    {
        // Arrange
        var form = new CashCountFormModel
        {
            IsOpening = false,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new()
                {
                    WalletId = _testWallet.Id,
                    CountedAmount = 500m
                }
            }
        };

        // Act
        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No open session found");
    }

    [Fact]
    public async Task SaveCashCountAsync_WithExistingOpenSession_ForOpeningCount_ReturnsError()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = true,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new()
                {
                    WalletId = _testWallet.Id,
                    CountedAmount = 1000m
                }
            }
        };

        // Act
        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("open session already exists");
    }

    [Fact]
    public async Task SaveCashCountAsync_WithUserNotAgent_ReturnsError()
    {
        // Arrange
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = true,
            WalletEntries = new List<WalletCountEntryDto>()
        };

        // Act
        var result = await _sut.SaveCashCountAsync(nonAgentUser.Id, form);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured as an agent");
    }

    [Fact]
    public async Task SaveCashCountAsync_UpdatesExistingDraftCount()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var existingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsClosing()
            .WithTotalAmount(500m)
            .Build();
        _dbContext.CashCounts.Add(existingCount);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = false,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new()
                {
                    WalletId = _testWallet.Id,
                    CountedAmount = 800m
                }
            }
        };

        // Act
        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        // Assert
        result.Success.Should().BeTrue();

        var updatedCount = await _dbContext.CashCounts.FindAsync(existingCount.Id);
        updatedCount!.TotalAmount.Should().Be(800m);
    }

    [Fact]
    public async Task SaveCashCountAsync_WithSubmittedCount_ReturnsError()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var submittedCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsClosing()
            .AsSubmitted()
            .Build();
        _dbContext.CashCounts.Add(submittedCount);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = false,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new()
                {
                    WalletId = _testWallet.Id,
                    CountedAmount = 800m
                }
            }
        };

        // Act
        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already been submitted");
    }

    #endregion

    #region SubmitCashCountAsync Tests

    [Fact]
    public async Task SubmitCashCountAsync_ForOpeningCount_WithdrawsFromVaultAndUpdatesWallets()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsOpening()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(cashCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(1000m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.WithdrawForSessionAsync(
                session.Id,
                _testBranch.Id,
                1000m,
                _testUser.Id,
                true,
                default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        // Act
        var result = await _sut.SubmitCashCountAsync(_testUser.Id, cashCount.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedCount = await _dbContext.CashCounts.FindAsync(cashCount.Id);
        updatedCount!.SubmittedAt.Should().NotBeNull();
        updatedCount.ApprovedAt.Should().NotBeNull();

        var updatedWallet = await _dbContext.Wallets.FindAsync(_testWallet.Id);
        updatedWallet!.Balance.Should().Be(1000m);

        _vaultServiceMock.Verify(x => x.WithdrawForSessionAsync(
            session.Id,
            _testBranch.Id,
            1000m,
            _testUser.Id,
            true,
            default), Times.Once);
    }

    [Fact]
    public async Task SubmitCashCountAsync_ForClosingCount_DepositsToVaultAndZeroesWallets()
    {
        // Arrange
        _testWallet.Balance = 800m; // Set initial balance
        await _dbContext.SaveChangesAsync();

        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsClosing()
            .WithTotalAmount(800m)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(cashCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(800m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.DepositForSessionAsync(
                session.Id,
                _testBranch.Id,
                800m,
                _testUser.Id,
                true,
                default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        // Act
        var result = await _sut.SubmitCashCountAsync(_testUser.Id, cashCount.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedCount = await _dbContext.CashCounts.FindAsync(cashCount.Id);
        updatedCount!.SubmittedAt.Should().NotBeNull();

        var updatedWallet = await _dbContext.Wallets.FindAsync(_testWallet.Id);
        updatedWallet!.Balance.Should().Be(0m);

        var updatedSession = await _dbContext.CashSessions.FindAsync(session.Id);
        updatedSession!.Status.Should().Be(CashSessionStatus.Closed);
        updatedSession.ClosedAt.Should().NotBeNull();

        _vaultServiceMock.Verify(x => x.DepositForSessionAsync(
            session.Id,
            _testBranch.Id,
            800m,
            _testUser.Id,
            true,
            default), Times.Once);
    }

    [Fact]
    public async Task SubmitCashCountAsync_WithVaultWithdrawalFailure_ReturnsError()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsOpening()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.WithdrawForSessionAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                default))
            .ReturnsAsync(VaultOperationResult.Error("Insufficient vault balance"));

        // Act
        var result = await _sut.SubmitCashCountAsync(_testUser.Id, cashCount.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Vault withdrawal failed");
        result.ErrorMessage.Should().Contain("Insufficient vault balance");
    }

    [Fact]
    public async Task SubmitCashCountAsync_WithAlreadySubmittedCount_ReturnsError()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsOpening()
            .AsSubmitted()
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.SubmitCashCountAsync(_testUser.Id, cashCount.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already been submitted");
    }

    [Fact]
    public async Task SubmitCashCountAsync_WithNonExistentCount_ReturnsError()
    {
        // Act
        var result = await _sut.SubmitCashCountAsync(_testUser.Id, 999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task SubmitCashCountAsync_WithUserNotAgent_ReturnsError()
    {
        // Arrange
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.SubmitCashCountAsync(nonAgentUser.Id, 1);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured as an agent");
    }

    #endregion

    #region GetCashCountFormAsync Tests

    [Fact]
    public async Task GetCashCountFormAsync_WithExistingCount_ReturnsFormWithDetails()
    {
        // Arrange
        var session = CashSessionBuilder.Default()
            .WithAgentId(_testAgent.Id)
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .AsOpening()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(cashCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(1000m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetCashCountFormAsync(_testUser.Id, cashCount.Id);

        // Assert
        result.Should().NotBeNull();
        result!.CashCountId.Should().Be(cashCount.Id);
        result.CashSessionId.Should().Be(session.Id);
        result.IsOpening.Should().BeTrue();
        result.WalletEntries.Should().ContainSingle();
        result.WalletEntries[0].CountedAmount.Should().Be(1000m);
    }

    [Fact]
    public async Task GetCashCountFormAsync_WithNonExistentCount_ReturnsNull()
    {
        // Act
        var result = await _sut.GetCashCountFormAsync(_testUser.Id, 999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCashCountFormAsync_WithUserNotAgent_ReturnsNull()
    {
        // Arrange
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetCashCountFormAsync(nonAgentUser.Id, 1);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
