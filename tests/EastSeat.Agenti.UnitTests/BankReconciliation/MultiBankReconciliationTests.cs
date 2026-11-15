using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class MultiBankReconciliationTests
{
    [Fact]
    public async Task EachBank_UsesItsOwnAdjustmentsAndOnlyTheLastApprovalCertifiesTheClosing()
    {
        using var fixture = new BankingTestContext();
        var (service, first, second, closingId) = await PrepareAsync(fixture);
        Assert.All(new[] { first, second }, r =>
            Assert.True(r.Sessions.Single().CanApprove, string.Join(" ", r.Sessions.Single().BlockingReasons)));
        Assert.DoesNotContain(first.Sessions.Single().Wallets, w => w.WalletName == "ABSA");
        Assert.Equal(-10_000m, first.Sessions.Single().Wallets.Single(w => w.WalletId == fixture.BankWallet.Id).BankAdjustments);
        Assert.Equal(-5_000m, second.Sessions.Single().Wallets.Single(w => w.WalletName == "ABSA").BankAdjustments);
        var balances = await fixture.Db.Wallets.ToDictionaryAsync(w => w.Id, w => w.Balance);
        var vault = (await fixture.Db.Vaults.SingleAsync()).CurrentBalance;

        first = await ApproveAsync(service, fixture, first);

        Assert.True(first.Sessions.Single().BankApprovalCurrent);
        Assert.Equal(["ABSA"], first.Sessions.Single().PendingBankWallets);
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(closingId))!.Status);
        Assert.False(await fixture.Db.Notifications.AnyAsync(n => n.Type == NotificationType.CountApproved));
        var otherAccount = await fixture.Db.BankAccounts.Include(a => a.Wallet).SingleAsync(a => a.Id == second.BankAccountId);
        otherAccount.WorkflowEnabled = false;
        otherAccount.Wallet!.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(["ABSA"], (await service.GetAsync(fixture.Admin.Id, first.Id)).Sessions.Single().PendingBankWallets);
        var command = Guid.NewGuid();
        var version = second.Version;
        var hash = second.Sessions.Single().BasisHash;

        second = await service.ApproveClosingAsync(fixture.Admin.Id, second.Id, fixture.Session.Id, command, version, hash);
        await service.ApproveClosingAsync(fixture.Admin.Id, second.Id, fixture.Session.Id, command, version, hash);

        Assert.Empty(second.Sessions.Single().PendingBankWallets);
        Assert.Equal(CashCountStatus.Approved, second.Sessions.Single().ApprovalStatus);
        Assert.Equal(CashCountStatus.Approved, (await fixture.Db.CashCounts.FindAsync(closingId))!.Status);
        Assert.Equal(2, await fixture.Db.BankReconciliationResults.CountAsync());
        Assert.Equal(1, await fixture.Db.CashCountAuditLogs.CountAsync(a => a.CashCountId == closingId && a.Action == CashCountAuditAction.Approved));
        Assert.Equal(1, await fixture.Db.Notifications.CountAsync(n => n.Type == NotificationType.CountApproved));
        Assert.Equal(vault, (await fixture.Db.Vaults.SingleAsync()).CurrentBalance);
        foreach (var wallet in await fixture.Db.Wallets.ToListAsync())
            Assert.Equal(balances[wallet.Id], wallet.Balance);
        Assert.Equal(1, await fixture.Db.SessionFloatSettlements.CountAsync(s => s.Stage == FloatSettlementStage.Closing));
    }

    [Theory]
    [InlineData("classification")]
    [InlineData("revision")]
    [InlineData("flag")]
    public async Task EarlierBankApproval_MustStillBeCurrentWhenAnotherBankIsApproved(string change)
    {
        using var fixture = new BankingTestContext();
        var (service, first, second, closingId) = await PrepareAsync(fixture);
        first = await ApproveAsync(service, fixture, first);
        if (change == "classification")
        {
            await ClassifyAsync(service, fixture, first, BankTransactionClassification.Unknown);
        }
        else if (change == "revision")
        {
            await fixture.ImportStatementAsync(fixture.Account, Clock(), [Fee(-20_000m, 980_000m)],
                1_000_000m, 980_000m, acceptRevisions: true);
        }
        else
        {
            var admin = await fixture.Db.Users.SingleAsync(u => u.Id == fixture.Admin.Id);
            admin.Role = UserRole.Supervisor;
            await fixture.Db.SaveChangesAsync();
            await service.FlagAsync(fixture.Admin.Id, first.Id, first.Version,
                "The first bank requires additional independent investigation.");
            admin.Role = UserRole.Admin;
            await fixture.Db.SaveChangesAsync();
        }

        second = await ApproveAsync(service, fixture, second);

        Assert.True(second.Sessions.Single().BankApprovalCurrent);
        Assert.Contains(fixture.BankWallet.Name, second.Sessions.Single().PendingBankWallets);
        Assert.Equal(CashCountStatus.PendingApproval, second.Sessions.Single().ApprovalStatus);
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(closingId))!.Status);
        Assert.Equal(2, await fixture.Db.BankReconciliationResults.CountAsync());
        Assert.False(await fixture.Db.Notifications.AnyAsync(n => n.Type == NotificationType.CountApproved));
    }

    [Fact]
    public async Task MissingStatementOrUnboundBank_CannotBeSkippedByApprovingAnotherBank()
    {
        using var fixture = new BankingTestContext();
        var other = fixture.AddWallet("ABSA", WalletTypeEnum.Bank, 200_000m);
        var unbound = fixture.AddWallet("Unbound bank", WalletTypeEnum.Bank, 50_000m);
        fixture.BindBankAccount(other);
        fixture.AddApprovedOpening(withSettlement: true, additionalWallets: [other, unbound]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.ImportStatementAsync(fixture.Account, Clock(), [], 1_000_000m, 1_000_000m);
        var service = Service(fixture);
        var review = await service.CreateAsync(fixture.Admin.Id, fixture.Account.Id, fixture.Session.SessionDate, fixture.Session.SessionDate);

        review = await ApproveAsync(service, fixture, review);

        Assert.Contains("ABSA", review.Sessions.Single().PendingBankWallets);
        Assert.Contains(review.Sessions.Single().PendingBankWallets, w => w.Contains("Unbound bank"));
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.Status);
    }

    private static async Task<(BankReconciliationService Service, BankReconciliationReviewDto First,
        BankReconciliationReviewDto Second, long ClosingId)> PrepareAsync(BankingTestContext fixture)
    {
        var other = fixture.AddWallet("ABSA", WalletTypeEnum.Bank, 200_000m);
        var account = fixture.BindBankAccount(other);
        var mobile = fixture.AddWallet("Airtel", WalletTypeEnum.MobileMoney, 300_000m);
        fixture.AddApprovedOpening(withSettlement: true, additionalWallets: [other, mobile]);
        var closing = fixture.AddSubmittedClosing(new Dictionary<long, decimal>
            { [fixture.BankWallet.Id] = 990_000m, [other.Id] = 195_000m });
        await fixture.ImportStatementAsync(fixture.Account, Clock(), [Fee(-10_000m, 990_000m)], 1_000_000m, 990_000m);
        await fixture.ImportStatementAsync(account, Clock(), [Fee(-5_000m, 195_000m)], 200_000m, 195_000m);
        var service = Service(fixture);
        var first = await service.CreateAsync(fixture.Admin.Id, fixture.Account.Id, fixture.Session.SessionDate, fixture.Session.SessionDate);
        var second = await service.CreateAsync(fixture.Admin.Id, account.Id, fixture.Session.SessionDate, fixture.Session.SessionDate);
        first = await ClassifyAsync(service, fixture, first, BankTransactionClassification.Fee);
        second = await ClassifyAsync(service, fixture, second, BankTransactionClassification.Fee);
        return (service, first, second, closing.Id);
    }

    private static Task<BankReconciliationReviewDto> ClassifyAsync(
        BankReconciliationService service, BankingTestContext fixture, BankReconciliationReviewDto review,
        BankTransactionClassification classification) =>
        service.ClassifyAsync(fixture.Admin.Id, review.Id, new BankClassificationForm
        {
            ExpectedReviewVersion = review.Version, BankTransactionId = review.BankEntries.Single().Id,
            EvidenceFileId = review.BankEntries.Single().EvidenceFileId, Classification = classification,
            Reason = "The statement identifies this bank's own fee."
        });

    private static Task<BankReconciliationReviewDto> ApproveAsync(
        BankReconciliationService service, BankingTestContext fixture, BankReconciliationReviewDto review) =>
        service.ApproveClosingAsync(fixture.Admin.Id, review.Id, fixture.Session.Id,
            Guid.NewGuid(), review.Version, review.Sessions.Single().BasisHash);

    private static ParsedStatementEntry Fee(decimal amount, decimal balance) => new(
        "synthetic row", "synthetic fee", "FEE", "FEE", null, "Bank fee", "UGX",
        amount, balance, BankingScenario.Now, null, null, true);

    private static BankReconciliationService Service(BankingTestContext fixture) =>
        new(fixture.Db, new BankingAccessService(fixture.Db, Clock()), Clock(), new SessionBalanceService(fixture.Db, Clock()));

    private static BusinessClock Clock() => new(new FixedTimeProvider());
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => BankingScenario.Now.AddDays(1);
    }
}
