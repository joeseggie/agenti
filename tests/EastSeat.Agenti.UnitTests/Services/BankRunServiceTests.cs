using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.BankRuns;
using EastSeat.Agenti.Web.Features.Notifications;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "BankRun")]
public class BankRunServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly BankRunService _sut;
    private readonly Branch _testBranch;
    private readonly ApplicationUser _testUser;
    private readonly Agent _testAgent;
    private readonly WalletType _cashWalletType;
    private readonly WalletType _bankWalletType;
    private readonly Wallet _cashWallet;
    private readonly Wallet _bankWallet;
    private readonly CashSession _testSession;

    public BankRunServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _dbContext.Database.EnsureCreated();
        _notificationServiceMock = new Mock<INotificationService>();

        _testBranch = new Branch { Id = 1, Name = "Test Branch", CreatedAt = DateTimeOffset.UtcNow };
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

        // Wallet types are seeded by EnsureCreated; retrieve the ones we need.
        // Cash = Id 1, Bank Account = Id 4 (per ApplicationDbContext seed data).
        _cashWalletType = _dbContext.WalletTypes.Find(1L)!;
        _bankWalletType = _dbContext.WalletTypes.Find(4L)!;

        _cashWallet = WalletBuilder.Default()
            .WithId(1)
            .WithAgentId(_testAgent.Id)
            .WithWalletTypeId(_cashWalletType.Id)
            .WithName("Cash Drawer")
            .WithBalance(500_000m)
            .Build();
        _cashWallet.WalletType = _cashWalletType;
        _dbContext.Wallets.Add(_cashWallet);

        _bankWallet = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(_testAgent.Id)
            .WithWalletTypeId(_bankWalletType.Id)
            .WithName("ABSA Account")
            .WithBalance(0m)
            .Build();
        _bankWallet.WalletType = _bankWalletType;
        _dbContext.Wallets.Add(_bankWallet);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _testSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(today)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(_testSession);

        _dbContext.SaveChanges();

        _sut = new BankRunService(_dbContext, _notificationServiceMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private void SeedApprovedOpeningCount()
    {
        var openingCount = CashCountBuilder.Default()
            .WithCashSessionId(_testSession.Id)
            .WithAgentId(_testAgent.Id)
            .WithIsOpening(true)
            .WithStatus(CashCountStatus.Approved)
            .WithTotalAmount(500_000m)
            .Build();
        _dbContext.CashCounts.Add(openingCount);
        _dbContext.SaveChanges();
    }

    #region RecordBankRunAsync Tests

    [Fact]
    public async Task RecordBankRunAsync_WithValidData_RecordsBankRunAndUpdatesBalances()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 200_000m,
            ReceiptNumber = "REC-001",
            Notes = "Test bank run"
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        result.BankRunId.Should().BeGreaterThan(0);

        var saved = await _dbContext.BankRuns.FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.Amount.Should().Be(200_000m);
        saved.FromWalletId.Should().Be(_cashWallet.Id);
        saved.ToWalletId.Should().Be(_bankWallet.Id);
        saved.AgentId.Should().Be(_testAgent.Id);
        saved.CashSessionId.Should().Be(_testSession.Id);
        saved.ReceiptNumber.Should().Be("REC-001");

        // Verify balances updated
        var updatedCash = await _dbContext.Wallets.FindAsync(_cashWallet.Id);
        updatedCash!.Balance.Should().Be(300_000m);
        var updatedBank = await _dbContext.Wallets.FindAsync(_bankWallet.Id);
        updatedBank!.Balance.Should().Be(200_000m);
    }

    [Fact]
    public async Task RecordBankRunAsync_WithDenominations_StoresDenominationsJson()
    {
        SeedApprovedOpeningCount();
        var denominations = "{\"50000\":2,\"20000\":5}";
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 200_000m,
            Denominations = denominations
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        var saved = await _dbContext.BankRuns.FirstOrDefaultAsync();
        saved!.Denominations.Should().Be(denominations);
    }

    [Fact]
    public async Task RecordBankRunAsync_UserNotAgent_ReturnsError()
    {
        var nonAgentUser = UserBuilder.Default().WithEmail("notanagent@test.com").Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(nonAgentUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured as an agent");
    }

    [Fact]
    public async Task RecordBankRunAsync_AgentNoBranch_ReturnsError()
    {
        var userNoBranch = UserBuilder.Default().WithEmail("nobranch@test.com").Build();
        _dbContext.Users.Add(userNoBranch);
        var agentNoBranch = AgentBuilder.Default().WithId(99).WithCode("NOBN").WithUserId(userNoBranch.Id).Build();
        // No BranchId set
        agentNoBranch.BranchId = null;
        _dbContext.Agents.Add(agentNoBranch);
        await _dbContext.SaveChangesAsync();

        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(userNoBranch.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not assigned to a branch");
    }

    [Fact]
    public async Task RecordBankRunAsync_NoActiveSession_ReturnsError()
    {
        _dbContext.CashSessions.Remove(_testSession);
        await _dbContext.SaveChangesAsync();

        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active cash session");
    }

    [Fact]
    public async Task RecordBankRunAsync_NoApprovedOpeningCount_ReturnsError()
    {
        // Session exists but no approved opening count
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Opening cash count must be approved");
    }

    [Fact]
    public async Task RecordBankRunAsync_WithPendingClosingCount_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var closingCount = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(_testSession.Id)
            .WithAgentId(_testAgent.Id)
            .WithIsOpening(false)
            .WithStatus(CashCountStatus.PendingApproval)
            .Build();
        _dbContext.CashCounts.Add(closingCount);
        await _dbContext.SaveChangesAsync();

        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("closing count has been submitted");
    }

    [Fact]
    public async Task RecordBankRunAsync_WithApprovedClosingCount_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var closingCount = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(_testSession.Id)
            .WithAgentId(_testAgent.Id)
            .WithIsOpening(false)
            .WithStatus(CashCountStatus.Approved)
            .Build();
        _dbContext.CashCounts.Add(closingCount);
        await _dbContext.SaveChangesAsync();

        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("closing count has been submitted");
    }

    [Fact]
    public async Task RecordBankRunAsync_ZeroAmount_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 0m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    [Fact]
    public async Task RecordBankRunAsync_NegativeAmount_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = -100m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    [Fact]
    public async Task RecordBankRunAsync_FromWalletNotFound_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = 9999,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Source wallet not found");
    }

    [Fact]
    public async Task RecordBankRunAsync_FromWalletNotCashType_ReturnsError()
    {
        SeedApprovedOpeningCount();
        // Use bank wallet as source (wrong type)
        var form = new BankRunFormModel
        {
            FromWalletId = _bankWallet.Id,
            ToWalletId = _cashWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("source wallet must be a Cash wallet");
    }

    [Fact]
    public async Task RecordBankRunAsync_ToWalletNotFound_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = 9999,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Destination wallet not found");
    }

    [Fact]
    public async Task RecordBankRunAsync_ToWalletNotBankType_ReturnsError()
    {
        SeedApprovedOpeningCount();
        // Use a seeded MobileMoney type (Id=2 MTN Mobile Money) for the wrong-type wallet
        var mmType = _dbContext.WalletTypes.Find(2L)!;
        var mmWallet = WalletBuilder.Default().WithId(3).WithAgentId(_testAgent.Id).WithWalletTypeId(mmType.Id).WithBalance(0m).Build();
        mmWallet.WalletType = mmType;
        _dbContext.Wallets.Add(mmWallet);
        await _dbContext.SaveChangesAsync();

        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = mmWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("destination wallet must be a Bank wallet");
    }

    [Fact]
    public async Task RecordBankRunAsync_SameSourceAndDestination_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _cashWallet.Id,
            Amount = 100_000m
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Source and destination wallets must be different");
    }

    [Fact]
    public async Task RecordBankRunAsync_AmountExceedsCashBalance_ReturnsError()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 600_000m  // More than the 500_000 balance
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds the cash wallet balance");
    }

    [Fact]
    public async Task RecordBankRunAsync_SendsNotificationToAdmins()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m
        };

        await _sut.RecordBankRunAsync(_testUser.Id, form);

        _notificationServiceMock.Verify(n =>
            n.NotifyBranchAdminsAsync(
                _testBranch.Id,
                "Bank Run Recorded",
                It.IsAny<string>(),
                NotificationType.BankRunRecorded,
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordBankRunAsync_TrimsReceiptNumberAndNotes()
    {
        SeedApprovedOpeningCount();
        var form = new BankRunFormModel
        {
            FromWalletId = _cashWallet.Id,
            ToWalletId = _bankWallet.Id,
            Amount = 100_000m,
            ReceiptNumber = "  REC-001  ",
            Notes = "  Some notes  "
        };

        var result = await _sut.RecordBankRunAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        var saved = await _dbContext.BankRuns.FirstOrDefaultAsync();
        saved!.ReceiptNumber.Should().Be("REC-001");
        saved.Notes.Should().Be("Some notes");
    }

    #endregion

    #region GetBankRunsForSessionAsync Tests

    [Fact]
    public async Task GetBankRunsForSessionAsync_ReturnsAllBankRunsInSession()
    {
        var run1 = BankRunBuilder.Default().WithId(1).WithCashSessionId(_testSession.Id).WithAgentId(_testAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithRecordedByUserId(_testUser.Id).Build();
        var run2 = BankRunBuilder.Default().WithId(2).WithCashSessionId(_testSession.Id).WithAgentId(_testAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithRecordedByUserId(_testUser.Id).Build();
        _dbContext.BankRuns.AddRange(run1, run2);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunsForSessionAsync(_testSession.Id);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBankRunsForSessionAsync_FilteredByAgentId_ReturnsOnlyAgentRuns()
    {
        var otherUser = UserBuilder.Default().WithEmail("other@test.com").Build();
        _dbContext.Users.Add(otherUser);
        var otherAgent = AgentBuilder.Default().WithId(2).WithCode("OTHR").WithUserId(otherUser.Id).WithBranchId(_testBranch.Id).Build();
        _dbContext.Agents.Add(otherAgent);
        await _dbContext.SaveChangesAsync();

        var run1 = BankRunBuilder.Default().WithId(1).WithCashSessionId(_testSession.Id).WithAgentId(_testAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithRecordedByUserId(_testUser.Id).Build();
        var run2 = BankRunBuilder.Default().WithId(2).WithCashSessionId(_testSession.Id).WithAgentId(otherAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithRecordedByUserId(otherUser.Id).Build();
        _dbContext.BankRuns.AddRange(run1, run2);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunsForSessionAsync(_testSession.Id, _testAgent.Id);

        result.Should().HaveCount(1);
        result[0].AgentCode.Should().Be(_testAgent.Code);
    }

    [Fact]
    public async Task GetBankRunsForSessionAsync_NoRuns_ReturnsEmpty()
    {
        var result = await _sut.GetBankRunsForSessionAsync(_testSession.Id);

        result.Should().BeEmpty();
    }

    #endregion

    #region GetBankRunsForAgentAsync Tests

    [Fact]
    public async Task GetBankRunsForAgentAsync_WithActiveSession_ReturnsBankRuns()
    {
        var run = BankRunBuilder.Default().WithId(1).WithCashSessionId(_testSession.Id).WithAgentId(_testAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithRecordedByUserId(_testUser.Id).Build();
        _dbContext.BankRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunsForAgentAsync(_testUser.Id);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetBankRunsForAgentAsync_UserNotAgent_ReturnsEmpty()
    {
        var nonAgent = UserBuilder.Default().WithEmail("notanagent2@test.com").Build();
        _dbContext.Users.Add(nonAgent);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunsForAgentAsync(nonAgent.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBankRunsForAgentAsync_NoActiveSession_ReturnsEmpty()
    {
        _testSession.Status = CashSessionStatus.Closed;
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunsForAgentAsync(_testUser.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBankRunsForAgentAsync_AgentNoBranch_ReturnsEmpty()
    {
        _testAgent.BranchId = null;
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunsForAgentAsync(_testUser.Id);

        result.Should().BeEmpty();
    }

    #endregion

    #region GetBankRunTotalsAsync Tests

    [Fact]
    public async Task GetBankRunTotalsAsync_ReturnsTotalGroupedByFromWalletId()
    {
        var run1 = BankRunBuilder.Default().WithId(1).WithCashSessionId(_testSession.Id).WithAgentId(_testAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithAmount(100_000m)
            .WithRecordedByUserId(_testUser.Id).Build();
        var run2 = BankRunBuilder.Default().WithId(2).WithCashSessionId(_testSession.Id).WithAgentId(_testAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithAmount(50_000m)
            .WithRecordedByUserId(_testUser.Id).Build();
        _dbContext.BankRuns.AddRange(run1, run2);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunTotalsAsync(_testSession.Id, _testAgent.Id);

        result.Should().ContainKey(_cashWallet.Id);
        result[_cashWallet.Id].Should().Be(150_000m);
    }

    [Fact]
    public async Task GetBankRunTotalsAsync_NoBankRuns_ReturnsEmptyDictionary()
    {
        var result = await _sut.GetBankRunTotalsAsync(_testSession.Id, _testAgent.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBankRunTotalsAsync_OnlyCountsForGivenAgent()
    {
        var otherUser = UserBuilder.Default().WithEmail("other2@test.com").Build();
        _dbContext.Users.Add(otherUser);
        var otherAgent = AgentBuilder.Default().WithId(2).WithCode("OTHR").WithUserId(otherUser.Id).WithBranchId(_testBranch.Id).Build();
        _dbContext.Agents.Add(otherAgent);

        var run = BankRunBuilder.Default().WithId(1).WithCashSessionId(_testSession.Id).WithAgentId(otherAgent.Id)
            .WithFromWalletId(_cashWallet.Id).WithToWalletId(_bankWallet.Id).WithAmount(200_000m)
            .WithRecordedByUserId(otherUser.Id).Build();
        _dbContext.BankRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetBankRunTotalsAsync(_testSession.Id, _testAgent.Id);

        result.Should().BeEmpty();
    }

    #endregion

    #region GetAgentWalletsAsync Tests

    [Fact]
    public async Task GetAgentWalletsAsync_ReturnsCashWallets()
    {
        var result = await _sut.GetAgentWalletsAsync(_testUser.Id, WalletTypeEnum.Cash);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(_cashWallet.Id);
        result[0].Balance.Should().Be(_cashWallet.Balance);
    }

    [Fact]
    public async Task GetAgentWalletsAsync_ReturnsBankWallets()
    {
        var result = await _sut.GetAgentWalletsAsync(_testUser.Id, WalletTypeEnum.Bank);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(_bankWallet.Id);
    }

    [Fact]
    public async Task GetAgentWalletsAsync_UserNotAgent_ReturnsEmpty()
    {
        var nonAgent = UserBuilder.Default().WithEmail("nonagent3@test.com").Build();
        _dbContext.Users.Add(nonAgent);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAgentWalletsAsync(nonAgent.Id, WalletTypeEnum.Cash);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAgentWalletsAsync_ExcludesInactiveWallets()
    {
        var inactiveWalletType = new WalletType { Id = 100, Name = "Inactive Bank", Type = WalletTypeEnum.Bank, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.WalletTypes.Add(inactiveWalletType);
        var inactiveWallet = WalletBuilder.Default().WithId(4).WithAgentId(_testAgent.Id).WithWalletTypeId(100).IsInactive().Build();
        inactiveWallet.WalletType = inactiveWalletType;
        _dbContext.Wallets.Add(inactiveWallet);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAgentWalletsAsync(_testUser.Id, WalletTypeEnum.Bank);

        result.Should().HaveCount(1); // Only active bank wallet
        result[0].Id.Should().Be(_bankWallet.Id);
    }

    #endregion
}
