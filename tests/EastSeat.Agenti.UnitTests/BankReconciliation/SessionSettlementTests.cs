using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class SessionSettlementTests
{
    [Theory]
    [InlineData(true, 1_020_000)]
    [InlineData(false, 980_000)]
    public async Task LegacyClosing_AccountsForBankRunIntoOrOutOfComparedWallet(bool intoBank, decimal closingBank)
    {
        using var fixture = new BankingTestContext();
        fixture.Session.WorkflowVersion = SessionWorkflowVersion.Legacy;
        fixture.AddApprovedOpening();
        var (service, _) = CreateService(fixture);
        fixture.Db.BankRuns.Add(new BankRun
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id,
            FromWalletId = intoBank ? fixture.CashWallet.Id : fixture.BankWallet.Id,
            ToWalletId = intoBank ? fixture.BankWallet.Id : fixture.CashWallet.Id,
            Amount = 20_000m, RecordedByUserId = fixture.User.Id, CreatedAt = BankingTestContext.Now
        });
        var closing = AddCount(fixture, false, 100_000m, closingBank);

        await service.SubmitClosingAsync(fixture.User.Id, closing.Id);

        Assert.Empty(await fixture.Db.Discrepancies.ToListAsync());
        Assert.Equal(CashCountStatus.PendingApproval, closing.Status);
    }

    [Fact]
    public async Task LegacyClosing_BankRunDoesNotMaskRealComparedWalletVariance()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.WorkflowVersion = SessionWorkflowVersion.Legacy;
        fixture.AddApprovedOpening();
        var (service, _) = CreateService(fixture);
        fixture.Db.BankRuns.Add(new BankRun
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id,
            FromWalletId = fixture.CashWallet.Id, ToWalletId = fixture.BankWallet.Id,
            Amount = 20_000m, RecordedByUserId = fixture.User.Id, CreatedAt = BankingTestContext.Now
        });
        var closing = AddCount(fixture, false, 80_000m, 1_025_000m);
        closing.Explanation = "Bank balance needs review.";
        await fixture.Db.SaveChangesAsync();

        await service.SubmitClosingAsync(fixture.User.Id, closing.Id);

        var discrepancy = await fixture.Db.Discrepancies.SingleAsync();
        Assert.Equal(1_020_000m, discrepancy.ExpectedAmount);
        Assert.Equal(5_000m, discrepancy.Variance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BankingClosing_BankRunIsIncludedWhenValidatingBankBalance(bool mismatch)
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        var opening = AddCount(fixture, true, 100_000m, 1_000_000m);
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = opening.TotalAmount;
        await fixture.Db.SaveChangesAsync();
        await service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id);
        fixture.Db.BankRuns.Add(new BankRun
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id,
            FromWalletId = fixture.CashWallet.Id, ToWalletId = fixture.BankWallet.Id,
            Amount = 20_000m, RecordedByUserId = fixture.User.Id, CreatedAt = BankingTestContext.Now
        });
        var closing = AddCount(fixture, false, 80_000m, mismatch ? 1_025_000m : 1_020_000m);

        if (mismatch)
        {
            await Assert.ThrowsAsync<BankingRuleException>(() => service.SubmitClosingAsync(fixture.User.Id, closing.Id));
            Assert.False(await fixture.Db.SessionFloatSettlements.AnyAsync(s => s.Stage == FloatSettlementStage.Closing));
        }
        else
        {
            await service.SubmitClosingAsync(fixture.User.Id, closing.Id);
            Assert.Empty(await fixture.Db.Discrepancies.ToListAsync());
        }
    }

    [Fact]
    public async Task MultipleBankAndMobileWallets_OpenAndCloseWithOneSettlementAndSeparateBindings()
    {
        using var fixture = new BankingTestContext();
        var secondBank = fixture.AddWallet("ABSA", WalletTypeEnum.Bank, 200_000m);
        var secondAccount = fixture.BindBankAccount(secondBank);
        var mobile = fixture.AddWallet("Airtel", WalletTypeEnum.MobileMoney, 300_000m);
        var initial = await fixture.Db.Wallets.ToDictionaryAsync(w => w.Id, w => w.Balance);
        var (settlements, vault) = CreateService(fixture);
        var clock = new BusinessClock(new FixedTimeProvider());
        var workflow = new BankSessionWorkflow(fixture.Db, new BankingAccessService(fixture.Db, clock),
            clock, new SessionBalanceService(fixture.Db, clock), settlements);
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = initial.Values.Sum();
        await fixture.Db.SaveChangesAsync();
        var opening = await workflow.InitializeFormAsync(fixture.User.Id, true);
        foreach (var wallet in opening.WalletEntries)
        {
            wallet.CountedAmount = initial[wallet.WalletId];
            wallet.ObservedAt = clock.UtcNow;
            wallet.Denominations = null;
        }
        var savedOpening = await workflow.SaveAsync(fixture.User.Id, opening);
        await workflow.SubmitAsync(fixture.User.Id, savedOpening.CashCountId!.Value);
        await workflow.ApproveOpeningAsync(fixture.Admin.Id, savedOpening.CashCountId.Value);
        fixture.Db.BankRuns.Add(new BankRun
        {
            AgentId = fixture.Agent.Id, CashSessionId = fixture.Session.Id, FromWalletId = fixture.CashWallet.Id,
            ToWalletId = mobile.Id, Amount = 20_000m, RecordedByUserId = fixture.User.Id,
            OccurredAt = clock.UtcNow, CreatedAt = clock.UtcNow
        });
        await fixture.Db.SaveChangesAsync();
        var trading = await workflow.InitializeFormAsync(fixture.User.Id, false, forWalletAdjustment: true);
        Assert.Equal(initial[mobile.Id] + 20_000m, trading.WalletEntries.Single(w => w.WalletId == mobile.Id).ExpectedBalance);
        var closing = await workflow.InitializeFormAsync(fixture.User.Id, false);
        Assert.Equal(4, closing.WalletEntries.Count);
        foreach (var wallet in closing.WalletEntries)
        {
            wallet.CountedAmount = initial[wallet.WalletId] + (wallet.WalletId == mobile.Id ? 10_000m : 0m);
            Assert.Equal(wallet.WalletId == mobile.Id || wallet.WalletId == fixture.CashWallet.Id, wallet.UsesEnteredClosingAmount);
            Assert.Equal(wallet.CountedAmount, wallet.ExpectedBalance);
            wallet.ObservedAt = clock.UtcNow;
            wallet.Denominations = null;
        }
        var savedClosing = await workflow.SaveAsync(fixture.User.Id, closing);

        await workflow.SubmitAsync(fixture.User.Id, savedClosing.CashCountId!.Value);
        await workflow.SubmitAsync(fixture.User.Id, savedClosing.CashCountId.Value);

        var snapshot = await fixture.Db.CashCountSnapshots.SingleAsync(s => s.CashCountId == savedClosing.CashCountId);
        var wallets = CountSnapshotCodec.Deserialize(snapshot.WalletsJson);
        Assert.Equal(fixture.Account.Id, wallets.Single(w => w.WalletId == fixture.BankWallet.Id).BankAccountId);
        Assert.Equal(secondAccount.Id, wallets.Single(w => w.WalletId == secondBank.Id).BankAccountId);
        Assert.Null(wallets.Single(w => w.WalletId == mobile.Id).BankAccountId);
        Assert.All(await fixture.Db.Wallets.ToListAsync(), w => Assert.Equal(0m, w.Balance));
        Assert.Empty(await fixture.Db.Discrepancies.ToListAsync());
        Assert.Equal(initial.Values.Sum() + 10_000m, (await fixture.Db.Vaults.SingleAsync()).CurrentBalance);
        vault.Verify(v => v.DepositForSessionAsync(fixture.Session.Id, fixture.Account.BranchId,
            initial.Values.Sum() + 10_000m, fixture.User.Id, false, It.IsAny<CancellationToken>()), Times.Once);
    }

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
        var previousClose = AddPreviousClosing(fixture, SessionWorkflowVersion.BankReconciliation, settled: false);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Opening_BridgesFromLegacySubmittedClosingOnlyOnceItsFloatIsSettled(bool settled)
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        AddPreviousClosing(fixture, SessionWorkflowVersion.Legacy, settled);
        var opening = AddCount(fixture, true, 100_000m, 1_002_000m);
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = 1_102_000m;
        await fixture.Db.SaveChangesAsync();

        await service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id);

        var settlement = await fixture.Db.SessionFloatSettlements.SingleAsync(s => s.Stage == FloatSettlementStage.Opening);
        Assert.Equal(settled ? 1 : 0, await fixture.Db.OpeningBankVariances.CountAsync());
        Assert.Equal(settled ? 2_000m : 0m, settlement.BankVariance);
        Assert.Equal(settled ? 1_100_000m : 1_102_000m, settlement.VaultAmount);
    }

    [Fact]
    public async Task Closing_EnteredCashSettlesOnceWithoutExplanationOrDiscrepancy()
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        var opening = AddCount(fixture, true, 100_000m, 1_000_000m);
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = opening.TotalAmount;
        await fixture.Db.SaveChangesAsync();
        await service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id);
        var closing = AddCount(fixture, false, 90_000m, 1_000_000m);

        await service.SubmitClosingAsync(fixture.User.Id, closing.Id);
        await service.SubmitClosingAsync(fixture.User.Id, closing.Id);

        Assert.Empty(await fixture.Db.Discrepancies.ToListAsync());
        var settlement = await fixture.Db.SessionFloatSettlements.SingleAsync(s => s.Stage == FloatSettlementStage.Closing);
        Assert.Equal(1_090_000m, settlement.VaultAmount);
        Assert.Equal(CashCountStatus.PendingApproval, closing.Status);
    }

    [Fact]
    public async Task Closing_ExplainedBankOnlyVarianceDoesNotCreateDiscrepancy()
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        var opening = AddCount(fixture, true, 100_000m, 1_000_000m);
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = opening.TotalAmount;
        await fixture.Db.SaveChangesAsync();
        await service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id);
        var closing = AddCount(fixture, false, 100_000m, 1_010_000m);
        closing.Explanation = "Bank balance moved by an unrecorded customer transfer.";
        await fixture.Db.SaveChangesAsync();

        await service.SubmitClosingAsync(fixture.User.Id, closing.Id);

        Assert.Empty(await fixture.Db.Discrepancies.ToListAsync());
    }

    [Fact]
    public async Task Session_StaysOpenUntilEveryOpenedAgentHasSubmittedAClosing()
    {
        using var fixture = new BankingTestContext();
        var (service, _) = CreateService(fixture);
        var secondUser = new ApplicationUser
        {
            Id = "banking-agent-2", UserName = "banking-agent-2", FirstName = "Second", LastName = "Agent",
            Role = UserRole.Agent, BranchId = fixture.Account.BranchId, IsActive = true
        };
        fixture.Db.Users.Add(secondUser);
        fixture.Db.SaveChanges();
        var secondAgent = new Agent
        {
            UserId = secondUser.Id, BranchId = fixture.Account.BranchId, Code = "TST2", IsActive = true,
            CreatedAt = BankingTestContext.Now
        };
        fixture.Db.Agents.Add(secondAgent);
        fixture.Db.SaveChanges();
        secondUser.AgentId = secondAgent.Id;
        var secondCash = new Wallet
        {
            AgentId = secondAgent.Id, WalletTypeId = 1, Name = "Second cash", Currency = "UGX",
            Balance = 50_000m, IsActive = true, CreatedAt = BankingTestContext.Now
        };
        fixture.Db.Wallets.Add(secondCash);
        fixture.Db.SaveChanges();
        var firstOpening = AddCount(fixture, true, 100_000m, 1_000_000m);
        var secondOpening = AddSecondAgentCount(fixture, secondAgent, secondCash, isOpening: true);
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = firstOpening.TotalAmount + secondOpening.TotalAmount;
        await fixture.Db.SaveChangesAsync();
        await service.ApproveOpeningAsync(fixture.Admin.Id, firstOpening.Id);
        await service.ApproveOpeningAsync(fixture.Admin.Id, secondOpening.Id);
        var firstClosing = AddCount(fixture, false, 100_000m, 1_000_000m);
        var secondClosing = AddSecondAgentCount(fixture, secondAgent, secondCash, isOpening: false);

        await service.SubmitClosingAsync(fixture.User.Id, firstClosing.Id);

        var session = (await fixture.Db.CashSessions.FindAsync(fixture.Session.Id))!;
        Assert.Equal(CashSessionStatus.Open, session.Status);
        Assert.Null(session.OperationalClosedAt);

        await service.SubmitClosingAsync(secondUser.Id, secondClosing.Id);

        session = (await fixture.Db.CashSessions.FindAsync(fixture.Session.Id))!;
        Assert.Equal(CashSessionStatus.Closed, session.Status);
        Assert.NotNull(session.ClosedAt);
        Assert.NotNull(session.OperationalClosedAt);
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(firstClosing.Id))!.Status);
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
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Users.Where(u => u.Id == fixture.User.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Supervisor));
        var selfApproval = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveOpeningAsync(fixture.User.Id, opening.Id));
        Assert.Contains("your own opening", selfApproval.Message);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.ApproveOpeningAsync(fixture.Admin.Id, opening.Id));

        Assert.False(await fixture.Db.SessionFloatSettlements.AnyAsync());
        Assert.False(await fixture.Db.CashCountSnapshots.AnyAsync());
    }

    private static CashCount AddPreviousClosing(
        BankingTestContext fixture, SessionWorkflowVersion workflow, bool settled)
    {
        var previousAt = BankingTestContext.Now.AddDays(-1);
        var oldSession = new CashSession
        {
            BranchId = fixture.Account.BranchId, SessionDate = fixture.Session.SessionDate.AddDays(-1),
            Status = CashSessionStatus.Closed, WorkflowVersion = workflow, OpenedAt = previousAt.AddHours(-1),
            OperationalClosedAt = workflow == SessionWorkflowVersion.BankReconciliation ? previousAt : null
        };
        fixture.Db.CashSessions.Add(oldSession);
        fixture.Db.SaveChanges();
        var previousClose = new CashCount
        {
            CashSessionId = oldSession.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            Status = CashCountStatus.PendingApproval, CountDate = oldSession.SessionDate,
            TotalAmount = 1_100_000m, CreatedAt = previousAt
        };
        fixture.Db.CashCounts.Add(previousClose);
        fixture.Db.SaveChanges();
        var snapshot = new CashCountSnapshot
        {
            CashCountId = previousClose.Id, TotalAmount = previousClose.TotalAmount,
            CapturedByUserId = fixture.User.Id, CapturedAt = previousAt,
            WalletsJson = CountSnapshotCodec.Serialize(
            [
                new(fixture.CashWallet.Id, "cash", WalletTypeEnum.Cash, "UGX", 100_000m, previousAt, null),
                new(fixture.BankWallet.Id, "bank", WalletTypeEnum.Bank, "UGX", 1_000_000m, previousAt, fixture.Account.Id)
            ])
        };
        fixture.Db.CashCountSnapshots.Add(snapshot);
        fixture.Db.SaveChanges();
        if (settled)
            fixture.Db.SessionFloatSettlements.Add(new SessionFloatSettlement
            {
                CashCountId = previousClose.Id, SnapshotId = snapshot.Id, Stage = FloatSettlementStage.Closing,
                ReportedAmount = previousClose.TotalAmount, VaultAmount = previousClose.TotalAmount,
                PostedAt = previousAt, PostedByUserId = fixture.User.Id
            });
        fixture.Db.Vaults.Single().CurrentBalance = 1_100_000m;
        fixture.Db.SaveChanges();
        return previousClose;
    }

    private static CashCount AddSecondAgentCount(
        BankingTestContext fixture, Agent agent, Wallet wallet, bool isOpening)
    {
        var count = new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = agent.Id, IsOpening = isOpening,
            Status = isOpening ? CashCountStatus.PendingApproval : CashCountStatus.Draft,
            CountDate = fixture.Session.SessionDate, TotalAmount = 50_000m, CreatedAt = BankingTestContext.Now,
            Details =
            [
                new() { WalletId = wallet.Id, Amount = 50_000m,
                    ObservedAt = isOpening ? fixture.Session.OpenedAt : BankingTestContext.Now.AddMinutes(30) }
            ]
        };
        fixture.Db.CashCounts.Add(count);
        fixture.Db.SaveChanges();
        return count;
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
