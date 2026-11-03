using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class SessionBalanceTests
{
    [Fact]
    public async Task Projection_UsesOpeningAndMovementsInsteadOfRefreshedWalletCache()
    {
        using var fixture = new BankingTestContext();
        AddOpening(fixture);
        var customer = fixture.AddCustomerTransaction(500_000m);
        fixture.Db.CustomerMovements.Add(Movement(fixture, customer.Id, CustomerMovementKind.Completed, 500_000m, -500_000m));
        fixture.Db.BankRuns.Add(new BankRun
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, FromWalletId = fixture.CashWallet.Id,
            ToWalletId = fixture.BankWallet.Id, Amount = 400_000m, RecordedByUserId = fixture.User.Id,
            OccurredAt = BankingTestContext.Now.AddMinutes(1), CreatedAt = BankingTestContext.Now.AddMinutes(1)
        });
        fixture.CashWallet.Balance = 200_000m;
        fixture.BankWallet.Balance = 900_000m;
        await fixture.Db.SaveChangesAsync();

        var positions = await CreateService(fixture).GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id);

        Assert.Equal(200_000m, positions.Single(p => p.WalletId == fixture.CashWallet.Id).ExpectedBalance);
        Assert.Equal(900_000m, positions.Single(p => p.WalletId == fixture.BankWallet.Id).ExpectedBalance);
        Assert.Equal(1_100_000m, positions.Sum(p => p.ExpectedBalance));
    }

    [Fact]
    public async Task FailedDeposit_ReversalDoesNotReleaseCustomerCashOrCreateAnotherReceipt()
    {
        using var fixture = new BankingTestContext();
        AddOpening(fixture);
        var customer = fixture.AddCustomerTransaction();
        fixture.Db.CustomerMovements.AddRange(
            Movement(fixture, customer.Id, CustomerMovementKind.FailedAttempt, 50_000m, -50_000m),
            Movement(fixture, customer.Id, CustomerMovementKind.BankReversal, 0m, 50_000m));
        await fixture.Db.SaveChangesAsync();

        var positions = await CreateService(fixture).GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id);
        var cash = positions.Single(p => p.WalletId == fixture.CashWallet.Id);
        Assert.Equal(150_000m, cash.ExpectedBalance);
        Assert.Equal(50_000m, cash.ReservedCustomerCash);
        Assert.Equal(100_000m, cash.AvailableForTransfer);

        fixture.Db.CustomerMovements.Add(Movement(fixture, customer.Id, CustomerMovementKind.Retry, 0m, -50_000m));
        await fixture.Db.SaveChangesAsync();
        positions = await CreateService(fixture).GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id);

        Assert.Equal(150_000m, positions.Single(p => p.WalletId == fixture.CashWallet.Id).ExpectedBalance);
        Assert.Equal(0m, positions.Single(p => p.WalletId == fixture.CashWallet.Id).ReservedCustomerCash);
        Assert.Equal(950_000m, positions.Single(p => p.WalletId == fixture.BankWallet.Id).ExpectedBalance);
    }

    [Fact]
    public async Task LinkedCorrection_ReplacesEffectWithoutErasingOriginal()
    {
        using var fixture = new BankingTestContext();
        AddOpening(fixture);
        var customer = fixture.AddCustomerTransaction();
        var original = Movement(fixture, customer.Id, CustomerMovementKind.Completed, 50_000m, -50_000m);
        fixture.Db.CustomerMovements.Add(original);
        await fixture.Db.SaveChangesAsync();
        var correction = Movement(fixture, customer.Id, CustomerMovementKind.Completed, 40_000m, -40_000m);
        correction.SupersedesMovementId = original.Id;
        fixture.Db.CustomerMovements.Add(correction);
        await fixture.Db.SaveChangesAsync();

        var positions = await CreateService(fixture).GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id);

        Assert.Equal(140_000m, positions.Single(p => p.WalletId == fixture.CashWallet.Id).ExpectedBalance);
        Assert.Equal(2, await fixture.Db.CustomerMovements.CountAsync());
    }

    [Fact]
    public async Task Projection_IgnoresCrossSessionBankMovementsOutsideOpeningWalletsButValidatesCurrentSession()
    {
        using var fixture = new BankingTestContext();
        AddOpening(fixture);
        var retiredWallet = new Wallet
        {
            AgentId = fixture.Agent.Id, WalletTypeId = 2, Name = "Retired bank", Currency = "UGX",
            IsActive = false, CreatedAt = BankingTestContext.Now
        };
        fixture.Db.Wallets.Add(retiredWallet);
        await fixture.Db.SaveChangesAsync();
        var retiredAccount = new BankAccount
        {
            BranchId = fixture.Account.BranchId, WalletId = retiredWallet.Id, BankName = "Retired bank",
            AccountIdentifier = "RETIRED-ACCOUNT", CreatedAt = BankingTestContext.Now
        };
        var priorSession = new CashSession
        {
            BranchId = fixture.Session.BranchId, SessionDate = fixture.Session.SessionDate.AddDays(-1),
            OpenedAt = BankingTestContext.Now.AddDays(-1), Status = CashSessionStatus.Closed,
            WorkflowVersion = SessionWorkflowVersion.BankReconciliation
        };
        fixture.Db.AddRange(retiredAccount, priorSession);
        await fixture.Db.SaveChangesAsync();
        var customer = new CustomerTransaction
        {
            BankAccountId = retiredAccount.Id, AgentId = fixture.Agent.Id,
            OriginatingSessionId = fixture.Session.Id, CashWalletId = fixture.CashWallet.Id,
            Kind = CustomerTransactionKind.Deposit, Amount = 50_000m, CustomerIdentifier = "RETIRED-WALLET",
            CommandId = Guid.NewGuid(), RecordedByUserId = fixture.User.Id,
            OccurredAt = BankingTestContext.Now, RecordedAt = BankingTestContext.Now
        };
        fixture.Db.CustomerTransactions.Add(customer);
        await fixture.Db.SaveChangesAsync();
        var historicalMovement = Movement(
            fixture, customer.Id, CustomerMovementKind.BankReversal, 0m, 50_000m);
        historicalMovement.CashSessionId = priorSession.Id;
        fixture.Db.CustomerMovements.Add(historicalMovement);
        await fixture.Db.SaveChangesAsync();

        var positions = await CreateService(fixture).GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id);
        Assert.Equal(1_000_000m, positions.Single(p => p.WalletId == fixture.BankWallet.Id).ExpectedBalance);

        fixture.Db.CustomerMovements.Add(Movement(
            fixture, customer.Id, CustomerMovementKind.BankReversal, 0m, 50_000m));
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(fixture).GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id));
    }

    [Fact]
    public async Task Reservations_AggregatePerCaseAndRespectSnapshotCutoffAndCorrections()
    {
        using var fixture = new BankingTestContext();
        AddOpening(fixture);
        var deposit = fixture.AddCustomerTransaction();
        var withdrawal = fixture.AddCustomerTransaction();
        var settled = fixture.AddCustomerTransaction();
        var original = Movement(fixture, withdrawal.Id, CustomerMovementKind.PendingAttempt, 0m, 70_000m);
        fixture.Db.CustomerMovements.AddRange(
            Movement(fixture, deposit.Id, CustomerMovementKind.FailedAttempt, 50_000m, 0m),
            original,
            Movement(fixture, settled.Id, CustomerMovementKind.Completed, 50_000m, -50_000m));
        await fixture.Db.SaveChangesAsync();
        var correction = Movement(fixture, withdrawal.Id, CustomerMovementKind.PendingAttempt, 0m, 40_000m);
        correction.SupersedesMovementId = original.Id;
        correction.TransactionKind = CustomerTransactionKind.Withdrawal;
        fixture.Db.CustomerMovements.Add(correction);
        await fixture.Db.SaveChangesAsync();
        var paid = Movement(fixture, withdrawal.Id, CustomerMovementKind.CashPayment, -10_000m, 0m);
        paid.RelatedMovementId = correction.Id;
        paid.CustomerSettledAmount = 10_000m;
        var retry = Movement(fixture, deposit.Id, CustomerMovementKind.Retry, 0m, -50_000m);
        retry.OccurredAt = BankingTestContext.Now.AddMinutes(30);
        fixture.Db.CustomerMovements.AddRange(paid, retry);
        await fixture.Db.SaveChangesAsync();

        var closing = new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            CountDate = fixture.Session.SessionDate, CreatedAt = BankingTestContext.Now
        };
        fixture.Db.CashCounts.Add(closing);
        await fixture.Db.SaveChangesAsync();
        var snapshot = new CashCountSnapshot
        {
            CashCountId = closing.Id, CapturedAt = BankingTestContext.Now, CapturedByUserId = fixture.User.Id,
            WalletsJson = CountSnapshotCodec.Serialize(
            [
                new(fixture.CashWallet.Id, "Cash", WalletTypeEnum.Cash, "UGX", 190_000m, BankingTestContext.Now, null),
                new(fixture.BankWallet.Id, "Bank", WalletTypeEnum.Bank, "UGX", 990_000m, BankingTestContext.Now, fixture.Account.Id)
            ])
        };
        fixture.Db.CashCountSnapshots.Add(snapshot);
        await fixture.Db.SaveChangesAsync();

        var atSnapshot = await CreateService(fixture).GetPositionsAtSnapshotAsync(snapshot.Id);
        Assert.Equal(50_000m, atSnapshot.Single(p => p.WalletId == fixture.CashWallet.Id).ReservedCustomerCash);
        Assert.Equal(30_000m, atSnapshot.Single(p => p.WalletId == fixture.BankWallet.Id).ReservedBankSettlement);

        var current = await CreateService(fixture).GetPositionsAsync(fixture.Session.Id, fixture.Agent.Id);
        Assert.Equal(0m, current.Single(p => p.WalletId == fixture.CashWallet.Id).ReservedCustomerCash);
        Assert.Equal(30_000m, current.Single(p => p.WalletId == fixture.BankWallet.Id).ReservedBankSettlement);
    }

    [Fact]
    public void BusinessClock_UsesUgandaDateAcrossUtcMidnightBoundary()
    {
        var instant = new DateTimeOffset(2026, 11, 1, 21, 5, 0, TimeSpan.Zero);
        var clock = new BusinessClock(new FixedTimeProvider(instant));

        Assert.Equal(new DateOnly(2026, 11, 2), clock.Today);
        Assert.Equal(TimeSpan.FromHours(3), clock.ToBusinessTime(instant).Offset);
    }

    internal static void AddOpening(BankingTestContext fixture)
    {
        fixture.AddApprovedOpening();
    }

    private static SessionBalanceService CreateService(BankingTestContext fixture) =>
        new(fixture.Db, new BusinessClock(new FixedTimeProvider(BankingTestContext.Now.AddHours(1))));

    private static CustomerMovement Movement(
        BankingTestContext fixture, long transactionId, CustomerMovementKind kind, decimal cash, decimal bank) => new()
    {
        CustomerTransactionId = transactionId, CashSessionId = fixture.Session.Id, Kind = kind,
        Amount = Math.Max(Math.Abs(cash), Math.Abs(bank)),
        CustomerSettledAmount = kind is CustomerMovementKind.Completed or CustomerMovementKind.Retry ? Math.Max(0, -bank) : 0,
        CashEffect = cash, BankEffect = bank, CommandId = Guid.NewGuid(), RecordedByUserId = fixture.User.Id,
        OccurredAt = BankingTestContext.Now, RecordedAt = BankingTestContext.Now
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
