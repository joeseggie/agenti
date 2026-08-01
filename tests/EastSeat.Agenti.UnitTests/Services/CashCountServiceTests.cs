using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.Vaults;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
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
    private readonly Mock<IWalletAdjustmentService> _walletAdjustmentServiceMock;
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
        _walletAdjustmentServiceMock = new Mock<IWalletAdjustmentService>();
        _walletAdjustmentServiceMock
            .Setup(x => x.GetWalletAdjustmentTotalsAsync(It.IsAny<long>(), It.IsAny<long>()))
            .ReturnsAsync(new Dictionary<long, decimal>());

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

        _sut = new CashCountService(_dbContext, _vaultServiceMock.Object, _notificationServiceMock.Object, _walletAdjustmentServiceMock.Object);
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
    public async Task GetCurrentSessionAsync_WithPendingOpeningApproval_AllowsRevisingButBlocksClosing()
    {
        // Updated behavior: a pending opening cash count CAN be revised by the agent
        // (saved as draft or re-submitted), but the closing count remains blocked until
        // the opening is approved.
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
        // Agent should be able to revise the pending opening count.
        result.CanPerformOpeningCount.Should().BeTrue();
        // Closing remains blocked until the opening is approved.
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
    public async Task GetCurrentSessionAsync_WithApprovedCounts_ReturnsApprovalDetails()
    {
        var approvedAt = new DateTimeOffset(2026, 5, 18, 14, 30, 0, TimeSpan.Zero);
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithFirstName("Ada")
            .WithLastName("Admin")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);

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
            .WithApprovedAt(approvedAt)
            .WithApprovedByUserId(adminUser.Id)
            .Build();

        var approvedClosing = CashCountBuilder.Default()
            .WithId(99)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsApproved()
            .WithApprovedAt(approvedAt.AddHours(3))
            .WithApprovedByUserId(adminUser.Id)
            .Build();

        _dbContext.CashCounts.AddRange(approvedOpening, approvedClosing);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.OpeningCountApprovedByName.Should().Be("Ada Admin");
        result.OpeningCountApprovedAt.Should().Be(approvedAt);
        result.ClosingCountApprovedByName.Should().Be("Ada Admin");
        result.ClosingCountApprovedAt.Should().Be(approvedAt.AddHours(3));
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

    [Fact]
    public async Task GetCurrentSessionAsync_WithApprovedOpening_CanRecordBankRun()
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

        result.CanRecordBankRun.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithNoOpenSession_CannotRecordBankRun()
    {
        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.CanRecordBankRun.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithNoApprovedOpening_CannotRecordBankRun()
    {
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.CanRecordBankRun.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithSubmittedClosingCount_CannotRecordBankRun()
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
        var pendingClosing = CashCountBuilder.Default()
            .WithId(99)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .WithIsOpening(false)
            .WithStatus(CashCountStatus.PendingApproval)
            .Build();
        _dbContext.CashCounts.AddRange(approvedOpening, pendingClosing);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.CanRecordBankRun.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_WithApprovedClosingCount_CannotRecordBankRun()
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
        var approvedClosing = CashCountBuilder.Default()
            .WithId(99)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .WithIsOpening(false)
            .WithStatus(CashCountStatus.Approved)
            .Build();
        _dbContext.CashCounts.AddRange(approvedOpening, approvedClosing);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetCurrentSessionAsync(_testUser.Id);

        result.CanRecordBankRun.Should().BeFalse();
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
        result.ErrorMessage.Should().Contain("pending approval");
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

    #region Opening Count Discrepancy Scenarios

    [Fact]
    public async Task SubmitCashCountAsync_OpeningDifferingFromPreviousClosing_RequiresExplanation()
    {
        var (_, openingCount) = await SeedPreviousClosingAndTodayOpeningAsync(
            previousClosingTotal: 1000m,
            openingTotal: 800m,
            openingExplanation: null);

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, openingCount.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("explanation");

        var reloaded = await _dbContext.CashCounts.FindAsync(openingCount.Id);
        reloaded!.Status.Should().Be(CashCountStatus.Draft);
        reloaded.SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task SubmitCashCountAsync_OpeningDifferingFromPreviousClosing_CreatesDiscrepancy()
    {
        var (_, openingCount) = await SeedPreviousClosingAndTodayOpeningAsync(
            previousClosingTotal: 1000m,
            openingTotal: 800m,
            openingExplanation: "Shortage carried over from overnight safe transfer");

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, openingCount.Id);

        result.Success.Should().BeTrue();

        var discrepancy = await _dbContext.Discrepancies
            .FirstOrDefaultAsync(d => d.CashCountId == openingCount.Id);
        discrepancy.Should().NotBeNull();
        discrepancy!.ExpectedAmount.Should().Be(1000m);
        discrepancy.ActualAmount.Should().Be(800m);
        discrepancy.Variance.Should().Be(-200m);
        discrepancy.Status.Should().Be(DiscrepancyStatus.PendingReview);
        discrepancy.Explanation.Should().Be("Shortage carried over from overnight safe transfer");
    }

    [Fact]
    public async Task SubmitCashCountAsync_OpeningMatchingPreviousClosing_CreatesNoDiscrepancy()
    {
        var (_, openingCount) = await SeedPreviousClosingAndTodayOpeningAsync(
            previousClosingTotal: 1000m,
            openingTotal: 1000m,
            openingExplanation: null);

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, openingCount.Id);

        result.Success.Should().BeTrue();

        var updated = await _dbContext.CashCounts.FindAsync(openingCount.Id);
        updated!.Status.Should().Be(CashCountStatus.PendingApproval);

        var discrepancies = await _dbContext.Discrepancies
            .Where(d => d.CashCountId == openingCount.Id)
            .ToListAsync();
        discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveCashCountAsync_OpeningWithDiscrepancy_ApprovesDiscrepancy()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin-opening@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        var (_, openingCount) = await SeedPreviousClosingAndTodayOpeningAsync(
            previousClosingTotal: 1000m,
            openingTotal: 800m,
            openingExplanation: "Shortage carried over from overnight safe transfer");

        _vaultServiceMock
            .Setup(x => x.WithdrawForSessionAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<string>(), false, default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        var submitResult = await _sut.SubmitCashCountAsync(_testUser.Id, openingCount.Id);
        submitResult.Success.Should().BeTrue();

        var result = await _sut.ApproveCashCountAsync(adminUser.Id, openingCount.Id);

        result.Success.Should().BeTrue();

        var discrepancy = await _dbContext.Discrepancies
            .FirstOrDefaultAsync(d => d.CashCountId == openingCount.Id);
        discrepancy!.Status.Should().Be(DiscrepancyStatus.Approved);
        discrepancy.ApprovedByUserId.Should().Be(adminUser.Id);
    }

    [Fact]
    public async Task InitializeCashCountFormAsync_Opening_PopulatesPreviousClosingTotal()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var previousSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(yesterday)
            .AsClosed()
            .Build();
        _dbContext.CashSessions.Add(previousSession);
        await _dbContext.SaveChangesAsync();

        var previousClosing = CashCountBuilder.Default()
            .WithCashSessionId(previousSession.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsApproved()
            .WithCountDate(yesterday)
            .WithTotalAmount(1500m)
            .Build();
        _dbContext.CashCounts.Add(previousClosing);
        await _dbContext.SaveChangesAsync();

        var form = await _sut.InitializeCashCountFormAsync(_testUser.Id, isOpening: true);

        form.PreviousClosingTotal.Should().Be(1500m);
        form.PreviousClosingDate.Should().Be(yesterday);
    }

    [Fact]
    public async Task InitializeCashCountFormAsync_Closing_DoesNotPopulatePreviousClosingTotal()
    {
        var form = await _sut.InitializeCashCountFormAsync(_testUser.Id, isOpening: false);

        form.PreviousClosingTotal.Should().BeNull();
        form.OpeningVariance.Should().Be(0m);
    }

    /// <summary>
    /// Seeds an approved closing count for the previous day plus a draft opening count for today.
    /// </summary>
    private async Task<(CashSession Session, CashCount OpeningCount)> SeedPreviousClosingAndTodayOpeningAsync(
        decimal previousClosingTotal, decimal openingTotal, string? openingExplanation)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        var previousSession = CashSessionBuilder.Default()
            .WithId(10)
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(yesterday)
            .AsClosed()
            .Build();
        _dbContext.CashSessions.Add(previousSession);
        await _dbContext.SaveChangesAsync();

        var previousClosing = CashCountBuilder.Default()
            .WithId(10)
            .WithCashSessionId(previousSession.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsApproved()
            .WithCountDate(yesterday)
            .WithTotalAmount(previousClosingTotal)
            .Build();
        _dbContext.CashCounts.Add(previousClosing);
        await _dbContext.SaveChangesAsync();

        var session = CashSessionBuilder.Default()
            .WithId(11)
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(today)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var openingCount = CashCountBuilder.Default()
            .WithId(11)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .WithStatus(CashCountStatus.Draft)
            .WithCountDate(today)
            .WithTotalAmount(openingTotal)
            .WithExplanation(openingExplanation)
            .Build();
        _dbContext.CashCounts.Add(openingCount);

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(openingCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(openingTotal)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        return (session, openingCount);
    }

    #endregion

    #region Surplus Scenarios

    [Fact]
    public async Task SubmitCashCountAsync_ClosingWithSurplus_CreatesDiscrepancyWithPositiveVariance()
    {
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
            .WithTotalAmount(1200m) // Surplus: 200 more than opening
            .WithExplanation("Customer returned excess change")
            .Build();
        _dbContext.CashCounts.Add(closingCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, closingCount.Id);

        result.Success.Should().BeTrue();

        var discrepancy = await _dbContext.Discrepancies
            .FirstOrDefaultAsync(d => d.CashCountId == closingCount.Id);
        discrepancy.Should().NotBeNull();
        discrepancy!.Variance.Should().Be(200m);
        discrepancy.ExpectedAmount.Should().Be(1000m);
        discrepancy.ActualAmount.Should().Be(1200m);
        discrepancy.Status.Should().Be(DiscrepancyStatus.PendingReview);
    }

    [Fact]
    public async Task SubmitCashCountAsync_ClosingWithSurplus_RequiresExplanation()
    {
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
            .WithTotalAmount(1200m) // Surplus without explanation
            .Build();
        _dbContext.CashCounts.Add(closingCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.SubmitCashCountAsync(_testUser.Id, closingCount.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("explanation");
    }

    [Fact]
    public async Task ApproveCashCountAsync_ClosingWithSurplus_CreatesSurplusVaultTransaction()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        // Use the auto-created vault (created when _testBranch was added)
        var vault = await _dbContext.Vaults.FirstAsync(v => v.BranchId == _testBranch.Id);

        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var closingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsSubmitted()
            .WithTotalAmount(1200m)
            .Build();
        _dbContext.CashCounts.Add(closingCount);

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(closingCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(1200m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);

        // Create a pending discrepancy with positive variance (surplus)
        var discrepancy = new Discrepancy
        {
            CashSessionId = session.Id,
            CashCountId = closingCount.Id,
            Status = DiscrepancyStatus.PendingReview,
            ExpectedAmount = 1000m,
            ActualAmount = 1200m,
            Variance = 200m,
            Explanation = "Customer returned excess change",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Discrepancies.Add(discrepancy);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.DepositForSessionAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<string>(), false, default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        var result = await _sut.ApproveCashCountAsync(adminUser.Id, closingCount.Id);

        result.Success.Should().BeTrue();

        var surplusTransaction = await _dbContext.VaultTransactions
            .FirstOrDefaultAsync(vt => vt.Type == VaultTransactionType.SurplusDeposit);
        surplusTransaction.Should().NotBeNull();
        surplusTransaction!.Amount.Should().Be(200m);
        surplusTransaction.VaultId.Should().Be(vault.Id);
        surplusTransaction.Status.Should().Be(VaultTransactionStatus.Completed);
        surplusTransaction.Notes.Should().Contain("Surplus");
    }

    [Fact]
    public async Task ApproveCashCountAsync_ClosingWithShortage_DoesNotCreateSurplusTransaction()
    {
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

        var closingCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsSubmitted()
            .WithTotalAmount(800m)
            .Build();
        _dbContext.CashCounts.Add(closingCount);

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(closingCount.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(800m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);

        // Create a pending discrepancy with negative variance (shortage)
        var discrepancy = new Discrepancy
        {
            CashSessionId = session.Id,
            CashCountId = closingCount.Id,
            Status = DiscrepancyStatus.PendingReview,
            ExpectedAmount = 1000m,
            ActualAmount = 800m,
            Variance = -200m,
            Explanation = "Cash was lost during the day",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Discrepancies.Add(discrepancy);
        await _dbContext.SaveChangesAsync();

        _vaultServiceMock
            .Setup(x => x.DepositForSessionAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<string>(), false, default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        var result = await _sut.ApproveCashCountAsync(adminUser.Id, closingCount.Id);

        result.Success.Should().BeTrue();

        var surplusTransaction = await _dbContext.VaultTransactions
            .FirstOrDefaultAsync(vt => vt.Type == VaultTransactionType.SurplusDeposit);
        surplusTransaction.Should().BeNull();
    }

    #endregion

    #region UnapproveCashCountAsync Tests

    [Fact]
    public async Task UnapproveCashCountAsync_NonAdmin_ReturnsError()
    {
        var supervisor = UserBuilder.Default()
            .WithEmail("sup@test.com")
            .WithRole(UserRole.Supervisor)
            .Build();
        _dbContext.Users.Add(supervisor);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.UnapproveCashCountAsync(supervisor.Id, 1, "Reason long enough");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only administrators");
    }

    [Fact]
    public async Task UnapproveCashCountAsync_ShortReason_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin-uap1@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.UnapproveCashCountAsync(adminUser.Id, 1, "short");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least 10 characters");
    }

    [Fact]
    public async Task UnapproveCashCountAsync_NonApprovedCount_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin-uap2@test.com")
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
            .WithStatus(CashCountStatus.PendingApproval)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.UnapproveCashCountAsync(adminUser.Id, cashCount.Id, "Need to correct values");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only approved");
    }

    [Fact]
    public async Task UnapproveCashCountAsync_NotTodayCount_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin-uap3@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminUser);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(yesterday)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var cashCount = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .WithCountDate(yesterday)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(500m)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.UnapproveCashCountAsync(adminUser.Id, cashCount.Id, "Try to unapprove old count");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("today");
    }

    [Fact]
    public async Task UnapproveCashCountAsync_OpeningWhileClosingApproved_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin-uap4@test.com")
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
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(opening);

        var closing = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(closing);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.UnapproveCashCountAsync(adminUser.Id, opening.Id, "Want to fix the opening");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("closing count is already approved");
    }

    [Fact]
    public async Task UnapproveCashCountAsync_OpeningWhileClosingPending_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin-uap5@test.com")
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
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(opening);

        var closing = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .WithStatus(CashCountStatus.PendingApproval)
            .WithTotalAmount(900m)
            .Build();
        _dbContext.CashCounts.Add(closing);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.UnapproveCashCountAsync(adminUser.Id, opening.Id, "Need to revise opening");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Reject the closing count first");
    }

    [Fact]
    public async Task UnapproveCashCountAsync_OpeningTodayNoClosing_Succeeds()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin-uap6@test.com")
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
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(opening);

        var detail = CashCountDetailBuilder.Default()
            .WithCashCountId(opening.Id)
            .WithWalletId(_testWallet.Id)
            .WithAmount(1000m)
            .Build();
        _dbContext.CashCountDetails.Add(detail);
        await _dbContext.SaveChangesAsync();

        // Vault must accept the reversing deposit for opening unapproval.
        _vaultServiceMock
            .Setup(x => x.DepositForSessionAsync(
                session.Id, _testBranch.Id, 1000m, adminUser.Id, false, default))
            .ReturnsAsync(VaultOperationResult.Ok(1));

        var result = await _sut.UnapproveCashCountAsync(adminUser.Id, opening.Id, "Wrong denomination split");

        result.Success.Should().BeTrue();

        var updated = await _dbContext.CashCounts.FindAsync(opening.Id);
        updated!.Status.Should().Be(CashCountStatus.PendingApproval);
        updated.ApprovedAt.Should().BeNull();
        updated.ApprovedByUserId.Should().BeNull();

        var auditLog = await _dbContext.CashCountAuditLogs
            .FirstOrDefaultAsync(a => a.CashCountId == opening.Id && a.Action == CashCountAuditAction.Unapproved);
        auditLog.Should().NotBeNull();
        auditLog!.PerformedByUserId.Should().Be(adminUser.Id);
        auditLog.PreviousStatus.Should().Be(CashCountStatus.Approved);
        auditLog.NewStatus.Should().Be(CashCountStatus.PendingApproval);
    }

    #endregion

    #region GetCashCountHistoryAsync Tests

    [Fact]
    public async Task GetCashCountHistoryAsync_ReturnsAuditEntriesInDescendingOrder()
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
            .WithStatus(CashCountStatus.Draft)
            .WithTotalAmount(500m)
            .Build();
        _dbContext.CashCounts.Add(cashCount);
        await _dbContext.SaveChangesAsync();

        _dbContext.CashCountAuditLogs.Add(new CashCountAuditLog
        {
            CashCountId = cashCount.Id,
            CashSessionId = session.Id,
            AgentId = _testAgent.Id,
            IsOpening = true,
            Action = CashCountAuditAction.Created,
            PreviousStatus = null,
            NewStatus = CashCountStatus.Draft,
            TotalAmount = 500m,
            PerformedByUserId = _testUser.Id,
            PerformedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });
        _dbContext.CashCountAuditLogs.Add(new CashCountAuditLog
        {
            CashCountId = cashCount.Id,
            CashSessionId = session.Id,
            AgentId = _testAgent.Id,
            IsOpening = true,
            Action = CashCountAuditAction.Submitted,
            PreviousStatus = CashCountStatus.Draft,
            NewStatus = CashCountStatus.PendingApproval,
            TotalAmount = 500m,
            PerformedByUserId = _testUser.Id,
            PerformedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await _dbContext.SaveChangesAsync();

        var history = await _sut.GetCashCountHistoryAsync(session.Id);

        history.Should().HaveCount(2);
        history[0].Action.Should().Be(CashCountAuditAction.Submitted);
        history[1].Action.Should().Be(CashCountAuditAction.Created);
        history[0].PerformedByName.Should().NotBe("System");
    }

    #endregion

    #region SaveCashCountAsync (Edit Pending) Tests

    [Fact]
    public async Task SaveCashCountAsync_EditPendingOpening_ResetsToDraft()
    {
        // Acceptance criterion: if a cash count is pending approval, the agent can make
        // changes and save them as a draft.
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var pending = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsSubmitted()
            .WithTotalAmount(500m)
            .Build();
        _dbContext.CashCounts.Add(pending);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = true,
            CountDate = DateOnly.FromDateTime(DateTime.UtcNow),
            WalletEntries =
            {
                new WalletCountEntryDto
                {
                    WalletId = _testWallet.Id,
                    WalletName = _testWallet.Name,
                    WalletTypeName = "Cash",
                    CountedAmount = 750m
                }
            }
        };

        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        var updated = await _dbContext.CashCounts.FindAsync(pending.Id);
        updated!.Status.Should().Be(CashCountStatus.Draft);
        updated.SubmittedAt.Should().BeNull();
        updated.TotalAmount.Should().Be(750m);

        var savedAudit = await _dbContext.CashCountAuditLogs
            .Where(a => a.CashCountId == pending.Id && a.Action == CashCountAuditAction.Saved)
            .FirstOrDefaultAsync();
        savedAudit.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveCashCountAsync_EditOpeningWhilePendingClosing_ReturnsError()
    {
        // Acceptance criterion: if a closing count has been submitted, the opening cannot be edited.
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var opening = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(opening);

        var closing = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsSubmitted()
            .WithTotalAmount(950m)
            .Build();
        _dbContext.CashCounts.Add(closing);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = true,
            CountDate = DateOnly.FromDateTime(DateTime.UtcNow),
            WalletEntries =
            {
                new WalletCountEntryDto
                {
                    WalletId = _testWallet.Id,
                    WalletName = _testWallet.Name,
                    WalletTypeName = "Cash",
                    CountedAmount = 800m
                }
            }
        };

        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pending approval");
    }

    [Fact]
    public async Task SaveCashCountAsync_EditOpeningWhileApprovedClosing_ReturnsError()
    {
        // Acceptance criterion: once a closing count is approved, the opening cannot be changed.
        var session = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var opening = CashCountBuilder.Default()
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsOpening()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(opening);

        var closing = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(session.Id)
            .WithAgentId(_testAgent.Id)
            .AsClosing()
            .AsApproved()
            .WithTotalAmount(1000m)
            .Build();
        _dbContext.CashCounts.Add(closing);
        await _dbContext.SaveChangesAsync();

        var form = new CashCountFormModel
        {
            IsOpening = true,
            CountDate = DateOnly.FromDateTime(DateTime.UtcNow),
            WalletEntries =
            {
                new WalletCountEntryDto
                {
                    WalletId = _testWallet.Id,
                    WalletName = _testWallet.Name,
                    WalletTypeName = "Cash",
                    CountedAmount = 800m
                }
            }
        };

        var result = await _sut.SaveCashCountAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("closing count");
    }

    #endregion
}
