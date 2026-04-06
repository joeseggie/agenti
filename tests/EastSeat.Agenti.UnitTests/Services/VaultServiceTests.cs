using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.Vaults;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "VaultOperations")]
public class VaultServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly VaultService _sut;
    private readonly Branch _testBranch;
    private readonly Vault _testVault;
    private readonly ApplicationUser _testUser;
    private readonly ApplicationUser _testAdmin;

    public VaultServiceTests()
    {
        // Setup in-memory database
        var services = new ServiceCollection();
        services.AddDbContextFactory<ApplicationDbContext>(opts =>
            opts.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        _serviceProvider = services.BuildServiceProvider();
        _dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        _dbContext = _dbContextFactory.CreateDbContext();

        // Seed required data
        _testBranch = new Branch
        {
            Id = 1,
            Name = "Test Branch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Branches.Add(_testBranch);

        _testVault = VaultBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .WithCurrentBalance(10000m)
            .Build();
        _dbContext.Vaults.Add(_testVault);

        _testUser = UserBuilder.Default()
            .WithEmail("user@test.com")
            .WithRole(UserRole.Agent)
            .Build();

        _testAdmin = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();

        _dbContext.Users.AddRange(_testUser, _testAdmin);
        _dbContext.SaveChanges();

        _sut = new VaultService(_dbContext);
    }

    private NotificationService CreateNotificationService()
        => new NotificationService(_dbContextFactory);

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    #region GetVaultAsync Tests

    [Fact]
    public async Task GetVaultAsync_WithExistingVault_ReturnsVaultDto()
    {
        // Act
        var result = await _sut.GetVaultAsync(_testBranch.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(_testVault.Id);
        result.BranchId.Should().Be(_testBranch.Id);
        result.BranchName.Should().Be(_testBranch.Name);
        result.CurrentBalance.Should().Be(10000m);
    }

    [Fact]
    public async Task GetVaultAsync_WithNonExistentBranch_ReturnsNull()
    {
        // Act
        var result = await _sut.GetVaultAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetRecentTransactionsAsync Tests

    [Fact(Skip = "Navigation property projection not supported in-memory. Tested in integration tests.")]
    public async Task GetRecentTransactionsAsync_ReturnsOrderedByCreatedAtDescending()
    {
        // Arrange
        var transaction1 = VaultTransactionBuilder.Default()
            .WithId(1)
            .WithVaultId(_testVault.Id)
            .WithAmount(100m)
            .WithCreatedAt(DateTimeOffset.UtcNow.AddHours(-2))
            .WithCreatedByUserId(_testUser.Id)
            .Build();

        var transaction2 = VaultTransactionBuilder.Default()
            .WithId(2)
            .WithVaultId(_testVault.Id)
            .WithAmount(200m)
            .WithCreatedAt(DateTimeOffset.UtcNow.AddHours(-1))
            .WithCreatedByUserId(_testUser.Id)
            .Build();

        _dbContext.VaultTransactions.AddRange(transaction1, transaction2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRecentTransactionsAsync(_testBranch.Id);

        // Assert
        result.Should().HaveCount(2);
        result[0].Amount.Should().Be(200m); // Most recent first
        result[1].Amount.Should().Be(100m);
    }

    [Fact(Skip = "Navigation property projection not supported in-memory. Tested in integration tests.")]
    public async Task GetRecentTransactionsAsync_ByDefault_ExcludesExpiredTransactions()
    {
        // Arrange
        var completedTransaction = VaultTransactionBuilder.Default()
            .WithId(1)
            .WithVaultId(_testVault.Id)
            .AsCompleted()
            .WithCreatedByUserId(_testUser.Id)
            .Build();

        var expiredTransaction = VaultTransactionBuilder.Default()
            .WithId(2)
            .WithVaultId(_testVault.Id)
            .AsExpired()
            .WithCreatedByUserId(_testUser.Id)
            .Build();

        _dbContext.VaultTransactions.AddRange(completedTransaction, expiredTransaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRecentTransactionsAsync(_testBranch.Id, includeExpired: false);

        // Assert
        result.Should().ContainSingle();
        result[0].Status.Should().Be(VaultTransactionStatus.Completed);
    }

    [Fact(Skip = "Navigation property projection not supported in-memory. Tested in integration tests.")]
    public async Task GetRecentTransactionsAsync_WithIncludeExpired_ReturnsAllTransactions()
    {
        // Arrange
        var completedTransaction = VaultTransactionBuilder.Default()
            .WithId(1)
            .WithVaultId(_testVault.Id)
            .AsCompleted()
            .WithCreatedByUserId(_testUser.Id)
            .Build();

        var expiredTransaction = VaultTransactionBuilder.Default()
            .WithId(2)
            .WithVaultId(_testVault.Id)
            .AsExpired()
            .WithCreatedByUserId(_testUser.Id)
            .Build();

        _dbContext.VaultTransactions.AddRange(completedTransaction, expiredTransaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRecentTransactionsAsync(_testBranch.Id, includeExpired: true);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact(Skip = "Navigation property projection not supported in-memory. Tested in integration tests.")]
    public async Task GetRecentTransactionsAsync_WithTakeParameter_LimitsResults()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            var transaction = VaultTransactionBuilder.Default()
                .WithId(i)
                .WithVaultId(_testVault.Id)
                .WithCreatedAt(DateTimeOffset.UtcNow.AddHours(-i))
                .WithCreatedByUserId(_testUser.Id)
                .WithCreatedByUser(_testUser)
                .Build();
            _dbContext.VaultTransactions.Add(transaction);
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRecentTransactionsAsync(_testBranch.Id, take: 5);

        // Assert
        result.Should().HaveCount(5);
    }

    #endregion

    #region WithdrawForSessionAsync Tests

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task WithdrawForSessionAsync_WithSufficientBalance_SucceedsAndUpdatesVault()
    {
        // Arrange
        var cashSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.CashSessions.Add(cashSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.WithdrawForSessionAsync(
            cashSession.Id,
            _testBranch.Id,
            500m,
            _testUser.Id,
            ensureTransaction: false); // Bypass transaction for in-memory testing

        // Assert
        result.Success.Should().BeTrue();
        result.TransactionId.Should().BeGreaterThan(0);

        var updatedVault = await _dbContext.Vaults.FindAsync(_testVault.Id);
        updatedVault!.CurrentBalance.Should().Be(9500m); // 10000 - 500

        var transaction = await _dbContext.VaultTransactions.FindAsync(result.TransactionId);
        transaction.Should().NotBeNull();
        transaction!.Type.Should().Be(VaultTransactionType.Opening);
        transaction.Status.Should().Be(VaultTransactionStatus.Completed);
        transaction.Amount.Should().Be(500m);
        transaction.BalanceAfter.Should().Be(9500m);
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task WithdrawForSessionAsync_WithInsufficientBalance_ReturnsError()
    {
        // Arrange
        var cashSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.CashSessions.Add(cashSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.WithdrawForSessionAsync(
            cashSession.Id,
            _testBranch.Id,
            15000m, // More than vault balance
            _testUser.Id,
            ensureTransaction: false);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Insufficient balance");
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task WithdrawForSessionAsync_WithZeroAmount_ReturnsError()
    {
        // Arrange
        var cashSession = CashSessionBuilder.Default().Build();
        _dbContext.CashSessions.Add(cashSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.WithdrawForSessionAsync(
            cashSession.Id,
            _testBranch.Id,
            0m,
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task WithdrawForSessionAsync_WithNegativeAmount_ReturnsError()
    {
        // Arrange
        var cashSession = CashSessionBuilder.Default().Build();
        _dbContext.CashSessions.Add(cashSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.WithdrawForSessionAsync(
            cashSession.Id,
            _testBranch.Id,
            -100m,
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    #endregion

    #region DepositForSessionAsync Tests

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task DepositForSessionAsync_WithValidAmount_SucceedsAndUpdatesVault()
    {
        // Arrange
        var cashSession = CashSessionBuilder.Default()
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.CashSessions.Add(cashSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DepositForSessionAsync(
            cashSession.Id,
            _testBranch.Id,
            1500m,
            _testUser.Id,
            ensureTransaction: false);

        // Assert
        result.Success.Should().BeTrue();
        result.TransactionId.Should().BeGreaterThan(0);

        var updatedVault = await _dbContext.Vaults.FindAsync(_testVault.Id);
        updatedVault!.CurrentBalance.Should().Be(11500m); // 10000 + 1500

        var transaction = await _dbContext.VaultTransactions.FindAsync(result.TransactionId);
        transaction.Should().NotBeNull();
        transaction!.Type.Should().Be(VaultTransactionType.Closing);
        transaction.Status.Should().Be(VaultTransactionStatus.Completed);
        transaction.Amount.Should().Be(1500m);
        transaction.BalanceAfter.Should().Be(11500m);
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task DepositForSessionAsync_WithZeroAmount_ReturnsError()
    {
        // Arrange
        var cashSession = CashSessionBuilder.Default().Build();
        _dbContext.CashSessions.Add(cashSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DepositForSessionAsync(
            cashSession.Id,
            _testBranch.Id,
            0m,
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task DepositForSessionAsync_WithNegativeAmount_ReturnsError()
    {
        // Arrange
        var cashSession = CashSessionBuilder.Default().Build();
        _dbContext.CashSessions.Add(cashSession);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DepositForSessionAsync(
            cashSession.Id,
            _testBranch.Id,
            -100m,
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    #endregion

    #region RequestManualAdjustmentAsync Tests

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithValidData_CreatesPendingTransaction()
    {
        // Act
        var result = await _sut.RequestManualAdjustmentAsync(
            _testBranch.Id,
            500m,
            isDeposit: true,
            "Need to deposit cash from safe",
            _testUser.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.TransactionId.Should().BeGreaterThan(0);

        var transaction = await _dbContext.VaultTransactions.FindAsync(result.TransactionId);
        transaction.Should().NotBeNull();
        transaction!.Type.Should().Be(VaultTransactionType.ManualDeposit);
        transaction.Status.Should().Be(VaultTransactionStatus.Pending);
        transaction.Amount.Should().Be(500m);
        transaction.CreatedByUserId.Should().Be(_testUser.Id);
        transaction.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(12), TimeSpan.FromSeconds(5));
        transaction.Notes.Should().Be("Need to deposit cash from safe");
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_AsWithdrawal_CreatesWithdrawalTransaction()
    {
        // Act
        var result = await _sut.RequestManualAdjustmentAsync(
            _testBranch.Id,
            300m,
            isDeposit: false,
            "Need to withdraw cash for emergency",
            _testUser.Id);

        // Assert
        result.Success.Should().BeTrue();

        var transaction = await _dbContext.VaultTransactions.FindAsync(result.TransactionId);
        transaction!.Type.Should().Be(VaultTransactionType.ManualWithdrawal);
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithZeroAmount_ReturnsError()
    {
        // Act
        var result = await _sut.RequestManualAdjustmentAsync(
            _testBranch.Id,
            0m,
            isDeposit: true,
            "Valid notes here",
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithShortNotes_ReturnsError()
    {
        // Act
        var result = await _sut.RequestManualAdjustmentAsync(
            _testBranch.Id,
            500m,
            isDeposit: true,
            "Short", // Less than 10 characters
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("minimum 10 characters");
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithEmptyNotes_ReturnsError()
    {
        // Act
        var result = await _sut.RequestManualAdjustmentAsync(
            _testBranch.Id,
            500m,
            isDeposit: true,
            "",
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("minimum 10 characters");
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithNonExistentBranch_ReturnsError()
    {
        // Act
        var result = await _sut.RequestManualAdjustmentAsync(
            999,
            500m,
            isDeposit: true,
            "Valid notes here",
            _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Branch not found");
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithNotificationService_SendsNotificationsToOtherAdmins()
    {
        // Arrange
        var secondAdmin = UserBuilder.Default()
            .WithEmail("admin2@test.com")
            .WithRole(UserRole.Admin)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(secondAdmin);
        await _dbContext.SaveChangesAsync();

        var notificationService = CreateNotificationService();
        var sutWithNotifications = new VaultService(_dbContext, null, notificationService);

        // Act
        var result = await sutWithNotifications.RequestManualAdjustmentAsync(
            _testBranch.Id,
            500m,
            isDeposit: true,
            "Deposit request from teller",
            _testAdmin.Id);

        // Assert
        result.Success.Should().BeTrue();

        var savedTransaction = await _dbContext.VaultTransactions.FindAsync(result.TransactionId);
        savedTransaction.Should().NotBeNull();

        var notifications = await _dbContext.Notifications.ToListAsync();
        notifications.Should().ContainSingle();
        notifications[0].RecipientUserId.Should().Be(secondAdmin.Id);
        notifications[0].SenderUserId.Should().Be(_testAdmin.Id);
        notifications[0].TransactionId.Should().Be(savedTransaction!.PublicId);
        notifications[0].Priority.Should().Be(NotificationPriority.High);
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithNotificationService_DoesNotNotifyRequester()
    {
        // Arrange
        var notificationService = CreateNotificationService();
        var sutWithNotifications = new VaultService(_dbContext, null, notificationService);

        // Act
        var result = await sutWithNotifications.RequestManualAdjustmentAsync(
            _testBranch.Id,
            300m,
            isDeposit: false,
            "Withdrawal request for operations",
            _testAdmin.Id);

        // Assert
        result.Success.Should().BeTrue();

        // _testAdmin made the request - should NOT be notified
        var notificationsToRequester = await _dbContext.Notifications
            .Where(n => n.RecipientUserId == _testAdmin.Id)
            .ToListAsync();
        notificationsToRequester.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestManualAdjustmentAsync_WithoutNotificationService_SucceedsWithoutNotifications()
    {
        // _sut does not have notificationService (uses default null)
        // Act
        var result = await _sut.RequestManualAdjustmentAsync(
            _testBranch.Id,
            500m,
            isDeposit: true,
            "Deposit without notifications",
            _testUser.Id);

        // Assert
        result.Success.Should().BeTrue();

        var notifications = await _dbContext.Notifications.ToListAsync();
        notifications.Should().BeEmpty();
    }

    #endregion

    #region ApproveManualAdjustmentAsync Tests

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task ApproveManualAdjustmentAsync_WithValidPendingDeposit_SucceedsAndUpdatesVault()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .WithType(VaultTransactionType.ManualDeposit)
            .AsPending()
            .WithAmount(500m)
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Test deposit")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(transaction.Id, _testAdmin.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedTransaction = await _dbContext.VaultTransactions.FindAsync(transaction.Id);
        updatedTransaction!.Status.Should().Be(VaultTransactionStatus.Completed);
        updatedTransaction.ApprovedByUserId.Should().Be(_testAdmin.Id);
        updatedTransaction.ApprovedAt.Should().NotBeNull();
        updatedTransaction.BalanceAfter.Should().Be(10500m);

        var updatedVault = await _dbContext.Vaults.FindAsync(_testVault.Id);
        updatedVault!.CurrentBalance.Should().Be(10500m); // 10000 + 500
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task ApproveManualAdjustmentAsync_WithValidPendingWithdrawal_SucceedsAndUpdatesVault()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .WithType(VaultTransactionType.ManualWithdrawal)
            .AsPending()
            .WithAmount(300m)
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Test withdrawal")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(transaction.Id, _testAdmin.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedVault = await _dbContext.Vaults.FindAsync(_testVault.Id);
        updatedVault!.CurrentBalance.Should().Be(9700m); // 10000 - 300
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task ApproveManualAdjustmentAsync_WithInsufficientBalanceForWithdrawal_ReturnsError()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .WithType(VaultTransactionType.ManualWithdrawal)
            .AsPending()
            .WithAmount(15000m) // More than vault balance
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Test withdrawal")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(transaction.Id, _testAdmin.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Insufficient vault balance");
    }

    [Fact]
    public async Task ApproveManualAdjustmentAsync_WhenCreatorTriesToApprove_ReturnsError()
    {
        // Arrange
        var adminCreator = UserBuilder.Default()
            .WithEmail("admin-creator@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(adminCreator);
        await _dbContext.SaveChangesAsync();

        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .AsPending()
            .WithCreatedByUserId(adminCreator.Id)
            .WithNotes("Test transaction")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(transaction.Id, adminCreator.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Creator cannot approve their own transaction");
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task ApproveManualAdjustmentAsync_WithNonAdminUser_ReturnsError()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .AsPending()
            .WithCreatedByUserId(_testAdmin.Id)
            .WithNotes("Test transaction")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(transaction.Id, _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only administrators can approve");
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task ApproveManualAdjustmentAsync_WithNonPendingTransaction_ReturnsError()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .AsCompleted()
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Test transaction")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(transaction.Id, _testAdmin.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not pending");
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task ApproveManualAdjustmentAsync_WithExpiredTransaction_MarksAsExpiredAndReturnsError()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .WithStatus(VaultTransactionStatus.Pending)
            .WithExpiresAt(DateTimeOffset.UtcNow.AddHours(-1)) // Already expired
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Test transaction")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(transaction.Id, _testAdmin.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("expired");

        var updatedTransaction = await _dbContext.VaultTransactions.FindAsync(transaction.Id);
        updatedTransaction!.Status.Should().Be(VaultTransactionStatus.Expired);
    }

    [Fact(Skip = "Uses FOR UPDATE raw SQL not supported in-memory. Tested in integration tests.")]
    public async Task ApproveManualAdjustmentAsync_WithNonExistentTransaction_ReturnsError()
    {
        // Act
        var result = await _sut.ApproveManualAdjustmentAsync(999, _testAdmin.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region RejectManualAdjustmentAsync Tests

    [Fact]
    public async Task RejectManualAdjustmentAsync_WithValidPendingTransaction_MarksAsRejected()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .AsPending()
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Test transaction")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.RejectManualAdjustmentAsync(transaction.Id, _testAdmin.Id);

        // Assert
        result.Success.Should().BeTrue();

        var updatedTransaction = await _dbContext.VaultTransactions.FindAsync(transaction.Id);
        updatedTransaction!.Status.Should().Be(VaultTransactionStatus.Rejected);
        updatedTransaction.ApprovedByUserId.Should().Be(_testAdmin.Id);
        updatedTransaction.ApprovedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectManualAdjustmentAsync_WithNonAdminUser_ReturnsError()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .AsPending()
            .WithCreatedByUserId(_testAdmin.Id)
            .WithNotes("Test transaction")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.RejectManualAdjustmentAsync(transaction.Id, _testUser.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only administrators can reject");
    }

    [Fact]
    public async Task RejectManualAdjustmentAsync_WithNonPendingTransaction_ReturnsError()
    {
        // Arrange
        var transaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .AsCompleted()
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Test transaction")
            .Build();
        _dbContext.VaultTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.RejectManualAdjustmentAsync(transaction.Id, _testAdmin.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not pending");
    }

    [Fact]
    public async Task RejectManualAdjustmentAsync_WithNonExistentTransaction_ReturnsError()
    {
        // Act
        var result = await _sut.RejectManualAdjustmentAsync(999, _testAdmin.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region ExpirePendingTransactionsAsync Tests

    [Fact]
    public async Task ExpirePendingTransactionsAsync_ExpiresOldPendingTransactions()
    {
        // Arrange
        var expiredTransaction1 = VaultTransactionBuilder.Default()
            .WithId(1)
            .WithVaultId(_testVault.Id)
            .WithStatus(VaultTransactionStatus.Pending)
            .WithExpiresAt(DateTimeOffset.UtcNow.AddHours(-1))
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Expired transaction 1")
            .Build();

        var expiredTransaction2 = VaultTransactionBuilder.Default()
            .WithId(2)
            .WithVaultId(_testVault.Id)
            .WithStatus(VaultTransactionStatus.Pending)
            .WithExpiresAt(DateTimeOffset.UtcNow.AddHours(-2))
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Expired transaction 2")
            .Build();

        var validTransaction = VaultTransactionBuilder.Default()
            .WithId(3)
            .WithVaultId(_testVault.Id)
            .WithStatus(VaultTransactionStatus.Pending)
            .WithExpiresAt(DateTimeOffset.UtcNow.AddHours(10))
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Valid transaction")
            .Build();

        _dbContext.VaultTransactions.AddRange(expiredTransaction1, expiredTransaction2, validTransaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.ExpirePendingTransactionsAsync();

        // Assert
        count.Should().Be(2);

        var updated1 = await _dbContext.VaultTransactions.FindAsync(1L);
        var updated2 = await _dbContext.VaultTransactions.FindAsync(2L);
        var updated3 = await _dbContext.VaultTransactions.FindAsync(3L);

        updated1!.Status.Should().Be(VaultTransactionStatus.Expired);
        updated2!.Status.Should().Be(VaultTransactionStatus.Expired);
        updated3!.Status.Should().Be(VaultTransactionStatus.Pending); // Still pending
    }

    [Fact]
    public async Task ExpirePendingTransactionsAsync_WithNoPendingTransactions_ReturnsZero()
    {
        // Act
        var count = await _sut.ExpirePendingTransactionsAsync();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task ExpirePendingTransactionsAsync_IgnoresAlreadyExpiredTransactions()
    {
        // Arrange
        var alreadyExpiredTransaction = VaultTransactionBuilder.Default()
            .WithVaultId(_testVault.Id)
            .AsExpired()
            .WithCreatedByUserId(_testUser.Id)
            .WithNotes("Already expired")
            .Build();
        _dbContext.VaultTransactions.Add(alreadyExpiredTransaction);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.ExpirePendingTransactionsAsync();

        // Assert
        count.Should().Be(0);
    }

    #endregion
}
