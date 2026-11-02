using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class SessionSettlementTests
{
    [Fact]
    public async Task Closing_ReplayDoesNotMoveFundsOrApproveTheClosing()
    {
        using var fixture = new BankingTestContext();
        var (service, vault) = CreateService(fixture);
        var opening = AddCount(fixture, true, 100_000m, 1_000_000m);
        var storedVault = await fixture.Db.Vaults.SingleAsync();
        storedVault.CurrentBalance = opening.TotalAmount;
        await fixture.Db.SaveChangesAsync();
        await service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id);
        var closing = AddCount(fixture, false, 100_000m, 1_000_000m);

        var first = await service.SubmitClosingAsync(fixture.User.Id, closing.Id);
        var replay = await service.SubmitClosingAsync(fixture.User.Id, closing.Id);

        Assert.False(first.AlreadySettled);
        Assert.True(replay.AlreadySettled);
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.Status);
        Assert.Equal(0m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
        Assert.Equal(0m, (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance);
        Assert.Equal(1_100_000m, (await fixture.Db.Vaults.SingleAsync()).CurrentBalance);
        Assert.NotNull((await fixture.Db.CashSessions.FindAsync(fixture.Session.Id))!.OperationalClosedAt);
        Assert.Equal(1, await fixture.Db.VaultTransactions.CountAsync(t => t.Type == VaultTransactionType.Closing));
        vault.Verify(v => v.DepositForSessionAsync(fixture.Session.Id, fixture.Account.BranchId,
            1_100_000m, fixture.User.Id, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(-2_000)]
    public async Task Opening_BankBridgeDoesNotCreatePhantomVaultFunds(decimal bankChange)
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        var oldSession = new CashSession
        {
            BranchId = fixture.Account.BranchId, SessionDate = fixture.Session.SessionDate.AddDays(-1),
            Status = CashSessionStatus.Closed, WorkflowVersion = SessionWorkflowVersion.BankReconciliation,
            OpenedAt = BankingTestContext.Now.AddDays(-1).AddHours(-1),
            OperationalClosedAt = BankingTestContext.Now.AddDays(-1)
        };
        fixture.Db.CashSessions.Add(oldSession);
        await fixture.Db.SaveChangesAsync();
        var previousClose = new CashCount
        {
            CashSessionId = oldSession.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            Status = CashCountStatus.PendingApproval, CountDate = oldSession.SessionDate,
            TotalAmount = 1_100_000m, CreatedAt = BankingTestContext.Now.AddDays(-1)
        };
        fixture.Db.CashCounts.Add(previousClose);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.CashCountSnapshots.Add(new CashCountSnapshot
        {
            CashCountId = previousClose.Id, TotalAmount = previousClose.TotalAmount,
            CapturedByUserId = fixture.User.Id, CapturedAt = BankingTestContext.Now.AddDays(-1),
            WalletsJson = CountSnapshotCodec.Serialize(
            [
                new(fixture.CashWallet.Id, "cash", WalletTypeEnum.Cash, "UGX", 100_000m, BankingTestContext.Now.AddDays(-1), null),
                new(fixture.BankWallet.Id, "bank", WalletTypeEnum.Bank, "UGX", 1_000_000m, BankingTestContext.Now.AddDays(-1), fixture.Account.Id)
            ])
        });
        var storedVault = await fixture.Db.Vaults.SingleAsync();
        storedVault.CurrentBalance = 1_100_000m;
        await fixture.Db.SaveChangesAsync();
        var opening = AddCount(fixture, true, 100_000m, 1_000_000m + bankChange);

        await service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id);

        var settlement = await fixture.Db.SessionFloatSettlements.SingleAsync();
        Assert.Equal(1_100_000m, settlement.VaultAmount);
        Assert.Equal(bankChange, settlement.BankVariance);
        Assert.Equal(0m, (await fixture.Db.Vaults.SingleAsync()).CurrentBalance);
        Assert.Equal(1_000_000m + bankChange, (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance);
        Assert.Equal(1_100_000m, (await fixture.Db.CashCounts.FindAsync(previousClose.Id))!.TotalAmount);
        Assert.Equal(CashCountStatus.PendingApproval, previousClose.Status);
    }

    [Fact]
    public async Task Closing_UnexplainedWalletVarianceRollsBackSnapshotAndAllocation()
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        var opening = AddCount(fixture, true, 100_000m, 1_000_000m);
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = opening.TotalAmount;
        await fixture.Db.SaveChangesAsync();
        await service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id);
        var closing = AddCount(fixture, false, 90_000m, 1_010_000m);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.SubmitClosingAsync(fixture.User.Id, closing.Id));

        Assert.False(await fixture.Db.CashCountSnapshots.AnyAsync(s => s.CashCountId == closing.Id));
        Assert.False(await fixture.Db.SessionFloatSettlements.AnyAsync(s => s.Stage == FloatSettlementStage.Closing));
        Assert.Equal(CashCountStatus.Draft, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.Status);
        Assert.Equal(100_000m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
        Assert.Equal(0m, (await fixture.Db.Vaults.SingleAsync()).CurrentBalance);
    }

    [Fact]
    public async Task Opening_StillRequiresApprovalAuthorityAndAvailableInternalFloat()
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        var opening = AddCount(fixture, true, 100_000m, 1_000_000m);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveOpeningAsync(fixture.User.Id, opening.Id));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id));

        Assert.False(await fixture.Db.SessionFloatSettlements.AnyAsync());
        Assert.False(await fixture.Db.CashCountSnapshots.AnyAsync());
    }

    private static CashCount AddCount(BankingTestContext fixture, bool opening, decimal cash, decimal bank)
    {
        var observedAt = opening ? fixture.Session.OpenedAt : BankingTestContext.Now.AddMinutes(30);
        var count = new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, IsOpening = opening,
            Status = opening ? CashCountStatus.PendingApproval : CashCountStatus.Draft,
            CountDate = fixture.Session.SessionDate, TotalAmount = cash + bank, CreatedAt = BankingTestContext.Now,
            Details =
            [
                new() { WalletId = fixture.CashWallet.Id, Amount = cash, ObservedAt = observedAt },
                new() { WalletId = fixture.BankWallet.Id, Amount = bank, ObservedAt = observedAt }
            ]
        };
        fixture.Db.CashCounts.Add(count);
        fixture.Db.SaveChanges();
        return count;
    }

    private static (SessionSettlementService Service, Mock<IVaultService> Vault) CreateService(BankingTestContext fixture)
    {
        var clock = new BusinessClock(new FixedTimeProvider());
        var vault = new Mock<IVaultService>();
        async Task<VaultOperationResult> Post(long sessionId, long branchId, decimal amount, string userId, bool withdraw, CancellationToken token)
        {
            var storedVault = await fixture.Db.Vaults.SingleAsync(v => v.BranchId == branchId, token);
            if (withdraw && storedVault.CurrentBalance < amount)
                return VaultOperationResult.Error("Insufficient balance in vault.");
            storedVault.CurrentBalance += withdraw ? -amount : amount;
            var entry = new VaultTransaction
            {
                VaultId = storedVault.Id, CashSessionId = sessionId, Amount = amount,
                Type = withdraw ? VaultTransactionType.Opening : VaultTransactionType.Closing,
                Status = VaultTransactionStatus.Completed, BalanceAfter = storedVault.CurrentBalance,
                CreatedByUserId = userId, CreatedAt = clock.UtcNow
            };
            fixture.Db.VaultTransactions.Add(entry);
            await fixture.Db.SaveChangesAsync(token);
            return VaultOperationResult.Ok(entry.Id);
        }
        vault.Setup(v => v.WithdrawForSessionAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .Returns((long sessionId, long branchId, decimal amount, string userId, bool _, CancellationToken token) =>
                Post(sessionId, branchId, amount, userId, true, token));
        vault.Setup(v => v.DepositForSessionAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .Returns((long sessionId, long branchId, decimal amount, string userId, bool _, CancellationToken token) =>
                Post(sessionId, branchId, amount, userId, false, token));
        return (new SessionSettlementService(fixture.Db, vault.Object,
            new BankingAccessService(fixture.Db, clock), new SessionBalanceService(fixture.Db, clock), clock), vault);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => BankingTestContext.Now.AddHours(1);
    }
}
