using System.Text;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Features.ClosingReconciliation;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.Vaults;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class ClosingReconciliationTests
{
    private static IBusinessClock Clock() => new BusinessClock(new ScenarioTimeProvider(BankingScenario.Now.AddDays(1)));
    private static ClosingReconciliationService Service(BankingScenario fixture) => new(fixture.Db,
        new BankingAccessService(fixture.Db, Clock()), Clock(), new SessionBalanceService(fixture.Db, Clock()),
        Mock.Of<ISessionSettlementService>());
    private static DailyBankStatementService Uploads(BankingScenario fixture) => new(fixture.Db,
        new BankingAccessService(fixture.Db, Clock()), Clock(), new UniversalBankCsvStatementProfile(),
        Options.Create(new BankEvidenceOptions()));
    private static string Csv(BankingScenario fixture, decimal opening = 1_000_000m, decimal change = 0m) =>
        BankingScenario.StatementCsv(fixture.Session.SessionDate, opening, change);

    [Fact]
    public async Task DerivedStatement_UsesWalletSnapshotAndRetainsEvidenceAfterPermissionChanges()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.AllowMissingRunningBalances = true;
        var second = fixture.AddWallet("Other bank", WalletTypeEnum.Bank, 200_000m);
        var secondAccount = fixture.BindBankAccount(second);
        fixture.AddApprovedOpening(additionalWallets: [second]);
        var openingId = await fixture.Db.CashCounts.Where(c => c.IsOpening).Select(c => c.Id).SingleAsync();
        var closing = fixture.AddSubmittedClosing();
        fixture.BankWallet.Balance = 999m;
        (await fixture.Db.CashCountDetails.SingleAsync(d => d.WalletId == fixture.BankWallet.Id)).Amount = 123m;
        await fixture.Db.SaveChangesAsync();
        var csv = BankingScenario.StatementCsv(fixture.Session.SessionDate, 0m, includeBalances: false);

        var uploaded = await fixture.UploadStatementAsync(fixture.Account, Clock(), csv);
        await fixture.UploadStatementAsync(secondAccount, Clock(), Csv(fixture, 200_000m));
        Assert.Equal(StatementBalanceSource.ApprovedOpening, uploaded.BalanceSource);
        Assert.Equal(openingId, uploaded.OpeningCountId);
        Assert.Equal(1_000_000m, uploaded.OpeningBalance);
        Assert.Equal(1_000_000m, uploaded.ClosingBalance);
        var statement = await fixture.Db.DailyBankStatements.AsNoTracking().Include(s => s.Rows)
            .SingleAsync(s => s.Id == uploaded.Id);
        Assert.Equal(csv, Encoding.UTF8.GetString(statement.Content));
        Assert.Equal(new[] { 999_999m, 1_000_000m }, statement.Rows.OrderBy(r => r.Sequence).Select(r => r.RunningBalance));
        Assert.All(statement.Rows, r => Assert.Null(r.TransactionTime));
        fixture.Account.AllowMissingRunningBalances = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(uploaded.Id, (await fixture.UploadStatementAsync(fixture.Account, Clock(), csv)).Id);
        var report = (await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!;
        Assert.True(report.Succeeded);
        var result = report.Wallets.Single(w => w.StatementId == uploaded.Id);
        Assert.Equal(StatementBalanceSource.ApprovedOpening, result.BalanceSource);
        Assert.Equal(openingId, result.OpeningCountId);
        Assert.Contains("no bank-reported balances", result.Explanation);
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        var approved = await Service(fixture).ApproveAsync(supervisor.Id, closing.Id, false);
        Assert.Equal(CashCountStatus.Approved, approved.Status);
        Assert.False(approved.Approval!.IsException);
        Assert.Equal(999m, (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance);
        Assert.Empty(await fixture.Db.VaultTransactions.ToListAsync());
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("missing")]
    [InlineData("unapproved")]
    [InlineData("different-date")]
    [InlineData("missing-wallet")]
    [InlineData("different-agent")]
    public async Task DerivedStatement_RequiresOptInAndSameWalletApprovedOpeningAtUpload(string missing)
    {
        using var fixture = new BankingTestContext();
        fixture.Account.AllowMissingRunningBalances = missing != "disabled";
        if (missing != "missing")
        {
            fixture.AddApprovedOpening(includeBank: missing != "missing-wallet");
            var opening = await fixture.Db.CashCounts.SingleAsync(c => c.IsOpening);
            if (missing == "unapproved") opening.Status = CashCountStatus.PendingApproval;
            if (missing == "different-date") opening.CountDate = opening.CountDate.AddDays(-1);
            if (missing == "different-agent")
            {
                var user = AddReviewer(fixture, UserRole.Agent);
                var agent = new Agent { UserId = user.Id, BranchId = fixture.Agent.BranchId, Code = "OTHR", CreatedAt = BankingScenario.Now };
                fixture.Db.Agents.Add(agent);
                await fixture.Db.SaveChangesAsync();
                opening.AgentId = agent.Id;
            }
        }
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BankingRuleException>(() => fixture.UploadStatementAsync(
            fixture.Account, Clock(), BankingScenario.StatementCsv(fixture.Session.SessionDate, 0m, includeBalances: false)));

        Assert.Contains(missing == "disabled" ? "explicitly allow" : "approved opening", error.Message);
        Assert.Empty(await fixture.Db.DailyBankStatements.ToListAsync());
        Assert.Empty(await fixture.Db.DailyBankStatementRows.ToListAsync());
    }

    [Fact]
    public async Task OptedInAccount_PopulatedStatementNeedsNoOpeningAtUploadAndKeepsBankAnchors()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.AllowMissingRunningBalances = true;
        await fixture.Db.SaveChangesAsync();
        var statement = await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, 1_000_100m));
        Assert.Equal(StatementBalanceSource.BankReported, statement.BalanceSource);
        Assert.Null(statement.OpeningCountId);
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();

        var result = (await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!.Wallets.Single();

        Assert.False(result.Succeeded);
        Assert.Equal(0m, result.Difference);
        Assert.Contains("statement opening differs", result.Explanation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DerivedStatement_LegacyOpeningWithoutSnapshot_RequiresExplicitReplacementIfBaselineChanges(bool changeOpening)
    {
        using var fixture = new BankingTestContext();
        fixture.Account.AllowMissingRunningBalances = true;
        var opening = new CashCount
        {
            AgentId = fixture.Agent.Id, CashSessionId = fixture.Session.Id, CountDate = fixture.Session.SessionDate,
            IsOpening = true, Status = CashCountStatus.Approved, CreatedAt = BankingScenario.Now,
            Details =
            [
                new() { WalletId = fixture.CashWallet.Id, Amount = 100_000m },
                new() { WalletId = fixture.BankWallet.Id, Amount = 1_000_000m }
            ]
        };
        fixture.Db.CashCounts.Add(opening);
        await fixture.Db.SaveChangesAsync();
        var csv = BankingScenario.StatementCsv(fixture.Session.SessionDate, 0m, includeBalances: false);
        var statement = await fixture.UploadStatementAsync(fixture.Account, Clock(), csv);
        Assert.Equal(statement.Id, (await fixture.UploadStatementAsync(fixture.Account, Clock(), csv)).Id);
        Assert.Equal(opening.Id, statement.OpeningCountId);
        if (changeOpening) opening.Details.Single(d => d.WalletId == fixture.BankWallet.Id).Amount++;
        var closing = new CashCount
        {
            AgentId = fixture.Agent.Id, CashSessionId = fixture.Session.Id, CountDate = fixture.Session.SessionDate,
            Status = CashCountStatus.PendingApproval, CreatedAt = BankingScenario.Now,
            Details =
            [
                new() { WalletId = fixture.CashWallet.Id, Amount = 100_000m },
                new() { WalletId = fixture.BankWallet.Id, Amount = changeOpening ? 1_000_001m : 1_000_000m }
            ]
        };
        fixture.Db.CashCounts.Add(closing);
        await fixture.Db.SaveChangesAsync();

        if (changeOpening)
        {
            var error = await Assert.ThrowsAsync<BankingRuleException>(() =>
                Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id));
            Assert.Contains("cannot rebase", error.Message);
            Assert.Empty(await fixture.Db.ClosingReconciliationRuns.ToListAsync());
            const string reason = "The approved legacy opening changed.";
            var noReason = await Assert.ThrowsAsync<BankingRuleException>(() =>
                fixture.UploadStatementAsync(fixture.Account, Clock(), csv, statement.Id));
            Assert.Contains("requires a reason", noReason.Message);
            await Assert.ThrowsAsync<BankingConflictException>(() =>
                fixture.UploadStatementAsync(fixture.Account, Clock(), csv, reason: reason));

            var replacement = await fixture.UploadStatementAsync(fixture.Account, Clock(), csv, statement.Id, reason);
            Assert.Equal(2, replacement.Revision);
            Assert.Equal(opening.Id, replacement.OpeningCountId);
            Assert.Equal(1_000_001m, replacement.OpeningBalance);
            Assert.Equal(replacement.Id, (await fixture.UploadStatementAsync(fixture.Account, Clock(), csv)).Id);
            var original = await fixture.Db.DailyBankStatements.AsNoTracking().SingleAsync(s => s.Id == statement.Id);
            Assert.Equal(1_000_000m, original.OpeningBalance);
            var report = (await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!;
            Assert.True(report.Succeeded);
            Assert.Equal(replacement.Id, Assert.Single(report.Wallets).StatementId);
        }
        else
        {
            Assert.True((await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!.Succeeded);
        }
    }

    [Fact]
    public async Task DerivedStatement_ArithmeticDifferenceStillNeedsSeparateAdminException()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.AllowMissingRunningBalances = true;
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(),
            BankingScenario.StatementCsv(fixture.Session.SessionDate, 0m, 50m, includeBalances: false));
        var service = Service(fixture);
        var result = (await service.ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!.Wallets.Single();
        Assert.False(result.Succeeded);
        Assert.Equal(-50m, result.Difference);
        Assert.Contains("no bank-reported balances", result.Explanation);
        await Assert.ThrowsAsync<BankingRuleException>(() => service.ApproveAsync(fixture.Admin.Id, closing.Id, false));
        var approved = await service.ApproveAsync(fixture.Admin.Id, closing.Id, true, "Reviewed the arithmetic difference.", true);
        Assert.False(approved.Report!.Succeeded);
        Assert.True(approved.Approval!.IsException);
    }

    [Fact]
    public async Task DerivedStatement_ReplacementKeepsOriginalBaselineAndPolicyOnlyAffectsNewUploads()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.AllowMissingRunningBalances = true;
        fixture.AddApprovedOpening();
        var first = await fixture.UploadStatementAsync(fixture.Account, Clock(),
            BankingScenario.StatementCsv(fixture.Session.SessionDate, 0m, includeBalances: false));
        fixture.Account.AllowMissingRunningBalances = false;
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BankingRuleException>(() => fixture.UploadStatementAsync(fixture.Account, Clock(),
            BankingScenario.StatementCsv(fixture.Session.SessionDate, 0m, 50m, includeBalances: false),
            first.Id, "A new complete statement export."));
        var replacement = await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture),
            first.Id, "Bank supplied balances in a new export.");

        Assert.Equal(StatementBalanceSource.BankReported, replacement.BalanceSource);
        Assert.Null(replacement.OpeningCountId);
        var original = await fixture.Db.DailyBankStatements.AsNoTracking().SingleAsync(s => s.Id == first.Id);
        Assert.Equal(StatementBalanceSource.ApprovedOpening, original.BalanceSource);
        Assert.Equal(first.OpeningCountId, original.OpeningCountId);
        Assert.Equal(2, await fixture.Db.DailyBankStatements.CountAsync());
    }

    [Fact]
    public async Task AllBanksRunTogether_RequireSeparateApproval_AndNeverMoveFundsAgain()
    {
        using var fixture = new BankingTestContext();
        var second = fixture.AddWallet("Second bank", WalletTypeEnum.Bank, 200_000m);
        var account = fixture.BindBankAccount(second);
        var mobile = fixture.AddWallet("Mobile", WalletTypeEnum.MobileMoney, 300_000m);
        fixture.AddApprovedOpening(withSettlement: true, additionalWallets: [second, mobile]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        await fixture.UploadStatementAsync(account, Clock(), Csv(fixture, 200_000m));
        var balances = await fixture.Db.Wallets.ToDictionaryAsync(w => w.Id, w => w.Balance);
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        var service = Service(fixture);

        var result = await service.ReconcileAsync(fixture.Admin.Id, closing.Id);

        Assert.True(result.Report!.Succeeded);
        Assert.Equal(2, result.Report.Wallets.Count);
        Assert.All(result.Report.Wallets, w => Assert.True(w.Succeeded));
        Assert.Equal(CashCountStatus.PendingApproval, result.Status);
        var approved = await service.ApproveAsync(supervisor.Id, closing.Id, false);
        Assert.Equal(CashCountStatus.Approved, approved.Status);
        Assert.False(approved.Approval!.IsException);
        Assert.Equal(result.Report.Id, (await service.ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!.Id);
        await service.ApproveAsync(supervisor.Id, closing.Id, false);
        Assert.Single(await fixture.Db.ClosingApprovalDecisions.ToListAsync());
        Assert.Single(await fixture.Db.Notifications.Where(n => n.Type == NotificationType.CountApproved).ToListAsync());
        Assert.Empty(await fixture.Db.VaultTransactions.ToListAsync());
        foreach (var wallet in await fixture.Db.Wallets.ToListAsync())
            Assert.Equal(balances[wallet.Id], wallet.Balance);
    }

    [Fact]
    public async Task MissingOneStatement_BlocksEntireRunWithoutPartialResults()
    {
        using var fixture = new BankingTestContext();
        var second = fixture.AddWallet("Missing bank", WalletTypeEnum.Bank, 200_000m);
        fixture.BindBankAccount(second);
        fixture.AddApprovedOpening(additionalWallets: [second]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        var error = await Assert.ThrowsAsync<BankingRuleException>(() => Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id));
        Assert.Contains("Missing bank", error.Message);
        Assert.Empty(await fixture.Db.ClosingReconciliationRuns.ToListAsync());
        Assert.Empty(await fixture.Db.ClosingWalletResults.ToListAsync());
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_RemainsFailedAfterAuditedAdminException(bool offsettingDifferences)
    {
        using var fixture = new BankingTestContext();
        var second = fixture.AddWallet("Second bank", WalletTypeEnum.Bank, 200_000m);
        var account = fixture.BindBankAccount(second);
        fixture.AddApprovedOpening(additionalWallets: [second]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, change: 50m));
        await fixture.UploadStatementAsync(account, Clock(), Csv(fixture, 200_000m, offsettingDifferences ? -50m : 0m));
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        var service = Service(fixture);
        var report = (await service.ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!;
        Assert.False(report.Succeeded);
        Assert.Equal(offsettingDifferences ? 2 : 1, report.Wallets.Count(w => !w.Succeeded));
        await Assert.ThrowsAsync<BankingRuleException>(() => service.ApproveAsync(fixture.Admin.Id, closing.Id, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveAsync(supervisor.Id, closing.Id, true, "Investigated bank difference.", true));
        await Assert.ThrowsAsync<BankingRuleException>(() =>
            service.ApproveAsync(fixture.Admin.Id, closing.Id, true, "short", true));
        var approved = await service.ApproveAsync(fixture.Admin.Id, closing.Id, true, "Investigated bank difference.", true);
        Assert.Equal(CashCountStatus.Approved, approved.Status);
        Assert.True(approved.Approval!.IsException);
        Assert.Equal("Investigated bank difference.", approved.Approval.Reason);
        Assert.False(approved.Report!.Succeeded);
        Assert.Equal(report.Id, approved.Report.Id);
    }

    [Fact]
    public async Task OffsetStatementBalances_FailEvenWhenNetMovementsAndSubmittedClosingAgree()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, 1_000_100m));
        var result = (await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!.Wallets.Single();
        Assert.False(result.Succeeded);
        Assert.Equal(0m, result.Difference);
        Assert.Contains("statement opening differs", result.Explanation);
    }

    [Theory]
    [InlineData("wrong-date")]
    [InlineData("multiple-dates")]
    [InlineData("empty")]
    [InlineData("bad-balance")]
    public async Task InvalidStatement_IsRejectedAtomically(string problem)
    {
        using var fixture = new BankingTestContext();
        var csv = Csv(fixture);
        csv = problem switch
        {
            "wrong-date" => csv.Replace("2026-11-02", "2026-11-01"),
            "multiple-dates" => csv + "2026-11-03,,OTHER,Another day,,1.00,1000001.00\n",
            "empty" => csv[..(csv.IndexOf('\n') + 1)],
            "bad-balance" => csv.Replace("1000000.00", "1000001.00"),
            _ => throw new ArgumentOutOfRangeException(nameof(problem))
        };
        await Assert.ThrowsAsync<BankingRuleException>(() => fixture.UploadStatementAsync(fixture.Account, Clock(), csv));
        Assert.Empty(await fixture.Db.DailyBankStatements.ToListAsync());
        Assert.Empty(await fixture.Db.DailyBankStatementRows.ToListAsync());
    }

    [Fact]
    public async Task ReplacementRetainsHistory_ReplayIsSafe_AndUsedInputsAreLocked()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        var first = await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, change: 1));
        Assert.Equal(first.Id, (await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, change: 1))).Id);
        await Assert.ThrowsAsync<BankingRuleException>(() =>
            fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture), first.Id, "short"));
        var second = await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture), first.Id, "Correct complete bank export.");
        Assert.Equal(2, second.Revision);
        var report = (await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!;
        Assert.Equal(second.Id, report.Wallets.Single().StatementId);
        Assert.True(report.Succeeded);
        await Assert.ThrowsAsync<BankingRuleException>(() =>
            fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, change: 2), second.Id, "A later attempted replacement."));
        Assert.Equal(2, await fixture.Db.DailyBankStatements.CountAsync());
        Assert.All(await fixture.Db.DailyBankStatementRows.ToListAsync(), r => Assert.Null(r.TransactionTime));
        var stored = await fixture.Db.DailyBankStatements.SingleAsync(s => s.Id == second.Id);
        stored.TotalCredits++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Agent)]
    public async Task NonAdmin_CannotUploadOrRun(UserRole role)
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        var user = role == UserRole.Agent ? fixture.User : AddReviewer(fixture, role);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(Csv(fixture)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Uploads(fixture).UploadAsync(user.Id,
            new DailyStatementUpload(fixture.Account.Id, fixture.Session.SessionDate, true, null, null), "synthetic.csv", content));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(fixture).ReconcileAsync(user.Id, closing.Id));
        if (role == UserRole.Agent)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(fixture).GetAsync(user.Id, closing.Id));
        else
            Assert.Equal(closing.Id, (await Service(fixture).GetAsync(user.Id, closing.Id)).ClosingCountId);
    }

    [Fact]
    public async Task MissingApprovedOpening_CannotBeOverriddenByException()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        (await fixture.Db.CashCounts.SingleAsync(c => c.IsOpening)).Status = CashCountStatus.PendingApproval;
        await fixture.Db.SaveChangesAsync();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        await Assert.ThrowsAsync<BankingRuleException>(() => Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id));
        await Assert.ThrowsAsync<BankingRuleException>(() =>
            Service(fixture).ApproveAsync(fixture.Admin.Id, closing.Id, true, "Attempt to override missing opening.", true));
        Assert.Empty(await fixture.Db.ClosingReconciliationRuns.ToListAsync());
    }

    [Fact]
    public async Task CapturedWalletsRemainRequired_WhenAccountIsPausedAndLiveBalancesChange()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        fixture.Account.WorkflowEnabled = false;
        fixture.Account.IsActive = false;
        fixture.BankWallet.Balance = 123m;
        await fixture.Db.SaveChangesAsync();
        var result = await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id);
        Assert.True(result.Report!.Succeeded);
        Assert.Equal(1_000_000m, result.Report.Wallets.Single().SubmittedClosing);
        Assert.Equal(123m, (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance);
    }

    [Fact]
    public async Task OperationallyClosedLegacySession_StillShowsPendingClosingToAgent()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.SessionDate = DateOnly.FromDateTime(DateTime.UtcNow);
        fixture.Session.WorkflowVersion = SessionWorkflowVersion.Legacy;
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        var service = new CashCountService(fixture.Db, Mock.Of<IVaultService>(),
            Mock.Of<INotificationService>(), Mock.Of<IWalletAdjustmentService>(), Mock.Of<IBankSessionWorkflow>());

        var current = await service.GetCurrentSessionAsync(fixture.User.Id);

        Assert.Equal("Session closed", current.StatusText);
        Assert.True(current.HasClosingCount);
        Assert.True(current.HasPendingApproval);
        Assert.Equal(closing.Id, current.ClosingCountId);
        Assert.Equal(CashCountStatus.PendingApproval, current.ClosingCountStatus);
        Assert.False(current.CanPerformOpeningCount);
        Assert.False(current.CanPerformClosingCount);
        Assert.False(current.CanRecordBankRun);
        Assert.False(current.CanRecordAdjustment);
    }

    [Fact]
    public async Task SuccessfulReport_DoesNotAllowSelfApproval()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id);
        fixture.User.Role = UserRole.Admin;
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BankingRuleException>(() =>
            Service(fixture).ApproveAsync(fixture.User.Id, closing.Id, false));

        Assert.Contains("your own closing", error.Message);
        Assert.Empty(await fixture.Db.ClosingApprovalDecisions.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approval_CannotOverrideNonBankDifferences(bool exception)
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing(new Dictionary<long, decimal> { [fixture.CashWallet.Id] = 100_001m });
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, change: exception ? 10m : 0m));
        await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id);

        var error = await Assert.ThrowsAsync<BankingRuleException>(() =>
            Service(fixture).ApproveAsync(fixture.Admin.Id, closing.Id, exception, "Investigated bank difference.", true));

        Assert.Contains("Cash or Mobile Money", error.Message);
        Assert.Empty(await fixture.Db.ClosingApprovalDecisions.ToListAsync());
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.Status);
    }

    [Fact]
    public async Task CashOnlyModernClosing_IsApprovedWithoutRunOrExceptionAndClosesSession()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening(includeBank: false);
        var closing = fixture.AddSubmittedClosing();
        fixture.Session.Status = CashSessionStatus.Open;
        fixture.Session.ClosedAt = null;
        await fixture.Db.SaveChangesAsync();
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(fixture).ApproveAsync(supervisor.Id, closing.Id, true, "Not applicable to cash.", true));
        var exception = await Assert.ThrowsAsync<BankingRuleException>(() =>
            Service(fixture).ApproveAsync(fixture.Admin.Id, closing.Id, true, "Not applicable to cash.", true));
        var approved = await Service(fixture).ApproveAsync(supervisor.Id, closing.Id, false);
        var replay = await Service(fixture).ApproveAsync(supervisor.Id, closing.Id, false);

        Assert.Contains("not required", exception.Message);
        Assert.Equal(CashCountStatus.Approved, approved.Status);
        Assert.Equal(CashCountStatus.Approved, replay.Status);
        Assert.Empty(await fixture.Db.ClosingApprovalDecisions.ToListAsync());
        Assert.Empty(await fixture.Db.ClosingReconciliationRuns.ToListAsync());
        var audit = await fixture.Db.CashCountAuditLogs.SingleAsync(l => l.CashCountId == closing.Id &&
            l.Action == CashCountAuditAction.Approved);
        Assert.Contains("not required", audit.Notes);
        Assert.Equal(CashSessionStatus.Closed, (await fixture.Db.CashSessions.FindAsync(fixture.Session.Id))!.Status);
    }

    [Fact]
    public async Task CashOnlyModernClosing_CannotBeSelfApproved()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening(includeBank: false);
        var closing = fixture.AddSubmittedClosing();
        fixture.User.Role = UserRole.Supervisor;
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BankingRuleException>(() =>
            Service(fixture).ApproveAsync(fixture.User.Id, closing.Id, false));

        Assert.Contains("your own closing", error.Message);
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.Status);
    }

    [Fact]
    public async Task ExplainedCashVariance_IsReviewedAndApprovedWithTheClosing()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening(includeBank: false);
        var closing = fixture.AddSubmittedClosing(new Dictionary<long, decimal> { [fixture.CashWallet.Id] = 90_000m });
        closing.Explanation = "Counted ten thousand short after the final deposit.";
        fixture.Db.Discrepancies.Add(new Discrepancy
        {
            CashSessionId = fixture.Session.Id, CashCountId = closing.Id, Status = DiscrepancyStatus.PendingReview,
            ExpectedAmount = 100_000m, ActualAmount = 90_000m, Variance = -10_000m,
            Explanation = closing.Explanation, ExplainedAt = BankingScenario.Now, CreatedAt = BankingScenario.Now
        });
        await fixture.Db.SaveChangesAsync();
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);

        var approved = await Service(fixture).ApproveAsync(supervisor.Id, closing.Id, false);

        Assert.Equal(CashCountStatus.Approved, approved.Status);
        var discrepancy = await fixture.Db.Discrepancies.SingleAsync();
        Assert.Equal(DiscrepancyStatus.Approved, discrepancy.Status);
        Assert.Equal(supervisor.Id, discrepancy.ApprovedByUserId);
    }

    [Fact]
    public async Task RenamingABankWalletAfterReconciliation_DoesNotInvalidateApproval()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        await Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id);
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Name = "Renamed bank wallet";
        await fixture.Db.SaveChangesAsync();

        var approved = await Service(fixture).ApproveAsync(supervisor.Id, closing.Id, false);

        Assert.Equal(CashCountStatus.Approved, approved.Status);
    }

    private static void Waive(BankingScenario fixture, Wallet wallet, bool required = false)
    {
        var type = fixture.Db.WalletTypes.Single(t => t.Id == wallet.WalletTypeId);
        type.RequiresStatementReconciliation = required;
        fixture.Db.SaveChanges();
    }

    [Fact]
    public async Task WaivedWallet_NeedsNoStatement_IsReportedNotReconciled_AndOrdinaryApprovalWorks()
    {
        using var fixture = new BankingTestContext();
        var second = fixture.AddWallet("Waived bank", WalletTypeEnum.Bank, 200_000m);
        fixture.BindBankAccount(second);
        Waive(fixture, second);
        fixture.AddApprovedOpening(additionalWallets: [second]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        var service = Service(fixture);

        var before = await service.GetAsync(fixture.Admin.Id, closing.Id);
        Assert.Contains(before.Wallets, w => w.WalletName == "Waived bank" && !w.StatementRequired);

        var report = (await service.ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!;

        Assert.True(report.Succeeded);
        var waived = Assert.Single(report.Wallets, w => w.StatementWaived);
        Assert.Equal("Waived bank", waived.WalletName);
        Assert.Null(waived.StatementId);
        Assert.Null(waived.StatementRevision);
        Assert.Single(report.Wallets, w => !w.StatementWaived && w.StatementId.HasValue);
        var approved = await service.ApproveAsync(supervisor.Id, closing.Id, false);
        Assert.Equal(CashCountStatus.Approved, approved.Status);
        Assert.False(approved.Approval!.IsException);
    }

    [Fact]
    public async Task AllBanksWaived_AdminStillRuns_SupervisorCannot_AndUploadedStatementIsIgnored()
    {
        using var fixture = new BankingTestContext();
        Waive(fixture, fixture.Db.Wallets.Include(w => w.WalletType).First(w => w.WalletType!.Type == WalletTypeEnum.Bank));
        fixture.AddApprovedOpening();
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, change: 50m));
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        var service = Service(fixture);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReconcileAsync(supervisor.Id, closing.Id));
        var report = (await service.ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!;

        Assert.True(report.Succeeded);
        Assert.All(report.Wallets, w => Assert.True(w.StatementWaived));
        Assert.Equal(CashCountStatus.Approved, (await service.ApproveAsync(supervisor.Id, closing.Id, false)).Status);
    }

    [Fact]
    public async Task WaivedWallet_StillRequiresBankAccountBinding()
    {
        using var fixture = new BankingTestContext();
        var second = fixture.AddWallet("Unbound waived", WalletTypeEnum.Bank, 200_000m);
        Waive(fixture, second);
        fixture.AddApprovedOpening(additionalWallets: [second]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        var error = await Assert.ThrowsAsync<BankingRuleException>(() => Service(fixture).ReconcileAsync(fixture.Admin.Id, closing.Id));
        Assert.Contains("binding", error.Message);
    }

    [Fact]
    public async Task FailedRequiredWallet_WithWaivedWallet_StillNeedsAdminException()
    {
        using var fixture = new BankingTestContext();
        var second = fixture.AddWallet("Waived bank", WalletTypeEnum.Bank, 200_000m);
        fixture.BindBankAccount(second);
        Waive(fixture, second);
        fixture.AddApprovedOpening(additionalWallets: [second]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture, change: 50m));
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        var service = Service(fixture);
        var report = (await service.ReconcileAsync(fixture.Admin.Id, closing.Id)).Report!;
        Assert.False(report.Succeeded);
        Assert.Single(report.Wallets, w => !w.Succeeded);
        await Assert.ThrowsAsync<BankingRuleException>(() => service.ApproveAsync(supervisor.Id, closing.Id, false));
        Assert.True((await service.ApproveAsync(fixture.Admin.Id, closing.Id, true, "Investigated bank difference.", true)).Approval!.IsException);
    }

    [Fact]
    public async Task TogglingAfterRun_DoesNotChangeSavedReport_OrBlockApproval()
    {
        using var fixture = new BankingTestContext();
        var second = fixture.AddWallet("Waived bank", WalletTypeEnum.Bank, 200_000m);
        fixture.BindBankAccount(second);
        Waive(fixture, second);
        fixture.AddApprovedOpening(additionalWallets: [second]);
        var closing = fixture.AddSubmittedClosing();
        await fixture.UploadStatementAsync(fixture.Account, Clock(), Csv(fixture));
        var supervisor = AddReviewer(fixture, UserRole.Supervisor);
        var service = Service(fixture);
        await service.ReconcileAsync(fixture.Admin.Id, closing.Id);

        Waive(fixture, second, required: true);

        var review = await service.GetAsync(fixture.Admin.Id, closing.Id);
        Assert.Single(review.Report!.Wallets, w => w.StatementWaived);
        Assert.Equal(CashCountStatus.Approved, (await service.ApproveAsync(supervisor.Id, closing.Id, false)).Status);
    }

    private static ApplicationUser AddReviewer(BankingScenario fixture, UserRole role)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(), UserName = "synthetic-reviewer-" + Guid.NewGuid(),
            FirstName = "Synthetic", LastName = "Reviewer", BranchId = fixture.Admin.BranchId,
            Role = role, IsActive = true
        };
        fixture.Db.Users.Add(user);
        fixture.Db.SaveChanges();
        return user;
    }
}
