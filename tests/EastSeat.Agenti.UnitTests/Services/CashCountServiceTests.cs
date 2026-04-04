using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.Notifications;
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
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly CashCountService _sut;
    private readonly Branch _testBranch;
    private readonly ApplicationUser _testUser;
    private readonly Agent _testAgent;
    private readonly WalletType _cashWalletType;
    private readonly Wallet _testWallet;

    public CashCountServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        _vaultServiceMock = new Mock<IVaultService>();
        _notificationServiceMock = new Mock<INotificationService>();

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

        _sut = new CashCountService(_dbContext, _vaultServiceMock.Object, _notificationServiceMock.Object);
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
        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.Should().NotBeNull();
        result.StatusText.Should().Be("No open session");
        result.StatusColor.Should().Be("info");
        result.CanPerformOpeningCount.Should().BeTrue();
        result.CanPerformClosingCount.Should().BeFalse();
        result.SessionId.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithUserNotAgent_ReturnsError()
    {
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(nonAgentUser.Id);

        result.StatusText.Should().Be("User not configured as an agent");
        result.StatusColor.Should().Be("error");
        result.CanPerformOpeningCount.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithPendingApproval_BlocksFurtherCounts()
    {
        // Rule 14: Pending approval blocks new counts
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var pendingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .WithStatus(CashCountStatus.PendingApproval)
            .Build();
        _dbContext.CashCounts.Add(pendingCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.HasPendingApproval.Should().BeTrue();
        result.CanPerformOpeningCount.Should().BeFalse();
        result.CanPerformClosingCount.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithApprovedOpening_CanPerformClosingCount()
    {
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var approvedOpening = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .Build();
        _dbContext.CashCounts.Add(approvedOpening);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.CanPerformOpeningCount.Should().BeFalse();
        result.CanPerformClosingCount.Should().BeTrue();
        result.HasOpeningCount.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithPreviousUnclosedSession_Blocks()
    {
        // Rule 4: Previous unclosed session blocks new session
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var oldSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(yesterday)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(oldSession);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.StatusText.Should().Be("Previous session not closed");
        result.CanPerformOpeningCount.Should().BeFalse();
        result.BlockReason.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region SaveCashCountAsync Tests

    [Fact]
    public async Task SaveCashCountAsync_ForOpeningCount_CreatesBranchLevelSessionAndCount()
    {
        // Rules 1-3: Creates branch-level session
        var form = new CashCountFormModel
        {
            IsOpening = true,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new() { WalletId = _testWallet.Id, CountedAmount = 1000m }
            }
        };

        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();

        var session = await _dbContext.CashSessions.FindAsync(result.CashSessionId);
        session.Should().NotBeNull();
        session!.BranchId.Should().Be(_testBranch.Id);
        session.Status.Should().Be(CashSessionStatus.Open);

        var count = await _dbContext.CashCounts.FindAsync(result.CashCountId);
        count.Should().NotBeNull();
        count!.AgentId.Should().Be(_testAgent.Id);
        count.Status.Should().Be(CashCountStatus.Draft);
    }

    [Fact]
    public async Task SaveCashCountAsync_FutureDateBlocked()
    {
        // Rule 15: No future dates
        var form = new CashCountFormModel
        {
            IsOpening = true,
            CountDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            WalletEntries = new List<WalletCountEntryDto>()
        };

        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("future");
    }

    [Fact]
    public async Task SaveCashCountAsync_ClosingCountRequiresApprovedOpening()
    {
        // Use a second agent whose opening count is Submitted (not PendingApproval)
        // so rule 14 doesn't fire, but the opening isn't approved either.
        var user2 = UserBuilder.Default()
            .WithEmail("agent2@test.com")
            .WithRole(UserRole.Agent)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(user2);

        var agent2 = AgentBuilder.Default()
            .WithId(2)
            .WithUserId(user2.Id)
            .WithBranchId(_testBranch.Id)
            .WithCode("A002")
            .Build();
        _dbContext.Agents.Add(agent2);
        user2.AgentId = agent2.Id;

        var wallet2 = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(agent2.Id)
            .WithWalletTypeId(_cashWalletType.Id)
            .WithBalance(0m)
            .Build();
        _dbContext.Wallets.Add(wallet2);
        await _dbContext.SaveChangesAsync();

        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Opening is Submitted (not Approved) — use Draft+Submitted status that isn't PendingApproval
        var opening = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(agent2.Id)
            .AsOpening()
            .WithStatus(CashCountStatus.Submitted)
            .Build();
        _dbContext.CashCounts.Add(opening);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = false,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new() { WalletId = wallet2.Id, CountedAmount = 500m }
            }
        };

        var result = await _sut.SaveCashCountAsync(user2.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Opening count must be approved");
    }

    [Fact]
    public async Task SaveCashCountAsync_WithUserNotAgent_ReturnsError()
    {
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel { IsOpening = true, WalletEntries = [] };

        var result = await _sut.SaveCashCountAsync(nonAgentUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured as an agent");
    }

    [Fact]
    public async Task SaveCashCountAsync_PreviousUnclosedSessionBlocksOpeningCount()
    {
        // Rule 4: Previous unclosed session blocks
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var oldSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(yesterday)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(oldSession);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = true,
            WalletEntries = new List<WalletCountEntryDto>
            {
                new() { WalletId = _testWallet.Id, CountedAmount = 1000m }
            }
        };

        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("previous cash session has not been closed");
    }

    #endregion

    #region SubmitCashCountAsync Tests

    [Fact]
    public async Task SubmitCashCountAsync_SetsStatusToPendingApproval()
    {
        // Rule 11, 20: Submit goes to PendingApproval, not auto-execute
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .WithTotalAmount(1000m)
            .WithStatus(CashCountStatus.Draft)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, cashCount.Id);

        result.Success.Should().BeTrue();

        var updatedCount = await _dbContext.CashCounts.FindAsync(cashCount.Id);
        updatedCount!.Status.Should().Be(CashCountStatus.PendingApproval);
        updatedCount.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SubmitCashCountAsync_ClosingMatchingOpening_AutoApproves()
    {
        // Rule 12: Auto-approve matching closing counts for today
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var openingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(openingCount);
        await _dbContext.SaveChangesAsync();

        var closingCount = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .WithStatus(CashCountStatus.Draft)
            .WithTotalAmount(1000m) // Matches opening
            .Build();
        _dbContext.CashCounts.Add(closingCount);

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(closingCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(1000m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.DepositForSessionAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<string>(), false, default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, closingCount.Id);

        result.Success.Should().BeTrue();

        var updated = await _dbContext.CashCounts.FindAsync(closingCount.Id);
        updated!.Status.Should().Be(CashCountStatus.Approved);
    }

    [Fact]
    public async Task SubmitCashCountAsync_ClosingWithDiscrepancy_RequiresExplanation()
    {
        // Rule 10, 16: Discrepancy requires explanation
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var openingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(openingCount);
        await _dbContext.SaveChangesAsync();

        var closingCount = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .WithStatus(CashCountStatus.Draft)
            .WithTotalAmount(800m) // Discrepancy!
            .Build();
        _dbContext.CashCounts.Add(closingCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, closingCount.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("explanation");
    }

    [Fact]
    public async Task SubmitCashCountAsync_AlreadySubmitted_ReturnsError()
    {
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsSubmitted()
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, cashCount.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already been submitted");
    }

    #endregion

    #region ApproveCashCountAsync Tests

    [Fact]
    public async Task ApproveCashCountAsync_OpeningCount_WithdrawsFromVault()
    {
        // Rule 20: Admin approval triggers vault withdrawal
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsSubmitted()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(cashCount);

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(cashCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(1000m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.WithdrawForSessionAsync(
                session.Id, _testBranch.Id, 1000m, adminUser.Id, false, default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        var result = await _sut.ApproveCashCountAsync(adminUser.Id, cashCount.Id);

        result.Success.Should().BeTrue();

        var updated = await _dbContext.CashCounts.FindAsync(cashCount.Id);
        updated!.Status.Should().Be(CashCountStatus.Approved);
        updated.ApprovedAt.Should().NotBeNull();
        updated.ApprovedByUserId.Should().Be(adminUser.Id);

        _vaultServiceMock.Verify(x => x.WithdrawForSessionAsync(
            session.Id, _testBranch.Id, 1000m, adminUser.Id, false, default), Times.Once);
    }

    [Fact]
    public async Task ApproveCashCountAsync_NonAdmin_ReturnsError()
    {
        var agentUser = UserBuilder.Default()
            .WithEmail("agent2@test.com")
            .WithRole(UserRole.Agent)
            .Build();
        _dbContext.Users.Add(agentUser);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.ApproveCashCountAsync(agentUser.Id, 1);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only administrators or supervisors");
    }

    #endregion

    #region RejectCashCountAsync Tests

    [Fact]
    public async Task RejectCashCountAsync_SetsStatusToRejected()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);

        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsSubmitted()
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RejectCashCountAsync(adminUser.Id, cashCount.Id, "Amount does not match the expected total");

        result.Success.Should().BeTrue();

        var updated = await _dbContext.CashCounts.FindAsync(cashCount.Id);
        updated!.Status.Should().Be(CashCountStatus.Rejected);
        updated.RejectionReason.Should().Contain("Amount does not match");
    }

    [Fact]
    public async Task RejectCashCountAsync_ShortReason_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RejectCashCountAsync(adminUser.Id, 1, "short");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least 10 characters");
    }

    #endregion

    #region GetPendingApprovalsAsync Tests

    [Fact]
    public async Task GetPendingApprovalsAsync_ReturnsPendingCounts()
    {
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var pendingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsSubmitted()
            .WithTotalAmount(500m)
            .Build();
        _dbContext.CashCounts.Add(pendingCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetPendingApprovalsAsync(_testBranch.Id);

        result.Should().ContainSingle();
        result[0].CashCountId.Should().Be(pendingCount.Id);
        result[0].IsOpening.Should().BeTrue();
        result[0].TotalAmount.Should().Be(500m);
    }

    #endregion

    #region AdminCloseAgentSessionAsync Tests

    [Fact]
    public async Task AdminCloseAgentSessionAsync_CreatesClosingCountAndApproves()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);

        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var openingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(openingCount);

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(openingCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(1000m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.DepositForSessionAsync(
                It.IsAny<long>(), It.IsAny<long>(), 1000m,
                adminUser.Id, false, default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        var result = await _sut.AdminCloseAgentSessionAsync(adminUser.Id, session.Id, _testAgent.Id);

        result.Success.Should().BeTrue();

        var closingCounts = await _dbContext.CashCounts
            .Where(c => c.CashSessionId == session.Id && !c.IsOpening)
            .ToListAsync();
        closingCounts.Should().ContainSingle();
        closingCounts[0].Status.Should().Be(CashCountStatus.Approved);
        closingCounts[0].TotalAmount.Should().Be(1000m);
    }

    [Fact]
    public async Task AdminCloseAgentSessionAsync_NonAdmin_ReturnsError()
    {
        var agentUser = UserBuilder.Default()
            .WithEmail("agent2@test.com")
            .WithRole(UserRole.Agent)
            .Build();
        _dbContext.Users.Add(agentUser);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.AdminCloseAgentSessionAsync(agentUser.Id, 1, 1);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only administrators");
    }

    [Fact]
    public async Task AdminCloseAgentSessionAsync_NoApprovedOpening_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);

        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.AdminCloseAgentSessionAsync(adminUser.Id, session.Id, _testAgent.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no approved opening count");
    }

    [Fact]
    public async Task AdminCloseAgentSessionAsync_AlreadyApprovedClosing_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);

        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var opening = CashCountBuilder.Default()
            .WithId(1)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        var closing = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.AddRange(opening, closing);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.AdminCloseAgentSessionAsync(adminUser.Id, session.Id, _testAgent.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already approved");
    }

    #endregion
}
