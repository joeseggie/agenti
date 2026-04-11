using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashSessions;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EastSeat.Agenti.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="CashSessionService"/> (branch-level sessions).
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
        var walletAdjustmentService = new WalletAdjustmentService(
            _dbContext, new Moq.Mock<EastSeat.Agenti.Web.Features.Notifications.INotificationService>().Object);
        _cashSessionService = new CashSessionService(_dbContext, walletAdjustmentService);
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
        var result = await _cashSessionService.GetCashSessionsAsync();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCashSessionsAsync_WithMultipleSessions_ReturnsOrderedByDateDescending()
    {
        var session1 = CashSessionBuilder.Default()
            .WithId(1)
            .WithBranchId(1)
            .WithSessionDate(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)))
            .Build();

        var session2 = CashSessionBuilder.Default()
            .WithId(2)
            .WithBranchId(1)
            .WithSessionDate(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)))
            .Build();

        var session3 = CashSessionBuilder.Default()
            .WithId(3)
            .WithBranchId(1)
            .WithSessionDate(DateOnly.FromDateTime(DateTime.UtcNow))
            .Build();

        _dbContext.CashSessions.AddRange(session1, session2, session3);
        await _dbContext.SaveChangesAsync();

        var result = await _cashSessionService.GetCashSessionsAsync();

        result.Should().HaveCount(3);
        result[0].Id.Should().Be(3);
        result[1].Id.Should().Be(2);
        result[2].Id.Should().Be(1);
    }

    [Fact]
    public async Task GetCashSessionsAsync_ReturnsCorrectAgentCount()
    {
        var session = CashSessionBuilder.Default()
            .WithBranchId(1)
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Two agents with opening counts
        var user1 = UserBuilder.Default().WithFirstName("Alice").Build();
        var user2 = UserBuilder.Default().WithFirstName("Bob").WithEmail("bob@test.com").Build();
        _dbContext.Users.AddRange(user1, user2);

        var agent1 = AgentBuilder.Default().WithId(1).WithUser(user1).WithCode("A001").Build();
        var agent2 = AgentBuilder.Default().WithId(2).WithUser(user2).WithCode("A002").Build();
        _dbContext.Agents.AddRange(agent1, agent2);

        var count1 = CashCountBuilder.Default()
            .WithId(1)
            .WithCashSessionId(session.Id)
            .WithAgentId(agent1.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        var count2 = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(agent2.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(2000m)
            .Build();
        _dbContext.CashCounts.AddRange(count1, count2);
        await _dbContext.SaveChangesAsync();

        var result = await _cashSessionService.GetCashSessionsAsync();

        result.Should().ContainSingle();
        result[0].AgentCount.Should().Be(2);
        result[0].TotalOpeningAmount.Should().Be(3000m);
    }

    #endregion

    #region CloseSessionAsync Tests

    [Fact]
    public async Task CloseSessionAsync_WithNonExistentSession_ReturnsError()
    {
        var result = await _cashSessionService.CloseSessionAsync(999);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Cash session not found.");
    }

    [Fact]
    public async Task CloseSessionAsync_WithAlreadyClosedSession_ReturnsError()
    {
        var session = CashSessionBuilder.Default().WithBranchId(1).AsClosed().Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var result = await _cashSessionService.CloseSessionAsync(session.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Session is already closed.");
    }

    [Fact]
    public async Task CloseSessionAsync_WithMissingClosingCounts_ReturnsError()
    {
        // Rules 5, 8, 9: All agents need approved closing counts
        var session = CashSessionBuilder.Default().WithBranchId(1).AsOpen().Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var openingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(1)
            .AsOpening()
            .AsApproved()
            .Build();
        _dbContext.CashCounts.Add(openingCount);
        await _dbContext.SaveChangesAsync();

        var result = await _cashSessionService.CloseSessionAsync(session.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("agent(s) still need approved closing counts");
    }

    [Fact]
    public async Task CloseSessionAsync_WithAllClosingCountsApproved_ClosesSuccessfully()
    {
        var session = CashSessionBuilder.Default().WithBranchId(1).AsOpen().Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var opening = CashCountBuilder.Default()
            .WithId(1)
            .WithCashSessionId(session.Id)
            .WithAgentId(1)
            .AsOpening()
            .AsApproved()
            .Build();
        var closing = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(1)
            .AsClosing()
            .AsApproved()
            .Build();
        _dbContext.CashCounts.AddRange(opening, closing);
        await _dbContext.SaveChangesAsync();

        var result = await _cashSessionService.CloseSessionAsync(session.Id);

        result.Success.Should().BeTrue();

        var updated = await _dbContext.CashSessions.FindAsync(session.Id);
        updated!.Status.Should().Be(CashSessionStatus.Closed);
        updated.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CloseSessionAsync_WithPendingApprovals_ReturnsError()
    {
        var session = CashSessionBuilder.Default().WithBranchId(1).AsOpen().Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var pendingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(1)
            .AsOpening()
            .AsSubmitted()
            .Build();
        _dbContext.CashCounts.Add(pendingCount);
        await _dbContext.SaveChangesAsync();

        var result = await _cashSessionService.CloseSessionAsync(session.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pending approval");
    }

    #endregion

    #region GetCashSessionDetailAsync Tests

    [Fact]
    public async Task GetCashSessionDetailAsync_WithNonExistentSession_ReturnsNull()
    {
        var result = await _cashSessionService.GetCashSessionDetailAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCashSessionDetailAsync_GroupsCountsByAgent()
    {
        var user1 = UserBuilder.Default().WithFirstName("Alice").WithLastName("A").Build();
        var user2 = UserBuilder.Default().WithFirstName("Bob").WithLastName("B").WithEmail("bob@test.com").Build();
        _dbContext.Users.AddRange(user1, user2);

        var agent1 = AgentBuilder.Default().WithId(1).WithUser(user1).WithCode("A001").Build();
        var agent2 = AgentBuilder.Default().WithId(2).WithUser(user2).WithCode("B002").Build();
        _dbContext.Agents.AddRange(agent1, agent2);

        var session = CashSessionBuilder.Default().WithBranchId(1).AsOpen().Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var count1 = CashCountBuilder.Default()
            .WithId(1)
            .WithCashSessionId(session.Id)
            .WithAgent(agent1)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        var count2 = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgent(agent2)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(2000m)
            .Build();
        _dbContext.CashCounts.AddRange(count1, count2);
        await _dbContext.SaveChangesAsync();

        var result = await _cashSessionService.GetCashSessionDetailAsync(session.Id);

        result.Should().NotBeNull();
        result!.AgentSummaries.Should().HaveCount(2);
        result.TotalOpeningAmount.Should().Be(3000m);
    }

    #endregion
}
