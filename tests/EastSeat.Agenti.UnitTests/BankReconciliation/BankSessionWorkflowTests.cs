using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankRuns;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankSessionWorkflowTests
{
    [Fact]
    public async Task NextOpening_IsAllowedWhileOlderClosingAwaitsApproval()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.WorkflowEnabled = true;
        fixture.Account.ImportProfileVerified = true;
        fixture.Session.SessionDate = fixture.Session.SessionDate.AddDays(-1);
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Session.OperationalClosedAt = BankingTestContext.Now.AddDays(-1);
        fixture.Db.CashCounts.Add(new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            CountDate = fixture.Session.SessionDate, Status = CashCountStatus.PendingApproval,
            TotalAmount = 1_100_000m, CreatedAt = BankingTestContext.Now.AddDays(-1)
        });
        await fixture.Db.SaveChangesAsync();
        var clock = new BusinessClock(new FixedTimeProvider());
        var workflow = new BankSessionWorkflow(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            new SessionBalanceService(fixture.Db, clock), Mock.Of<ISessionSettlementService>());

        var current = await workflow.GetCurrentSessionAsync(fixture.User.Id);
        var form = await workflow.InitializeFormAsync(fixture.User.Id, true);
        form.WalletEntries.Single(w => w.WalletId == fixture.CashWallet.Id).CountedAmount = 100_000m;
        form.WalletEntries.Single(w => w.WalletId == fixture.BankWallet.Id).CountedAmount = 1_000_000m;
        Assert.All(form.WalletEntries, wallet => Assert.Null(wallet.ObservedAt));
        foreach (var wallet in form.WalletEntries) wallet.ObservedAt = clock.UtcNow;
        var result = await workflow.SaveAsync(fixture.User.Id, form);

        Assert.True(current.HasPendingApproval);
        Assert.True(current.CanPerformOpeningCount);
        Assert.True(result.Success);
        Assert.Equal(2, await fixture.Db.CashSessions.CountAsync());
        Assert.Equal(CashCountStatus.PendingApproval,
            (await fixture.Db.CashCounts.SingleAsync(c => !c.IsOpening)).Status);
    }

    [Fact]
    public async Task BankRun_RefreshesFromCustomerTallyAndReplaysWithoutAnotherTransfer()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var clock = new BusinessClock(new FixedTimeProvider());
        var access = new BankingAccessService(fixture.Db, clock);
        var balances = new SessionBalanceService(fixture.Db, clock);
        var customerService = new CustomerTransactionService(fixture.Db, access, clock);
        await customerService.CaptureAsync(fixture.User.Id, new CustomerTransactionForm
        {
            CommandId = Guid.NewGuid(), CashSessionId = fixture.Session.Id, BankAccountId = fixture.Account.Id,
            CashWalletId = fixture.CashWallet.Id, Kind = CustomerTransactionKind.Deposit,
            Amount = 500_000m, CustomerIdentifier = "SYNTHETIC-CUSTOMER", BankReference = "CUSTOMER-500",
            OccurredAt = BankingTestContext.Now
        });
        var bankRunService = new BankRunService(fixture.Db, Mock.Of<INotificationService>(), balances, clock, access);
        var form = new BankRunFormModel
        {
            CommandId = Guid.NewGuid(), OccurredAt = BankingTestContext.Now.AddMinutes(1),
            Amount = 400_000m, ToWalletTypeId = fixture.BankWallet.WalletTypeId, ReceiptNumber = "RUN-400"
        };

        var result = await bankRunService.RecordBankRunAsync(fixture.User.Id, form);
        var replay = await bankRunService.RecordBankRunAsync(fixture.User.Id, form);

        Assert.True(result.Success);
        Assert.Equal(result.BankRunId, replay.BankRunId);
        Assert.Equal(200_000m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
        Assert.Equal(900_000m, (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance);
        Assert.Equal(1, await fixture.Db.BankRuns.CountAsync());
    }

    [Fact]
    public async Task FrozenClosing_IsReadableButCannotBeEdited()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var closing = new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            CountDate = fixture.Session.SessionDate, Status = CashCountStatus.PendingApproval,
            TotalAmount = 1_100_000m, CreatedAt = BankingTestContext.Now,
            Details =
            [
                new() { WalletId = fixture.CashWallet.Id, Amount = 100_000m, ObservedAt = BankingTestContext.Now },
                new() { WalletId = fixture.BankWallet.Id, Amount = 1_000_000m, ObservedAt = BankingTestContext.Now }
            ]
        };
        fixture.Db.CashCounts.Add(closing);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.CashCountSnapshots.Add(new CashCountSnapshot
        {
            CashCountId = closing.Id, TotalAmount = closing.TotalAmount,
            CapturedByUserId = fixture.User.Id, CapturedAt = BankingTestContext.Now,
            WalletsJson = CountSnapshotCodec.Serialize(
            [
                new(fixture.CashWallet.Id, "cash", WalletTypeEnum.Cash, "UGX", 100_000m, BankingTestContext.Now, null),
                new(fixture.BankWallet.Id, "bank", WalletTypeEnum.Bank, "UGX", 1_000_000m, BankingTestContext.Now, fixture.Account.Id)
            ])
        });
        fixture.Session.OperationalClosedAt = BankingTestContext.Now;
        fixture.Session.Status = CashSessionStatus.Closed;
        await fixture.Db.SaveChangesAsync();
        var clock = new BusinessClock(new FixedTimeProvider());
        var workflow = new BankSessionWorkflow(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            new SessionBalanceService(fixture.Db, clock), Mock.Of<ISessionSettlementService>());

        var form = await workflow.GetFormAsync(fixture.User.Id, closing.Id);

        Assert.NotNull(form);
        Assert.True(form.IsReadOnly);
        form.WalletEntries[0].CountedAmount++;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => workflow.SaveAsync(fixture.User.Id, form));
        Assert.Equal(1_100_000m, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.TotalAmount);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => BankingTestContext.Now.AddHours(1);
    }

    [Fact]
    public async Task ModernWalletAdjustment_UsesProjectionAndReviewsWithinItsBranchOnly()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var clock = new BusinessClock(new FixedTimeProvider());
        var access = new BankingAccessService(fixture.Db, clock);
        var balances = new SessionBalanceService(fixture.Db, clock);
        var customers = new CustomerTransactionService(fixture.Db, access, clock);
        await customers.CaptureAsync(fixture.User.Id, new CustomerTransactionForm
        {
            CommandId = Guid.NewGuid(), CashSessionId = fixture.Session.Id, BankAccountId = fixture.Account.Id,
            CashWalletId = fixture.CashWallet.Id, Kind = CustomerTransactionKind.Deposit, Amount = 500_000m,
            CustomerIdentifier = "000123", BankReference = "CASH-RECEIPT", OccurredAt = BankingTestContext.Now
        });
        var notifications = new Mock<INotificationService>();
        var service = new WalletAdjustmentService(fixture.Db, notifications.Object, balances, clock, access);
        var result = await service.RecordAdjustmentAsync(fixture.User.Id, new WalletAdjustmentFormModel
        {
            CommandId = Guid.NewGuid(), WalletId = fixture.CashWallet.Id, Amount = 200_000m,
            Reason = WalletAdjustmentReason.OwnerPayment, OccurredAt = BankingTestContext.Now.AddMinutes(1)
        });
        Assert.True(result.Success, result.ErrorMessage);
        var foreignBranch = new Branch { Name = "Foreign test branch", CreatedAt = BankingTestContext.Now };
        fixture.Db.Branches.Add(foreignBranch);
        await fixture.Db.SaveChangesAsync();
        var foreignAdmin = new ApplicationUser
        {
            Id = "foreign-reviewer", UserName = "foreign-reviewer", Role = UserRole.Admin,
            BranchId = foreignBranch.Id, IsActive = true
        };
        fixture.Db.Users.Add(foreignAdmin);
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveAdjustmentAsync(foreignAdmin.Id, result.AdjustmentId!.Value));
        var approved = await service.ApproveAdjustmentAsync(fixture.Admin.Id, result.AdjustmentId!.Value);
        var replay = await service.ApproveAdjustmentAsync(fixture.Admin.Id, result.AdjustmentId.Value);

        Assert.True(approved.Success);
        Assert.True(replay.Success);
        Assert.Equal(400_000m, (await balances.GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id))
            .Single(p => p.WalletId == fixture.CashWallet.Id).ExpectedBalance);
        Assert.Equal(100_000m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
        notifications.VerifyNoOtherCalls();
    }
}
