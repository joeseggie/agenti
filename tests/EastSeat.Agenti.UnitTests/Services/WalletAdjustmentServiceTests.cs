using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace EastSeat.Agenti.UnitTests.Services;

[Trait("Category", "Unit")]
[Trait("Feature", "WalletAdjustment")]
public class WalletAdjustmentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly WalletAdjustmentService _sut;
    private readonly Branch _testBranch;
    private readonly ApplicationUser _testUser;
    private readonly Agent _testAgent;
    private readonly WalletType _cashWalletType;
    private readonly Wallet _testWallet;
    private readonly CashSession _testSession;

    public WalletAdjustmentServiceTests()
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

        _sut = new WalletAdjustmentService(_dbContext, _notificationServiceMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
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

    #region RecordAdjustmentAsync Tests

    [Fact]
    public async Task RecordAdjustmentAsync_WithValidData_ReturnsSuccess()
    {
        SeedApprovedOpeningCount();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 50_000m,
            Notes = "Bank counted less"
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
        result.AdjustmentId.Should().BeGreaterThan(0);

        var saved = await _dbContext.WalletAdjustments.FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.Amount.Should().Be(50_000m);
        saved.Reason.Should().Be(WalletAdjustmentReason.BankShortage);
        saved.AgentId.Should().Be(_testAgent.Id);
        saved.CashSessionId.Should().Be(_testSession.Id);
    }

    [Fact]
    public async Task RecordAdjustmentAsync_WithNoActiveSession_ReturnsError()
    {
        // Remove the session to simulate no active session
        _dbContext.CashSessions.Remove(_testSession);
        await _dbContext.SaveChangesAsync();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 50_000m
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active cash session");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_WithoutApprovedOpening_ReturnsError()
    {
        // Don't seed approved opening count

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 50_000m
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Opening cash count must be approved");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_WithZeroAmount_ReturnsError()
    {
        SeedApprovedOpeningCount();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 0m
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than zero");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_ExceedingBalance_ReturnsError()
    {
        SeedApprovedOpeningCount();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 600_000m // Exceeds wallet balance of 500,000
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_OtherReasonWithoutNotes_ReturnsError()
    {
        SeedApprovedOpeningCount();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.Other,
            Amount = 50_000m,
            Notes = "short" // Less than 10 characters
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Notes are required");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_OtherReasonWithValidNotes_ReturnsSuccess()
    {
        SeedApprovedOpeningCount();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.Other,
            Amount = 50_000m,
            Notes = "Customer brought counterfeit notes that were confiscated"
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RecordAdjustmentAsync_WithWalletNotBelongingToAgent_ReturnsError()
    {
        SeedApprovedOpeningCount();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = 999, // Non-existent wallet
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 50_000m
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Wallet not found");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_WithClosingCountPending_ReturnsError()
    {
        SeedApprovedOpeningCount();

        // Add a closing count in PendingApproval
        var closingCount = CashCountBuilder.Default()
            .WithId(2)
            .WithCashSessionId(_testSession.Id)
            .WithAgentId(_testAgent.Id)
            .WithIsOpening(false)
            .WithStatus(CashCountStatus.PendingApproval)
            .WithTotalAmount(500_000m)
            .Build();
        _dbContext.CashCounts.Add(closingCount);
        await _dbContext.SaveChangesAsync();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 50_000m
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("closing count");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_SendsNotificationToBranchAdmins()
    {
        SeedApprovedOpeningCount();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.FakeNotes,
            Amount = 20_000m
        };

        await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        _notificationServiceMock.Verify(
            x => x.NotifyBranchAdminsAsync(
                _testBranch.Id,
                It.IsAny<string>(),
                It.Is<string>(msg => msg.Contains("20,000") && msg.Contains("fake notes")),
                NotificationType.WalletAdjustmentRecorded,
                It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordAdjustmentAsync_WithNonAgentUser_ReturnsError()
    {
        var nonAgentUser = UserBuilder.Default()
            .WithEmail("admin@test.com")
            .WithRole(UserRole.Admin)
            .Build();
        _dbContext.Users.Add(nonAgentUser);
        await _dbContext.SaveChangesAsync();

        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 50_000m
        };

        var result = await _sut.RecordAdjustmentAsync(nonAgentUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured as an agent");
    }

    #endregion

    #region GetWalletAdjustmentTotalsAsync Tests

    [Fact]
    public async Task GetWalletAdjustmentTotalsAsync_WithNoAdjustments_ReturnsEmptyDictionary()
    {
        var result = await _sut.GetWalletAdjustmentTotalsAsync(_testSession.Id, _testAgent.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWalletAdjustmentTotalsAsync_WithMultipleAdjustments_ReturnsSumPerWallet()
    {
        _dbContext.WalletAdjustments.AddRange(
            new WalletAdjustment
            {
                CashSessionId = _testSession.Id,
                WalletId = _testWallet.Id,
                AgentId = _testAgent.Id,
                Reason = WalletAdjustmentReason.BankShortage,
                Amount = 30_000m,
                RecordedByUserId = _testUser.Id,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new WalletAdjustment
            {
                CashSessionId = _testSession.Id,
                WalletId = _testWallet.Id,
                AgentId = _testAgent.Id,
                Reason = WalletAdjustmentReason.FakeNotes,
                Amount = 20_000m,
                RecordedByUserId = _testUser.Id,
                CreatedAt = DateTimeOffset.UtcNow
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetWalletAdjustmentTotalsAsync(_testSession.Id, _testAgent.Id);

        result.Should().ContainKey(_testWallet.Id);
        result[_testWallet.Id].Should().Be(50_000m);
    }

    #endregion

    #region GetAdjustmentsForSessionAsync Tests

    [Fact]
    public async Task GetAdjustmentsForSessionAsync_WithNoAdjustments_ReturnsEmptyList()
    {
        var result = await _sut.GetAdjustmentsForSessionAsync(_testSession.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdjustmentsForSessionAsync_WithAdjustments_ReturnsDtoList()
    {
        _dbContext.WalletAdjustments.Add(new WalletAdjustment
        {
            CashSessionId = _testSession.Id,
            WalletId = _testWallet.Id,
            AgentId = _testAgent.Id,
            Reason = WalletAdjustmentReason.OwnerPayment,
            Amount = 100_000m,
            Notes = "Owner requested payment",
            RecordedByUserId = _testUser.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAdjustmentsForSessionAsync(_testSession.Id);

        result.Should().HaveCount(1);
        result[0].Amount.Should().Be(100_000m);
        result[0].Reason.Should().Be(WalletAdjustmentReason.OwnerPayment);
        result[0].ReasonDisplay.Should().Be("Owner Payment Request");
    }

    [Fact]
    public async Task GetAdjustmentsForSessionAsync_FilteredByAgent_ReturnsOnlyAgentAdjustments()
    {
        // Create a second agent
        var secondUser = UserBuilder.Default()
            .WithEmail("agent2@test.com")
            .WithRole(UserRole.Agent)
            .WithBranchId(_testBranch.Id)
            .Build();
        _dbContext.Users.Add(secondUser);

        var secondAgent = AgentBuilder.Default()
            .WithId(2)
            .WithUserId(secondUser.Id)
            .WithBranchId(_testBranch.Id)
            .WithCode("AGT-002")
            .Build();
        _dbContext.Agents.Add(secondAgent);

        var secondWallet = WalletBuilder.Default()
            .WithId(2)
            .WithAgentId(secondAgent.Id)
            .WithWalletTypeId(_cashWalletType.Id)
            .WithBalance(300_000m)
            .Build();
        _dbContext.Wallets.Add(secondWallet);

        _dbContext.WalletAdjustments.AddRange(
            new WalletAdjustment
            {
                CashSessionId = _testSession.Id,
                WalletId = _testWallet.Id,
                AgentId = _testAgent.Id,
                Reason = WalletAdjustmentReason.BankShortage,
                Amount = 50_000m,
                RecordedByUserId = _testUser.Id,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new WalletAdjustment
            {
                CashSessionId = _testSession.Id,
                WalletId = secondWallet.Id,
                AgentId = secondAgent.Id,
                Reason = WalletAdjustmentReason.FakeNotes,
                Amount = 10_000m,
                RecordedByUserId = secondUser.Id,
                CreatedAt = DateTimeOffset.UtcNow
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAdjustmentsForSessionAsync(_testSession.Id, _testAgent.Id);

        result.Should().HaveCount(1);
        result[0].AgentCode.Should().Be(_testAgent.Code);
    }

    #endregion

    #region Cumulative Adjustment Balance Check

    [Fact]
    public async Task RecordAdjustmentAsync_CumulativeAmountExceedsBalance_ReturnsError()
    {
        SeedApprovedOpeningCount();

        // First adjustment: 400,000 of 500,000 balance
        _dbContext.WalletAdjustments.Add(new WalletAdjustment
        {
            CashSessionId = _testSession.Id,
            WalletId = _testWallet.Id,
            AgentId = _testAgent.Id,
            Reason = WalletAdjustmentReason.BankShortage,
            Amount = 400_000m,
            RecordedByUserId = _testUser.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Second adjustment: tries to take 200,000 more (only 100,000 effective balance left)
        var form = new WalletAdjustmentFormModel
        {
            WalletId = _testWallet.Id,
            Reason = WalletAdjustmentReason.OwnerPayment,
            Amount = 200_000m
        };

        var result = await _sut.RecordAdjustmentAsync(_testUser.Id, form);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds");
    }

    #endregion
}
