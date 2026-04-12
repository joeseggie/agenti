using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.PendingTransactions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "PendingTransactions")]
public class PendingTransactionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly PendingTransactionService _sut;
    private readonly Branch _testBranch;
    private readonly ApplicationUser _testUser;
    private readonly Agent _testAgent;
    private readonly WalletType _cashWalletType;
    private readonly Wallet _testWallet;
    private readonly CashSession _testSession;

    public PendingTransactionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

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
            .WithBalance(500_000m)
            .Build();
        _dbContext.Wallets.Add(_testWallet);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _testSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(today)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(_testSession);

        _dbContext.SaveChanges();

        _sut = new PendingTransactionService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region RecordPendingTransactionAsync Tests

    [Fact]
    public async Task RecordPendingTransactionAsync_WithValidData_ReturnsSuccess()
    {
        var form = new PendingTransactionFormModel
        {
            WalletId = _testWallet.Id,
            Type = PendingTransactionType.OutboundTransferFailed,
            Amount = 50_000m,
            CustomerAccountNumber = "0771234567",
            Notes = "Money sent but not received by customer"
        };

        var result = await _sut.RecordPendingTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        result.PendingTransactionId.Should().BeGreaterThan(0);

        var saved = await _dbContext.PendingTransactions.FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.Amount.Should().Be(50_000m);
        saved.Type.Should().Be(PendingTransactionType.OutboundTransferFailed);
        saved.Status.Should().Be(PendingTransactionStatus.Open);
        saved.AgentId.Should().Be(_testAgent.Id);
        saved.CashSessionId.Should().Be(_testSession.Id);
        saved.CustomerAccountNumber.Should().Be("0771234567");
    }

    [Fact]
    public async Task RecordPendingTransactionAsync_SelfLiquidationFailed_ReturnsSuccess()
    {
        var form = new PendingTransactionFormModel
        {
            WalletId = _testWallet.Id,
            Type = PendingTransactionType.SelfLiquidationFailed,
            Amount = 200_000m,
            Notes = "Transfer to own MTN wallet failed due to backend error"
        };

        var result = await _sut.RecordPendingTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        var saved = await _dbContext.PendingTransactions.FirstOrDefaultAsync();
        saved!.Type.Should().Be(PendingTransactionType.SelfLiquidationFailed);
    }

    [Fact]
    public async Task RecordPendingTransactionAsync_WithNoActiveSession_ReturnsError()
    {
        _dbContext.CashSessions.Remove(_testSession);
        await _dbContext.SaveChangesAsync();

        var form = new PendingTransactionFormModel
        {
            WalletId = _testWallet.Id,
            Type = PendingTransactionType.OutboundTransferFailed,
            Amount = 50_000m,
            Notes = "Money sent but not received by customer"
        };

        var result = await _sut.RecordPendingTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active cash session");
    }

    [Fact]
    public async Task RecordPendingTransactionAsync_WithNonAgentUser_ReturnsError()
    {
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("noagent@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var form = new PendingTransactionFormModel
        {
            WalletId = _testWallet.Id,
            Type = PendingTransactionType.OutboundTransferFailed,
            Amount = 50_000m,
            Notes = "Money sent but not received by customer"
        };

        var result = await _sut.RecordPendingTransactionAsync(nonAgentUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured as an agent");
    }

    [Fact]
    public async Task RecordPendingTransactionAsync_WithZeroAmount_ReturnsError()
    {
        var form = new PendingTransactionFormModel
        {
            WalletId = _testWallet.Id,
            Type = PendingTransactionType.OutboundTransferFailed,
            Amount = 0m,
            Notes = "Money sent but not received by customer"
        };

        var result = await _sut.RecordPendingTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Amount must be greater than zero");
    }

    [Fact]
    public async Task RecordPendingTransactionAsync_WithShortNotes_ReturnsError()
    {
        var form = new PendingTransactionFormModel
        {
            WalletId = _testWallet.Id,
            Type = PendingTransactionType.OutboundTransferFailed,
            Amount = 50_000m,
            Notes = "Short"
        };

        var result = await _sut.RecordPendingTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Notes are required");
    }

    [Fact]
    public async Task RecordPendingTransactionAsync_WithWrongWallet_ReturnsError()
    {
        var form = new PendingTransactionFormModel
        {
            WalletId = 99999, // Non-existent wallet
            Type = PendingTransactionType.OutboundTransferFailed,
            Amount = 50_000m,
            Notes = "Money sent but not received by customer"
        };

        var result = await _sut.RecordPendingTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Wallet not found");
    }

    #endregion

    #region UpdatePendingTransactionAsync Tests

    [Fact]
    public async Task UpdatePendingTransactionAsync_AddTicketNumber_ReturnsSuccess()
    {
        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel
        {
            TicketNumber = "INC-20260411-001",
            NewStatus = PendingTransactionStatus.ReportedToBank
        };

        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, pending.Id, update);

        result.Success.Should().BeTrue();

        var saved = await _dbContext.PendingTransactions.FindAsync(pending.Id);
        saved!.TicketNumber.Should().Be("INC-20260411-001");
        saved.Status.Should().Be(PendingTransactionStatus.ReportedToBank);
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_ReportedToBankWithoutTicket_ReturnsError()
    {
        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel
        {
            NewStatus = PendingTransactionStatus.ReportedToBank
            // No TicketNumber
        };

        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, pending.Id, update);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ticket number is required");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_ResolveWithNotes_ReturnsSuccess()
    {
        var pending = CreateTestPendingTransaction(PendingTransactionStatus.ReportedToBank);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel
        {
            NewStatus = PendingTransactionStatus.Resolved,
            ResolutionNotes = "Bank confirmed the money was returned to our account"
        };

        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, pending.Id, update);

        result.Success.Should().BeTrue();

        var saved = await _dbContext.PendingTransactions.FindAsync(pending.Id);
        saved!.Status.Should().Be(PendingTransactionStatus.Resolved);
        saved.ResolvedAt.Should().NotBeNull();
        saved.ResolutionNotes.Should().Be("Bank confirmed the money was returned to our account");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_ResolveWithoutNotes_ReturnsError()
    {
        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel
        {
            NewStatus = PendingTransactionStatus.Resolved
            // No ResolutionNotes
        };

        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, pending.Id, update);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Resolution notes are required");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_AlreadyResolved_ReturnsError()
    {
        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Resolved);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel
        {
            TicketNumber = "INC-001"
        };

        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, pending.Id, update);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("resolved or cancelled");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_InvalidStatusTransition_ReturnsError()
    {
        // Cannot go from Resolved back to Open
        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel
        {
            NewStatus = PendingTransactionStatus.Open // Same state – invalid transition
        };

        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, pending.Id, update);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cannot transition");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_NotFound_ReturnsError()
    {
        var update = new PendingTransactionUpdateModel
        {
            TicketNumber = "INC-001"
        };

        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, 99999, update);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_UnauthorizedUser_ReturnsError()
    {
        var anotherUser = UserBuilder.Default()
            .WithEmail("other@test.com")
            .WithRole(UserRole.Agent)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(anotherUser);

        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel { TicketNumber = "INC-001" };
        var result = await _sut.UpdatePendingTransactionAsync(anotherUser.Id, pending.Id, update);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not authorized");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_AdminCanUpdateAnyRecord_ReturnsSuccess()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);

        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel { TicketNumber = "INC-ADMIN-001" };
        var result = await _sut.UpdatePendingTransactionAsync(adminUser.Id, pending.Id, update);

        result.Success.Should().BeTrue();
        var saved = await _dbContext.PendingTransactions.FindAsync(pending.Id);
        saved!.TicketNumber.Should().Be("INC-ADMIN-001");
    }

    [Fact]
    public async Task UpdatePendingTransactionAsync_ShortNotes_ReturnsError()
    {
        var pending = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(pending);
        await _dbContext.SaveChangesAsync();

        var update = new PendingTransactionUpdateModel { Notes = "Short" };
        var result = await _sut.UpdatePendingTransactionAsync(_testUser.Id, pending.Id, update);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Notes must be at least 10 characters");
    }

    #endregion

    #region GetPendingTransactionsForAgentAsync Tests

    [Fact]
    public async Task GetPendingTransactionsForAgentAsync_ReturnsTodaysSessionItems()
    {
        var pt1 = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        var pt2 = CreateTestPendingTransaction(PendingTransactionStatus.ReportedToBank);
        _dbContext.PendingTransactions.AddRange(pt1, pt2);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetPendingTransactionsForAgentAsync(_testUser.Id);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPendingTransactionsForAgentAsync_NoSession_ReturnsEmpty()
    {
        _dbContext.CashSessions.Remove(_testSession);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetPendingTransactionsForAgentAsync(_testUser.Id);

        result.Should().BeEmpty();
    }

    #endregion

    #region GetOpenPendingTransactionsForBranchAsync Tests

    [Fact]
    public async Task GetOpenPendingTransactionsForBranchAsync_AdminUser_ReturnsOnlyOpenAndReported()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        var open = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        var reported = CreateTestPendingTransaction(PendingTransactionStatus.ReportedToBank);
        var resolved = CreateTestPendingTransaction(PendingTransactionStatus.Resolved);
        _dbContext.PendingTransactions.AddRange(open, reported, resolved);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetOpenPendingTransactionsForBranchAsync(adminUser.Id, _testBranch.Id);

        result.Should().HaveCount(2);
        result.Should().NotContain(t => t.Status == PendingTransactionStatus.Resolved);
    }

    [Fact]
    public async Task GetOpenPendingTransactionsForBranchAsync_AgentUser_ReturnsEmpty()
    {
        var open = CreateTestPendingTransaction(PendingTransactionStatus.Open);
        _dbContext.PendingTransactions.Add(open);
        await _dbContext.SaveChangesAsync();

        // _testUser is an Agent, not Admin/Supervisor — should be denied
        var result = await _sut.GetOpenPendingTransactionsForBranchAsync(_testUser.Id, _testBranch.Id);

        result.Should().BeEmpty();
    }

    #endregion

    #region GetAgentWalletsForUserAsync Tests

    [Fact]
    public async Task GetAgentWalletsForUserAsync_ReturnsActiveWallets()
    {
        var result = await _sut.GetAgentWalletsForUserAsync(_testUser.Id);

        result.Should().HaveCount(1);
        result[0].WalletId.Should().Be(_testWallet.Id);
    }

    [Fact]
    public async Task GetAgentWalletsForUserAsync_NonAgentUser_ReturnsEmpty()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAgentWalletsForUserAsync(adminUser.Id);

        result.Should().BeEmpty();
    }

    #endregion

    private PendingTransaction CreateTestPendingTransaction(PendingTransactionStatus status) => new()
    {
        CashSessionId = _testSession.Id,
        AgentId = _testAgent.Id,
        WalletId = _testWallet.Id,
        Type = PendingTransactionType.OutboundTransferFailed,
        Status = status,
        Amount = 50_000m,
        Currency = "UGX",
        Notes = "Test pending transaction",
        RecordedByUserId = _testUser.Id,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
