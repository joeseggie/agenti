using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EastSeat.Agenti.IntegrationTests;

public sealed class ReconciliationPostgresTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ConcurrentCaptures_ReplayOneCommandWithoutChangingWallets()
    {
        using var scenario = new BankingScenario(DbContext);
        scenario.AddApprovedOpening();
        var form = Capture(scenario);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<long> Submit()
        {
            await using var db = Fixture.CreateContext();
            await start.Task;
            for (var attempt = 0; ; attempt++)
            {
                var clock = new BusinessClock(new BankingTestClock());
                try
                {
                    return (await new CustomerTransactionService(db, new BankingAccessService(db, clock), clock)
                        .CaptureAsync(scenario.User.Id, form)).Id;
                }
                catch (Exception exception) when (attempt < 3 && BankingConcurrency.IsConflict(exception))
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
        var first = Submit();
        var second = Submit();
        start.SetResult();

        var ids = await Task.WhenAll(first, second);

        Assert.Equal(ids[0], ids[1]);
        await using var verify = Fixture.CreateContext();
        Assert.Equal(1, await verify.CustomerTransactions.CountAsync());
        Assert.Equal(1, await verify.CustomerMovements.CountAsync());
        Assert.Equal(100_000m, (await verify.Wallets.FindAsync(scenario.CashWallet.Id))!.Balance);
    }

    [Fact]
    public async Task ConcurrentClosingSubmissions_SettleOnceAndLeaveFinancialApprovalPending()
    {
        using var scenario = new BankingScenario(DbContext);
        scenario.AddApprovedOpening(withSettlement: true);
        var clock = new BusinessClock(new BankingTestClock());
        var workflow = Workflow(DbContext, clock);
        var form = await workflow.InitializeFormAsync(scenario.User.Id, false);
        foreach (var entry in form.WalletEntries)
        {
            entry.CountedAmount = entry.ExpectedBalance;
            entry.Denominations = null;
            entry.ObservedAt = clock.UtcNow;
        }
        var saved = await workflow.SaveAsync(scenario.User.Id, form);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Submit()
        {
            await using var db = Fixture.CreateContext();
            await start.Task;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var result = await Workflow(db, clock).SubmitAsync(scenario.User.Id, saved.CashCountId!.Value);
                    Assert.True(result.Success, result.ErrorMessage);
                    return;
                }
                catch (Exception exception) when (attempt < 3 && BankingConcurrency.IsConflict(exception))
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
        var first = Submit();
        var second = Submit();
        start.SetResult();
        await Task.WhenAll(first, second);

        await using var verify = Fixture.CreateContext();
        Assert.Equal(1, await verify.SessionFloatSettlements.CountAsync(s => s.Stage == FloatSettlementStage.Closing));
        Assert.Equal(1_100_000m, (await verify.Vaults.SingleAsync()).CurrentBalance);
        Assert.All(await verify.Wallets.ToListAsync(), wallet => Assert.Equal(0m, wallet.Balance));
        Assert.Equal(CashCountStatus.PendingApproval, (await verify.CashCounts.FindAsync(saved.CashCountId))!.Status);
        Assert.NotNull((await verify.CashSessions.FindAsync(scenario.Session.Id))!.OperationalClosedAt);
    }

    [Fact]
    public async Task PopulatedLegacyMigration_PreservesAmountsAndDoesNotInventSettlements()
    {
        // A separate database keeps migration rehearsal isolated from other test fixtures.
        var original = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString);
        var databaseName = "agenti_migration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        original.Database = databaseName;
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(original.ConnectionString).Options);
            var migrations = db.Database.GetMigrations().ToList();
            var foundation = migrations.FindIndex(m => m.EndsWith("_AddBankReconciliationFoundation", StringComparison.Ordinal));
            Assert.True(foundation > 0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[foundation - 1]);
            await using (var connection = new NpgsqlConnection(original.ConnectionString))
            {
                await connection.OpenAsync();
                await using var seed = new NpgsqlCommand("""
                    INSERT INTO "Branches" ("Name", "CreatedAt") VALUES ('Migration fixture', CURRENT_TIMESTAMP);
                    INSERT INTO "Vaults" ("BranchId", "CurrentBalance", "CreatedAt")
                    SELECT "Id", 123456.78, CURRENT_TIMESTAMP FROM "Branches";
                    INSERT INTO "CashSessions" ("BranchId", "SessionDate", "Status", "OpenedAt", "ClosedAt")
                    SELECT "Id", DATE '2026-10-01', 'Closed', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP FROM "Branches";
                    """, connection);
                await seed.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            var legacy = await db.CashSessions.SingleAsync();
            Assert.Equal(SessionWorkflowVersion.Legacy, legacy.WorkflowVersion);
            Assert.Equal(CashSessionStatus.Closed, legacy.Status);
            Assert.Null(legacy.OperationalClosedAt);
            Assert.Equal(123456.78m, (await db.Vaults.SingleAsync()).CurrentBalance);
            Assert.Empty(await db.SessionFloatSettlements.ToListAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static BankSessionWorkflow Workflow(ApplicationDbContext db, IBusinessClock clock)
    {
        var access = new BankingAccessService(db, clock);
        var balances = new SessionBalanceService(db, clock);
        return new(db, access, clock, balances, new SessionSettlementService(db, new VaultService(db), access, balances, clock));
    }

    internal static CustomerTransactionForm Capture(BankingScenario scenario) => new()
    {
        CommandId = Guid.NewGuid(), CashSessionId = scenario.Session.Id, BankAccountId = scenario.Account.Id,
        CashWalletId = scenario.CashWallet.Id, Kind = CustomerTransactionKind.Deposit,
        Outcome = CustomerCaptureOutcome.Completed, Amount = 50_000m, CustomerIdentifier = "0012345",
        BankReference = "SYNTHETIC-DEPOSIT", OccurredAt = BankingScenario.Now
    };
}
