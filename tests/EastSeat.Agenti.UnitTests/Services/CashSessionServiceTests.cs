using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashSessions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="CashSessionService"/>.
/// </summary>
public class CashSessionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CashSessionService _cashSessionService;

    public CashSessionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _cashSessionService = new CashSessionService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetCashSessionsAsync Tests

    [Fact]
    public async Task GetCashSessionsAsync_WithNoSessions_ReturnsEmptyList()
    {
        // Act
        var result = await _cashSessionService.GetCashSessionsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCashSessionsAsync_WithMultipleSessions_ReturnsOrderedByDateDescending()
    {
        // Arrange
        var user = UserBuilder.Default().WithFirstName("John").WithLastName("Doe").Build();
        var agent = AgentBuilder.Default().WithUser(user).WithCode("JODO").Build();

        var session1 = CashSessionBuilder.Default()
            .WithId(1)
            .WithAgent(agent)
            .WithSessionDate(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)))
            .Build();

        var session2 = CashSessionBuilder.Default()
            .WithId(2)
            .WithAgent(agent)
            .WithSessionDate(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)))
            .Build();

        var session3 = CashSessionBuilder.Default()
            .WithId(3)
            .WithAgent(agent)
            .WithSessionDate(DateOnly.FromDateTime(DateTime.UtcNow))
            .Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.AddRange(session1, session2, session3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionsAsync();

        // Assert
        result.Should().HaveCount(3);
        result[0].Id.Should().Be(3); // Most recent first
        result[1].Id.Should().Be(2);
        result[2].Id.Should().Be(1);
    }

    [Fact]
    public async Task GetCashSessionsAsync_WithOpeningAndClosingCounts_ReturnsCorrectTotals()
    {
        // Arrange
        var user = UserBuilder.Default().WithFirstName("Jane").WithLastName("Smith").Build();
        var agent = AgentBuilder.Default().WithUser(user).WithCode("JASM").Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).Build();

        var openingCount = CashCountBuilder.Default()
            .WithCashSession(session)
            .AsOpening()
            .WithTotalAmount(1000m)
            .Build();

        var closingCount = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSession(session)
            .AsClosing()
            .WithTotalAmount(1500m)
            .Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        _dbContext.CashCounts.AddRange(openingCount, closingCount);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].OpeningTotal.Should().Be(1000m);
        result[0].ClosingTotal.Should().Be(1500m);
    }

    [Fact]
    public async Task GetCashSessionsAsync_WithOnlyOpeningCount_ReturnsNullClosingTotal()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).AsOpen().Build();

        var openingCount = CashCountBuilder.Default()
            .WithCashSession(session)
            .AsOpening()
            .WithTotalAmount(500m)
            .Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        _dbContext.CashCounts.Add(openingCount);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].OpeningTotal.Should().Be(500m);
        result[0].ClosingTotal.Should().BeNull();
    }

    [Fact]
    public async Task GetCashSessionsAsync_IncludesSessionStatus()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var openSession = CashSessionBuilder.Default().WithId(1).WithAgent(agent).AsOpen().Build();
        var closedSession = CashSessionBuilder.Default().WithId(2).WithAgent(agent).AsClosed().Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.AddRange(openSession, closedSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionsAsync();

        // Assert
        result.Should().HaveCount(2);
        var open = result.FirstOrDefault(s => s.Id == 1);
        var closed = result.FirstOrDefault(s => s.Id == 2);

        open.Should().NotBeNull();
        open!.Status.Should().Be(CashSessionStatus.Open);
        open.ClosedAt.Should().BeNull();

        closed.Should().NotBeNull();
        closed!.Status.Should().Be(CashSessionStatus.Closed);
        closed.ClosedAt.Should().NotBeNull();
    }

    #endregion

    #region GetCashSessionDetailAsync Tests

    [Fact]
    public async Task GetCashSessionDetailAsync_WithNonExistentSession_ReturnsNull()
    {
        // Act
        var result = await _cashSessionService.GetCashSessionDetailAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCashSessionDetailAsync_WithValidSession_ReturnsDetailedInfo()
    {
        // Arrange
        var user = UserBuilder.Default().WithFirstName("Alice").WithLastName("Johnson").Build();
        var agent = AgentBuilder.Default().WithUser(user).WithCode("ALJO").Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).Build();

        var openingCount = CashCountBuilder.Default()
            .WithCashSession(session)
            .AsOpening()
            .WithTotalAmount(2000m)
            .WithSubmittedAt(DateTimeOffset.UtcNow)
            .Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        _dbContext.CashCounts.Add(openingCount);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionDetailAsync(session.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(session.Id);
        result.AgentName.Should().Be("Alice Johnson");
        result.AgentCode.Should().Be("ALJO");
        result.OpeningTotal.Should().Be(2000m);
        result.OpeningCount.Should().NotBeNull();
        result.OpeningCount!.TotalAmount.Should().Be(2000m);
    }

    [Fact]
    public async Task GetCashSessionDetailAsync_WithWalletDetails_ReturnsOrderedWalletEntries()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).Build();

        var walletType1 = new WalletType { Id = 1, Name = "Cash", Type = WalletTypeEnum.Cash };
        var walletType2 = new WalletType { Id = 2, Name = "Bank", Type = WalletTypeEnum.Bank };

        var wallet1 = WalletBuilder.Default()
            .WithId(1)
            .WithName("Cash USD")
            .WithWalletType(walletType1)
            .Build();

        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithName("Bank Account")
            .WithWalletType(walletType2)
            .Build();

        var openingCount = CashCountBuilder.Default()
            .WithCashSession(session)
            .AsOpening()
            .WithTotalAmount(3000m)
            .Build();

        var detail1 = CashCountDetailBuilder.Default()
            .WithCashCount(openingCount)
            .WithWallet(wallet1)
            .WithAmount(1000m)
            .Build();

        var detail2 = CashCountDetailBuilder.Default()
            .WithId(2)
            .WithCashCount(openingCount)
            .WithWallet(wallet2)
            .WithAmount(2000m)
            .Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.WalletTypes.AddRange(walletType1, walletType2);
        _dbContext.Wallets.AddRange(wallet1, wallet2);
        _dbContext.CashSessions.Add(session);
        _dbContext.CashCounts.Add(openingCount);
        _dbContext.CashCountDetails.AddRange(detail1, detail2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionDetailAsync(session.Id);

        // Assert
        result.Should().NotBeNull();
        result!.OpeningCount.Should().NotBeNull();
        result.OpeningCount!.WalletEntries.Should().HaveCount(2);

        // Should be ordered by WalletTypeName then WalletName
        result.OpeningCount.WalletEntries[0].WalletTypeName.Should().Be("Bank");
        result.OpeningCount.WalletEntries[0].Amount.Should().Be(2000m);
        result.OpeningCount.WalletEntries[1].WalletTypeName.Should().Be("Cash");
        result.OpeningCount.WalletEntries[1].Amount.Should().Be(1000m);
    }

    [Fact]
    public async Task GetCashSessionDetailAsync_WithBothCounts_ReturnsBothDetails()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).Build();

        var openingCount = CashCountBuilder.Default()
            .WithCashSession(session)
            .AsOpening()
            .WithTotalAmount(1000m)
            .Build();

        var closingCount = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSession(session)
            .AsClosing()
            .WithTotalAmount(1200m)
            .Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        _dbContext.CashCounts.AddRange(openingCount, closingCount);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionDetailAsync(session.Id);

        // Assert
        result.Should().NotBeNull();
        result!.OpeningCount.Should().NotBeNull();
        result.ClosingCount.Should().NotBeNull();
        result.OpeningTotal.Should().Be(1000m);
        result.ClosingTotal.Should().Be(1200m);
    }

    [Fact]
    public async Task GetCashSessionDetailAsync_WithNoCounts_ReturnsZeroTotals()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.GetCashSessionDetailAsync(session.Id);

        // Assert
        result.Should().NotBeNull();
        result!.OpeningTotal.Should().Be(0m);
        result.ClosingTotal.Should().BeNull();
        result.OpeningCount.Should().BeNull();
        result.ClosingCount.Should().BeNull();
    }

    #endregion

    #region CloseSessionAsync Tests

    [Fact]
    public async Task CloseSessionAsync_WithNonExistentSession_ReturnsError()
    {
        // Act
        var result = await _cashSessionService.CloseSessionAsync(999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Cash session not found.");
    }

    [Fact]
    public async Task CloseSessionAsync_WithOpenSession_ClosesSuccessfully()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).AsOpen().Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var sessionId = session.Id;

        // Act
        var result = await _cashSessionService.CloseSessionAsync(sessionId);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        // Verify session was updated
        var updatedSession = await _dbContext.CashSessions.FindAsync(sessionId);
        updatedSession.Should().NotBeNull();
        updatedSession!.Status.Should().Be(CashSessionStatus.Closed);
        updatedSession.ClosedAt.Should().NotBeNull();
        updatedSession.ClosedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CloseSessionAsync_WithAlreadyClosedSession_ReturnsError()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var session = CashSessionBuilder.Default().WithAgent(agent).AsClosed().Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cashSessionService.CloseSessionAsync(session.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Session is already closed.");
    }

    [Fact]
    public async Task CloseSessionAsync_UpdatesStatusAndClosedAt()
    {
        // Arrange
        var user = UserBuilder.Default().Build();
        var agent = AgentBuilder.Default().WithUser(user).Build();
        var session = CashSessionBuilder.Default()
            .WithAgent(agent)
            .WithStatus(CashSessionStatus.Open)
            .WithClosedAt(null)
            .Build();

        _dbContext.Users.Add(user);
        _dbContext.Agents.Add(agent);
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var beforeClose = DateTimeOffset.UtcNow;
        var sessionId = session.Id;

        // Act
        var result = await _cashSessionService.CloseSessionAsync(sessionId);

        // Assert
        result.Success.Should().BeTrue();

        var updatedSession = await _dbContext.CashSessions.FindAsync(sessionId);
        updatedSession.Should().NotBeNull();
        updatedSession!.Status.Should().Be(CashSessionStatus.Closed);
        updatedSession.ClosedAt.Should().NotBeNull();
        updatedSession.ClosedAt.Should().BeOnOrAfter(beforeClose);
    }

    #endregion
}
