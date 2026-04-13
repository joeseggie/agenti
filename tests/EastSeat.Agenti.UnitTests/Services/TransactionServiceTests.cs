using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.Transactions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "Transactions")]
public class TransactionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly TransactionService _sut;
    private readonly Branch _testBranch;
    private readonly ApplicationUser _testUser;
    private readonly Agent _testAgent;
    private readonly Wallet _fromWallet;
    private readonly Wallet _toWallet;
    private readonly CashSession _testSession;
    private readonly Transaction _testTransaction;

    public TransactionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
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

        var walletType = new WalletType
        {
            Id = 1,
            Name = "Cash",
            Type = WalletTypeEnum.Cash,
            IsActive = true,
            SupportsDenominations = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.WalletTypes.Add(walletType);

        _fromWallet = WalletBuilder.Default()
            .WithId(1)
            .WithAgentId(_testAgent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithBalance(500_000m)
            .WithName("From Wallet")
            .Build();
        _dbContext.Wallets.Add(_fromWallet);

        _toWallet = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(_testAgent.Id)
            .WithWalletTypeId(walletType.Id)
            .WithBalance(0m)
            .WithName("To Wallet")
            .Build();
        _dbContext.Wallets.Add(_toWallet);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _testSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithSessionDate(today)
            .AsOpen()
            .Build();
        _dbContext.CashSessions.Add(_testSession);

        _testTransaction = new Transaction
        {
            Id = 1,
            CashSessionId = _testSession.Id,
            FromWalletId = _fromWallet.Id,
            ToWalletId = _toWallet.Id,
            Type = TransactionType.Transfer,
            Amount = 100_000m,
            Currency = "UGX",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Transactions.Add(_testTransaction);

        _dbContext.SaveChanges();

        _sut = new TransactionService(_dbContext, _notificationServiceMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    #region GetTransactionsForAgentAsync Tests

    [Fact]
    public async Task GetTransactionsForAgentAsync_WithActiveSession_ReturnsTransactions()
    {
        var result = await _sut.GetTransactionsForAgentAsync(_testUser.Id);

        result.Should().HaveCount(1);
        result[0].Amount.Should().Be(100_000m);
        result[0].Type.Should().Be(TransactionType.Transfer);
    }

    [Fact]
    public async Task GetTransactionsForAgentAsync_WithNonAgentUser_ReturnsEmpty()
    {
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTransactionsForAgentAsync(nonAgentUser.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsForAgentAsync_WithNoActiveSession_ReturnsEmpty()
    {
        // Remove session to simulate no active session
        _dbContext.CashSessions.Remove(_testSession);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTransactionsForAgentAsync(_testUser.Id);

        result.Should().BeEmpty();
    }

    #endregion

    #region FlagTransactionAsync Tests

    [Fact]
    public async Task FlagTransactionAsync_WithValidData_CreatesFlag()
    {
        var form = new FlagTransactionFormModel
        {
            TransactionId = _testTransaction.Id,
            Reason = "Customer reported they did not receive the funds for this transfer."
        };

        var result = await _sut.FlagTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        result.FlagId.Should().BeGreaterThan(0);

        var flag = await _dbContext.TransactionFlags.FirstOrDefaultAsync();
        flag.Should().NotBeNull();
        flag!.TransactionId.Should().Be(_testTransaction.Id);
        flag.FlaggedByUserId.Should().Be(_testUser.Id);
        flag.Status.Should().Be(TransactionFlagStatus.PendingReview);
        flag.Reason.Should().Be(form.Reason);
    }

    [Fact]
    public async Task FlagTransactionAsync_WithReasonTooShort_ReturnsError()
    {
        var form = new FlagTransactionFormModel
        {
            TransactionId = _testTransaction.Id,
            Reason = "Short"
        };

        var result = await _sut.FlagTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("10 characters");
    }

    [Fact]
    public async Task FlagTransactionAsync_WithNonAgentUser_ReturnsError()
    {
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("notagent@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var form = new FlagTransactionFormModel
        {
            TransactionId = _testTransaction.Id,
            Reason = "This transaction was made in error during a customer visit."
        };

        var result = await _sut.FlagTransactionAsync(nonAgentUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured as an agent");
    }

    [Fact]
    public async Task FlagTransactionAsync_WithNonExistentTransaction_ReturnsError()
    {
        var form = new FlagTransactionFormModel
        {
            TransactionId = 99999,
            Reason = "This transaction was made in error during a customer visit."
        };

        var result = await _sut.FlagTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task FlagTransactionAsync_WhenAlreadyFlagged_ReturnsError()
    {
        // Create an existing active flag
        var existingFlag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Previous flag that is still pending review by supervisor.",
            Status = TransactionFlagStatus.PendingReview
        };
        _dbContext.TransactionFlags.Add(existingFlag);
        await _dbContext.SaveChangesAsync();

        var form = new FlagTransactionFormModel
        {
            TransactionId = _testTransaction.Id,
            Reason = "Trying to flag the same transaction again after first report."
        };

        var result = await _sut.FlagTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already has an active flag");
    }

    [Fact]
    public async Task FlagTransactionAsync_WhenPreviousFlagDismissed_AllowsNewFlag()
    {
        // Create a previously dismissed flag
        var dismissedFlag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Reason = "Earlier issue that was investigated and dismissed.",
            Status = TransactionFlagStatus.Dismissed,
            ResolvedByUserId = _testUser.Id,
            ResolvedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _dbContext.TransactionFlags.Add(dismissedFlag);
        await _dbContext.SaveChangesAsync();

        var form = new FlagTransactionFormModel
        {
            TransactionId = _testTransaction.Id,
            Reason = "New error discovered after the previous flag was dismissed."
        };

        var result = await _sut.FlagTransactionAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task FlagTransactionAsync_NotifiesBranchAdmins()
    {
        var form = new FlagTransactionFormModel
        {
            TransactionId = _testTransaction.Id,
            Reason = "Customer reported they did not receive the funds for this transfer."
        };

        await _sut.FlagTransactionAsync(_testUser.Id, form);

        _notificationServiceMock.Verify(
            n => n.NotifyBranchAdminsAsync(
                _testBranch.Id,
                It.IsAny<string>(),
                It.IsAny<string>(),
                NotificationType.TransactionFlagged,
                "/transaction-flags"),
            Times.Once);
    }

    #endregion

    #region StartInvestigationAsync Tests

    [Fact]
    public async Task StartInvestigationAsync_ByAdmin_UpdatesStatus()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);

        var flag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Customer reported they did not receive the funds for this transfer.",
            Status = TransactionFlagStatus.PendingReview
        };
        _dbContext.TransactionFlags.Add(flag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.StartInvestigationAsync(adminUser.Id, flag.Id);

        result.Success.Should().BeTrue();

        var updated = await _dbContext.TransactionFlags.FindAsync(flag.Id);
        updated!.Status.Should().Be(TransactionFlagStatus.UnderInvestigation);
    }

    [Fact]
    public async Task StartInvestigationAsync_ByAgent_ReturnsError()
    {
        var flag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Customer reported they did not receive the funds for this transfer.",
            Status = TransactionFlagStatus.PendingReview
        };
        _dbContext.TransactionFlags.Add(flag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.StartInvestigationAsync(_testUser.Id, flag.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only administrators or supervisors");
    }

    #endregion

    #region ResolveFlagAsync / DismissFlagAsync Tests

    [Fact]
    public async Task ResolveFlagAsync_ByAdmin_WithValidNotes_ResolvesFlag()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);

        var flag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Customer reported they did not receive the funds for this transfer.",
            Status = TransactionFlagStatus.UnderInvestigation
        };
        _dbContext.TransactionFlags.Add(flag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.ResolveFlagAsync(adminUser.Id, flag.Id,
            "Investigated and confirmed the funds were received by the customer.");

        result.Success.Should().BeTrue();

        var updated = await _dbContext.TransactionFlags.FindAsync(flag.Id);
        updated!.Status.Should().Be(TransactionFlagStatus.Resolved);
        updated.ResolvedByUserId.Should().Be(adminUser.Id);
        updated.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DismissFlagAsync_ByAdmin_WithValidNotes_DismissesFlag()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);

        var flag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Customer reported they did not receive the funds for this transfer.",
            Status = TransactionFlagStatus.PendingReview
        };
        _dbContext.TransactionFlags.Add(flag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.DismissFlagAsync(adminUser.Id, flag.Id,
            "Verified the transaction was correct; customer was mistaken about recipient.");

        result.Success.Should().BeTrue();

        var updated = await _dbContext.TransactionFlags.FindAsync(flag.Id);
        updated!.Status.Should().Be(TransactionFlagStatus.Dismissed);
    }

    [Fact]
    public async Task ResolveFlagAsync_WithShortNotes_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);

        var flag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Customer reported they did not receive the funds for this transfer.",
            Status = TransactionFlagStatus.UnderInvestigation
        };
        _dbContext.TransactionFlags.Add(flag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.ResolveFlagAsync(adminUser.Id, flag.Id, "Too short");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("10 characters");
    }

    [Fact]
    public async Task ResolveFlagAsync_OnAlreadyResolvedFlag_ReturnsError()
    {
        var adminUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(adminUser);

        var flag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Customer reported they did not receive the funds for this transfer.",
            Status = TransactionFlagStatus.Resolved,
            ResolvedByUserId = adminUser.Id,
            ResolvedAt = DateTimeOffset.UtcNow
        };
        _dbContext.TransactionFlags.Add(flag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.ResolveFlagAsync(adminUser.Id, flag.Id,
            "Attempting to resolve an already resolved flag.");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already been resolved or dismissed");
    }

    #endregion

    #region GetActiveFlagsForBranchAsync Tests

    [Fact]
    public async Task GetActiveFlagsForBranchAsync_ReturnsOnlyActiveFlags()
    {
        var resolvedFlag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow.AddHours(-3),
            Reason = "Earlier issue that was already resolved by the supervisor.",
            Status = TransactionFlagStatus.Resolved,
            ResolvedByUserId = _testUser.Id,
            ResolvedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var pendingFlag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "New issue found today with the customer receipt for this transfer.",
            Status = TransactionFlagStatus.PendingReview
        };

        _dbContext.TransactionFlags.AddRange(resolvedFlag, pendingFlag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetActiveFlagsForBranchAsync(_testBranch.Id);

        result.Should().HaveCount(1);
        result[0].Status.Should().Be(TransactionFlagStatus.PendingReview);
    }

    [Fact]
    public async Task GetAllFlagsForBranchAsync_ReturnsAllFlags()
    {
        var flag1 = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Reason = "Earlier issue that was resolved after investigation confirmed error.",
            Status = TransactionFlagStatus.Resolved,
            ResolvedByUserId = _testUser.Id,
            ResolvedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var flag2 = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "New issue found today with the customer receipt for this transfer.",
            Status = TransactionFlagStatus.PendingReview
        };

        _dbContext.TransactionFlags.AddRange(flag1, flag2);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAllFlagsForBranchAsync(_testBranch.Id);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllFlagsForBranchAsync_WithStatusFilter_ReturnsFilteredFlags()
    {
        var pendingFlag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow,
            Reason = "Pending issue requiring supervisor review of this transfer.",
            Status = TransactionFlagStatus.PendingReview
        };

        var investigatingFlag = new TransactionFlag
        {
            TransactionId = _testTransaction.Id,
            FlaggedByUserId = _testUser.Id,
            FlaggedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Reason = "Issue already picked up by supervisor for investigation.",
            Status = TransactionFlagStatus.UnderInvestigation
        };

        _dbContext.TransactionFlags.AddRange(pendingFlag, investigatingFlag);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAllFlagsForBranchAsync(_testBranch.Id, "PendingReview");

        result.Should().HaveCount(1);
        result[0].Status.Should().Be(TransactionFlagStatus.PendingReview);
    }

    #endregion
}
