using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Dashboard;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="DashboardService"/>.
/// </summary>
public class DashboardServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DashboardService _dashboardService;

    public DashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _dashboardService = new DashboardService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetDashboardAsync Tests

    [Fact]
    public async Task GetDashboardAsync_WithNoWalletsAndNoSession_ReturnsEmptyDashboard()
    {
        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.Should().NotBeNull();
        result.Wallets.Should().BeEmpty();
        result.HasWallets.Should().BeFalse();
        result.TotalBalance.Should().Be(0);
        result.Currency.Should().Be("UGX");
        result.SessionStatus.HasActiveSession.Should().BeFalse();
        result.SessionStatus.SessionId.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardAsync_WithActiveWallets_ReturnsWalletBalances()
    {
        // Arrange
        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            SupportsDenominations = true
        };

        var wallet1 = WalletBuilder.Default()
            .WithId(1)
            .WithName("Cash USD")
            .WithWalletType(walletType)
            .WithBalance(1000m)
            .WithCurrency("USD")
            .Build();

        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithName("Cash UGX")
            .WithWalletType(walletType)
            .WithBalance(500000m)
            .WithCurrency("UGX")
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.AddRange(wallet1, wallet2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.Should().NotBeNull();
        result.Wallets.Should().HaveCount(2);
        result.HasWallets.Should().BeTrue();
        result.TotalBalance.Should().Be(501000m);

        // Wallets are ordered by type then name, so "Cash UGX" comes before "Cash USD"
        result.Currency.Should().Be("UGX"); // First wallet's currency

        var firstWallet = result.Wallets[0];
        firstWallet.WalletName.Should().Be("Cash UGX");
        firstWallet.Balance.Should().Be(500000m);
        firstWallet.Currency.Should().Be("UGX");
        firstWallet.WalletTypeIcon.Should().Be("💵");
        firstWallet.SupportsDenominations.Should().BeTrue();
    }

    [Fact]
    public async Task GetDashboardAsync_WithInactiveWallets_ExcludesInactiveWallets()
    {
        // Arrange
        var walletType = new WalletType { Id = 1, Name = "Cash", Type = WalletTypeEnum.Cash };

        var activeWallet = WalletBuilder.Default()
            .WithId(1)
            .WithName("Active Wallet")
            .WithWalletType(walletType)
            .WithBalance(1000m)
            .Build();

        var inactiveWallet = WalletBuilder.Default()
            .WithId(2)
            .WithName("Inactive Wallet")
            .WithWalletType(walletType)
            .WithBalance(5000m)
            .IsInactive()
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.AddRange(activeWallet, inactiveWallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.Wallets.Should().HaveCount(1);
        result.Wallets[0].WalletName.Should().Be("Active Wallet");
        result.TotalBalance.Should().Be(1000m); // Only active wallet
    }

    [Fact]
    public async Task GetDashboardAsync_WithMultipleWalletTypes_OrdersByTypeAndName()
    {
        // Arrange
        var cashType = new WalletType { Id = 1, Name = "Cash", Type = WalletTypeEnum.Cash };
        var bankType = new WalletType { Id = 2, Name = "Bank", Type = WalletTypeEnum.Bank };
        var mobileMoneyType = new WalletType { Id = 3, Name = "Mobile Money", Type = WalletTypeEnum.MobileMoney };

        var bankWallet = WalletBuilder.Default()
            .WithId(1)
            .WithName("Bank Account")
            .WithWalletType(bankType)
            .Build();

        var cashWallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithName("Cash USD")
            .WithWalletType(cashType)
            .Build();

        var cashWallet1 = WalletBuilder.Default()
            .WithId(3)
            .WithName("Cash EUR")
            .WithWalletType(cashType)
            .Build();

        var mobileMoneyWallet = WalletBuilder.Default()
            .WithId(4)
            .WithName("MTN Mobile Money")
            .WithWalletType(mobileMoneyType)
            .Build();

        _dbContext.WalletTypes.AddRange(cashType, bankType, mobileMoneyType);
        _dbContext.Wallets.AddRange(bankWallet, cashWallet2, cashWallet1, mobileMoneyWallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.Wallets.Should().HaveCount(4);

        // Should be ordered by WalletType enum value (Cash=0, MobileMoney=1, Bank=2), then by name
        result.Wallets[0].WalletName.Should().Be("Cash EUR");
        result.Wallets[0].WalletTypeIcon.Should().Be("💵");

        result.Wallets[1].WalletName.Should().Be("Cash USD");
        result.Wallets[1].WalletTypeIcon.Should().Be("💵");

        result.Wallets[2].WalletName.Should().Be("MTN Mobile Money");
        result.Wallets[2].WalletTypeIcon.Should().Be("📱");

        result.Wallets[3].WalletName.Should().Be("Bank Account");
        result.Wallets[3].WalletTypeIcon.Should().Be("🏦");
    }

    [Fact]
    public async Task GetDashboardAsync_WithOpenSessionToday_ReturnsActiveSessionStatus()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var session = CashSessionBuilder.Default()
            .WithBranchId(1)
            .WithSessionDate(today)
            .AsOpen()
            .Build();

        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.SessionStatus.Should().NotBeNull();
        result.SessionStatus.HasActiveSession.Should().BeTrue();
        result.SessionStatus.SessionId.Should().Be(session.Id);
        result.SessionStatus.SessionDate.Should().Be(today);
        result.SessionStatus.Status.Should().Be(CashSessionStatus.Open);
        result.SessionStatus.OpenedAt.Should().NotBeNull();
        result.SessionStatus.StatusDisplay.Should().Be("Open");
        result.SessionStatus.StatusColor.Should().Be("success");
    }

    [Fact]
    public async Task GetDashboardAsync_WithClosedSessionToday_ReturnsInactiveSessionStatus()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var session = CashSessionBuilder.Default()
            .WithBranchId(1)
            .WithSessionDate(today)
            .AsClosed()
            .Build();

        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.SessionStatus.Should().NotBeNull();
        result.SessionStatus.HasActiveSession.Should().BeFalse();
        result.SessionStatus.SessionId.Should().Be(session.Id);
        result.SessionStatus.Status.Should().Be(CashSessionStatus.Closed);
        result.SessionStatus.StatusDisplay.Should().Be("Closed");
        result.SessionStatus.StatusColor.Should().Be("default");
    }

    [Fact]
    public async Task GetDashboardAsync_WithSessionYesterday_ReturnsNoActiveSession()
    {
        // Arrange
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var session = CashSessionBuilder.Default()
            .WithBranchId(1)
            .WithSessionDate(yesterday)
            .AsOpen()
            .Build();

        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.SessionStatus.Should().NotBeNull();
        result.SessionStatus.HasActiveSession.Should().BeFalse();
        result.SessionStatus.SessionId.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardAsync_WithMultipleSessionsToday_ReturnsMostRecentSession()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var earlierSession = CashSessionBuilder.Default()
            .WithId(1)
            .WithBranchId(1)
            .WithSessionDate(today)
            .WithOpenedAt(DateTimeOffset.UtcNow.AddHours(-5))
            .AsOpen()
            .Build();

        var laterSession = CashSessionBuilder.Default()
            .WithId(2)
            .WithBranchId(1)
            .WithSessionDate(today)
            .WithOpenedAt(DateTimeOffset.UtcNow.AddHours(-2))
            .AsOpen()
            .Build();

        _dbContext.CashSessions.AddRange(earlierSession, laterSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.SessionStatus.SessionId.Should().Be(laterSession.Id);
        result.SessionStatus.OpenedAt.Should().BeCloseTo(laterSession.OpenedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetDashboardAsync_WithWalletsAndSession_ReturnsBothDetails()
    {
        // Arrange
        var walletType = new WalletType { Id = 1, Name = "Cash", Type = WalletTypeEnum.Cash };
        var wallet = WalletBuilder.Default()
            .WithWalletType(walletType)
            .WithBalance(2500m)
            .Build();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var session = CashSessionBuilder.Default()
            .WithBranchId(1)
            .WithSessionDate(today)
            .AsOpen()
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.Add(wallet);
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.Wallets.Should().HaveCount(1);
        result.TotalBalance.Should().Be(2500m);
        result.HasWallets.Should().BeTrue();
        result.SessionStatus.HasActiveSession.Should().BeTrue();
        result.SessionStatus.SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetDashboardAsync_WithCustomWalletType_ReturnsCustomIcon()
    {
        // Arrange
        var customType = new WalletType { Id = 1, Name = "Custom", Type = WalletTypeEnum.Custom };
        var wallet = WalletBuilder.Default()
            .WithWalletType(customType)
            .Build();

        _dbContext.WalletTypes.Add(customType);
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.Wallets.Should().HaveCount(1);
        result.Wallets[0].WalletTypeIcon.Should().Be("💼");
    }

    [Fact]
    public async Task GetDashboardAsync_SessionStatusDisplay_ReturnsCorrectDisplay()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var blockedSession = CashSessionBuilder.Default()
            .WithBranchId(1)
            .WithSessionDate(today)
            .WithStatus(CashSessionStatus.Blocked)
            .Build();

        _dbContext.CashSessions.Add(blockedSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.SessionStatus.Status.Should().Be(CashSessionStatus.Blocked);
        result.SessionStatus.StatusDisplay.Should().Be("Blocked");
        result.SessionStatus.StatusColor.Should().Be("error");
        result.SessionStatus.HasActiveSession.Should().BeFalse(); // Blocked is not open
    }

    [Fact]
    public async Task GetDashboardAsync_WithZeroBalanceWallets_IncludesWallets()
    {
        // Arrange
        var walletType = new WalletType { Id = 1, Name = "Cash", Type = WalletTypeEnum.Cash };
        var wallet = WalletBuilder.Default()
            .WithWalletType(walletType)
            .WithBalance(0m)
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.Wallets.Should().HaveCount(1);
        result.Wallets[0].Balance.Should().Be(0m);
        result.TotalBalance.Should().Be(0m);
        result.HasWallets.Should().BeTrue();
    }

    [Fact]
    public async Task GetDashboardAsync_TotalBalance_SumsAllWalletBalances()
    {
        // Arrange
        var walletType = new WalletType { Id = 1, Name = "Cash", Type = WalletTypeEnum.Cash };

        var wallet1 = WalletBuilder.Default()
            .WithId(1)
            .WithWalletType(walletType)
            .WithBalance(100.50m)
            .Build();

        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithWalletType(walletType)
            .WithBalance(250.75m)
            .Build();

        var wallet3 = WalletBuilder.Default()
            .WithId(3)
            .WithWalletType(walletType)
            .WithBalance(49.25m)
            .Build();

        _dbContext.WalletTypes.Add(walletType);
        _dbContext.Wallets.AddRange(wallet1, wallet2, wallet3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _dashboardService.GetDashboardAsync("user-123");

        // Assert
        result.TotalBalance.Should().Be(400.50m);
    }

    #endregion
}
