using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Agents;
using EastSeat.Agenti.Web.Features.ClosingReconciliation;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.IntegrationTests;

public sealed class ReconciliationPostgresTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ConcurrentRunsAndApprovals_ProduceOneWholeClosingReportAndDecision()
    {
        using var scenario = new BankingScenario(DbContext);
        var otherBank = scenario.AddWallet("ABSA", WalletTypeEnum.Bank, 200_000m);
        var account = scenario.BindBankAccount(otherBank);
        account.AllowMissingRunningBalances = true;
        var mobile = scenario.AddWallet("Airtel", WalletTypeEnum.MobileMoney, 300_000m);
        scenario.AddApprovedOpening(withSettlement: true, additionalWallets: [otherBank, mobile]);
        var closing = scenario.AddSubmittedClosing();
        var clock = new BusinessClock(new BankingTestClock { Now = BankingScenario.Now.AddDays(1) });
        await scenario.UploadStatementAsync(scenario.Account, clock, BankingScenario.StatementCsv(scenario.Session.SessionDate, 1_000_000m));
        await scenario.UploadStatementAsync(account, clock,
            BankingScenario.StatementCsv(scenario.Session.SessionDate, 200_000m, includeBalances: false));
        ClosingReconciliationService Service(ApplicationDbContext db)
        {
            var access = new BankingAccessService(db, clock);
            var balances = new SessionBalanceService(db, clock);
            return new(db, access, clock, balances, new SessionSettlementService(db, new VaultService(db), access, balances, clock));
        }
        var balances = await DbContext.Wallets.ToDictionaryAsync(w => w.Id, w => w.Balance);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Approve()
        {
            await using var db = Fixture.CreateContext();
            await start.Task;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await Service(db).ReconcileAsync(scenario.Admin.Id, closing.Id);
                    await Service(db).ApproveAsync(scenario.Admin.Id, closing.Id, false);
                    return;
                }
                catch (Exception exception) when (attempt < 3 && BankingConcurrency.IsConflict(exception))
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
        var first = Approve();
        var second = Approve();
        start.SetResult();

        await Task.WhenAll(first, second);

        await using var verify = Fixture.CreateContext();
        Assert.Equal(1, await verify.ClosingReconciliationRuns.CountAsync());
        Assert.Equal(2, await verify.ClosingWalletResults.CountAsync());
        var derived = await verify.DailyBankStatements.SingleAsync(s => s.BankAccountId == account.Id);
        Assert.Equal(StatementBalanceSource.ApprovedOpening, derived.BalanceSource);
        Assert.Equal(await verify.CashCounts.Where(c => c.IsOpening).Select(c => c.Id).SingleAsync(), derived.OpeningCountId);
        Assert.Equal(1, await verify.ClosingApprovalDecisions.CountAsync());
        Assert.Equal(CashCountStatus.Approved, (await verify.CashCounts.FindAsync(closing.Id))!.Status);
        Assert.Equal(1, await verify.CashCountAuditLogs.CountAsync(a => a.CashCountId == closing.Id && a.Action == CashCountAuditAction.Approved));
        Assert.Equal(1, await verify.Notifications.CountAsync(n => n.Type == NotificationType.CountApproved));
        Assert.Equal(1, await verify.SessionFloatSettlements.CountAsync(s => s.Stage == FloatSettlementStage.Closing));
        Assert.Empty(await verify.VaultTransactions.ToListAsync());
        foreach (var wallet in await verify.Wallets.ToListAsync()) Assert.Equal(balances[wallet.Id], wallet.Balance);
    }

    [Fact]
    public async Task ConcurrentReplacementAndRun_FreezeExactlyTheAcceptedRevision()
    {
        using var scenario = new BankingScenario(DbContext);
        scenario.AddApprovedOpening();
        var closing = scenario.AddSubmittedClosing();
        var clock = new BusinessClock(new BankingTestClock { Now = BankingScenario.Now.AddDays(1) });
        var first = await scenario.UploadStatementAsync(scenario.Account, clock,
            BankingScenario.StatementCsv(scenario.Session.SessionDate, 1_000_000m, 1m));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Replace()
        {
            await using var db = Fixture.CreateContext();
            var uploads = new DailyBankStatementService(db, new BankingAccessService(db, clock), clock,
                new UniversalBankCsvStatementProfile(), Options.Create(new BankEvidenceOptions()));
            using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                BankingScenario.StatementCsv(scenario.Session.SessionDate, 1_000_000m)));
            await start.Task;
            try
            {
                await uploads.UploadAsync(scenario.Admin.Id,
                    new DailyStatementUpload(scenario.Account.Id, scenario.Session.SessionDate, true, first.Id,
                        "Corrected complete synthetic export."), "replacement.csv", content);
                return true;
            }
            catch (BankingRuleException error) when (error.Message.Contains("already been used"))
            {
                return false;
            }
            catch (Exception error) when (BankingConcurrency.IsConflict(error))
            {
                return false;
            }
        }
        async Task Reconcile()
        {
            await using var db = Fixture.CreateContext();
            var access = new BankingAccessService(db, clock);
            var balances = new SessionBalanceService(db, clock);
            var service = new ClosingReconciliationService(db, access, clock, balances,
                new SessionSettlementService(db, new VaultService(db), access, balances, clock));
            await start.Task;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await service.ReconcileAsync(scenario.Admin.Id, closing.Id);
                    return;
                }
                catch (Exception error) when (attempt < 3 && BankingConcurrency.IsConflict(error))
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
        var replacement = Replace();
        var reconciliation = Reconcile();
        start.SetResult();

        await Task.WhenAll(replacement, reconciliation);

        await using var verify = Fixture.CreateContext();
        var run = await verify.ClosingReconciliationRuns.Include(r => r.Wallets).SingleAsync();
        var latest = await verify.DailyBankStatements.OrderByDescending(s => s.Revision).FirstAsync();
        Assert.Equal(latest.Id, Assert.Single(run.Wallets).StatementId);
        Assert.Equal(await replacement, run.Succeeded);
        Assert.Equal(await replacement ? 2 : 1, await verify.DailyBankStatements.CountAsync());
        Assert.Empty(await verify.ClosingApprovalDecisions.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentCashAssignments_CannotCreateTwoCashCategoryWallets()
    {
        using var scenario = new BankingScenario(DbContext);
        DbContext.Wallets.Remove(scenario.CashWallet);
        var types = new[]
        {
            new WalletType { Name = $"Cash A-{Guid.NewGuid():N}", Type = WalletTypeEnum.Cash, IsActive = true },
            new WalletType { Name = $"Cash B-{Guid.NewGuid():N}", Type = WalletTypeEnum.Cash, IsActive = true }
        };
        DbContext.WalletTypes.AddRange(types);
        await DbContext.SaveChangesAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Assign(long typeId)
        {
            await using var db = Fixture.CreateContext();
            await start.Task;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return (await new AgentService(db).AddWalletAsync(new WalletFormModel
                    {
                        AgentId = scenario.Agent.Id, WalletTypeId = typeId, Name = "Cash", Currency = "UGX", IsActive = true
                    })).Success;
                }
                catch (Exception exception) when (attempt < 3 && BankingConcurrency.IsConflict(exception))
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
        var first = Assign(types[0].Id);
        var second = Assign(types[1].Id);
        start.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Single(results, r => r);
        await using var verify = Fixture.CreateContext();
        Assert.Equal(1, await verify.Wallets.CountAsync(w => w.AgentId == scenario.Agent.Id &&
            w.WalletType!.Type == WalletTypeEnum.Cash));
    }

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
            entry.CountedAmount = entry.WalletId == scenario.CashWallet.Id ? 100_000m : entry.ExpectedBalance;
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
    public async Task Reservations_AggregateOutstandingCasesWithoutNettingSettledCases()
    {
        using var scenario = new BankingScenario(DbContext);
        scenario.AddApprovedOpening();
        var deposit = scenario.AddCustomerTransaction();
        var withdrawal = scenario.AddCustomerTransaction();
        var settled = scenario.AddCustomerTransaction();
        var future = scenario.AddCustomerTransaction();
        CustomerMovement Movement(long id, CustomerTransactionKind kind, CustomerMovementKind movementKind,
            decimal cash, decimal bank, decimal settledAmount) => new()
        {
            CustomerTransactionId = id, CashSessionId = scenario.Session.Id, Kind = movementKind,
            TransactionKind = kind, CashEffect = cash, BankEffect = bank, CustomerSettledAmount = settledAmount,
            Amount = 50_000m, CommandId = Guid.NewGuid(), RecordedByUserId = scenario.User.Id,
            OccurredAt = BankingScenario.Now, RecordedAt = BankingScenario.Now
        };
        var futureMovement = Movement(future.Id, CustomerTransactionKind.Deposit,
            CustomerMovementKind.PendingAttempt, 50_000m, 0m, 0m);
        futureMovement.OccurredAt = BankingScenario.Now.AddDays(1);
        DbContext.CustomerMovements.AddRange(
            Movement(deposit.Id, CustomerTransactionKind.Deposit, CustomerMovementKind.FailedAttempt, 50_000m, -50_000m, 0m),
            Movement(withdrawal.Id, CustomerTransactionKind.Withdrawal, CustomerMovementKind.PendingAttempt, -10_000m, 40_000m, 10_000m),
            Movement(settled.Id, CustomerTransactionKind.Deposit, CustomerMovementKind.Completed, 50_000m, -50_000m, 50_000m),
            futureMovement);
        await DbContext.SaveChangesAsync();
        var clock = new BusinessClock(new BankingTestClock());

        var positions = await new SessionBalanceService(DbContext, clock)
            .GetPositionsAsync(scenario.Session.Id, scenario.Agent.Id);

        Assert.Equal(50_000m, positions.Single(p => p.WalletId == scenario.CashWallet.Id).ReservedCustomerCash);
        Assert.Equal(30_000m, positions.Single(p => p.WalletId == scenario.BankWallet.Id).ReservedBankSettlement);
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

    [Theory]
    [InlineData(CashCountStatus.Draft)]
    [InlineData(CashCountStatus.PendingApproval)]
    public async Task LegacyBankClosing_RequiresWholeReport_AndSettlesExactlyOnce(CashCountStatus initialStatus)
    {
        using var scenario = new BankingScenario(DbContext);
        scenario.Session.WorkflowVersion = SessionWorkflowVersion.Legacy;
        scenario.Account.WorkflowEnabled = false;
        (await DbContext.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        CashCount Count(bool opening) => new()
        {
            CashSessionId = scenario.Session.Id, AgentId = scenario.Agent.Id,
            IsOpening = opening, CountDate = scenario.Session.SessionDate,
            Status = opening ? CashCountStatus.Approved : initialStatus,
            TotalAmount = 1_100_000m, CreatedAt = BankingScenario.Now,
            SubmittedAt = !opening && initialStatus == CashCountStatus.PendingApproval ? BankingScenario.Now : null,
            Details =
            [
                new() { WalletId = scenario.CashWallet.Id, Amount = 100_000m },
                new() { WalletId = scenario.BankWallet.Id, Amount = 1_000_000m }
            ]
        };
        var closing = Count(false);
        DbContext.CashCounts.AddRange(Count(true), closing);
        await DbContext.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var scope = app.Services.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<ICashCountService>();
        var reconciliation = scope.ServiceProvider.GetRequiredService<IClosingReconciliationService>();
        if (initialStatus == CashCountStatus.Draft)
        {
            var submitted = await counts.SubmitCashCountAsync(scenario.User.Id, closing.Id);
            Assert.True(submitted.Success, submitted.ErrorMessage);
            Assert.Equal(CashCountStatus.PendingApproval, (await reconciliation.GetAsync(scenario.Admin.Id, closing.Id)).Status);
        }
        Assert.False((await counts.ApproveCashCountAsync(scenario.Admin.Id, closing.Id)).Success);
        var frozen = await counts.GetCashCountFormAsync(scenario.User.Id, closing.Id);
        Assert.True(frozen!.IsReadOnly);
        Assert.False((await counts.SaveCashCountAsync(scenario.User.Id, frozen)).Success);
        await scenario.UploadStatementAsync(scenario.Account, new BusinessClock(app.Clock),
            BankingScenario.StatementCsv(scenario.Session.SessionDate, 1_000_000m));
        var run = await reconciliation.ReconcileAsync(scenario.Admin.Id, closing.Id);
        Assert.True(run.Report!.Succeeded);
        await reconciliation.ApproveAsync(scenario.Admin.Id, closing.Id, false);
        await reconciliation.ApproveAsync(scenario.Admin.Id, closing.Id, false);
        await using var verify = Fixture.CreateContext();
        Assert.Equal(CashCountStatus.Approved, (await verify.CashCounts.FindAsync(closing.Id))!.Status);
        Assert.Equal(CashSessionStatus.Closed, (await verify.CashSessions.FindAsync(scenario.Session.Id))!.Status);
        Assert.Single(await verify.SessionFloatSettlements.Where(s => s.CashCountId == closing.Id).ToListAsync());
        Assert.Single(await verify.VaultTransactions.Where(v => v.CashSessionId == scenario.Session.Id).ToListAsync());
        Assert.Equal(1_100_000m, (await verify.Vaults.SingleAsync(v => v.BranchId == scenario.Agent.BranchId)).CurrentBalance);
        Assert.All(await verify.Wallets.Where(w => w.AgentId == scenario.Agent.Id).ToListAsync(), w => Assert.Equal(0m, w.Balance));
        if (initialStatus == CashCountStatus.PendingApproval)
            Assert.Equal(BankingScenario.Now, (await verify.CashCounts.FindAsync(closing.Id))!.SubmittedAt);
    }

    [Fact]
    public async Task ArchiveMigration_PreservesEvidenceAndCrossSchemaHistory_AndRejectsWritesAndRollback()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString);
        var databaseName = "agenti_archive_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        connectionString.Database = databaseName;
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString.ConnectionString).Options);
            var migrations = db.Database.GetMigrations().ToList();
            var cutover = migrations.FindIndex(m => m.EndsWith("_ReplaceClosingReconciliation", StringComparison.Ordinal));
            await db.GetService<IMigrator>().MigrateAsync(migrations[cutover - 1]);
            await using var legacy = new BeforeDerivedBalancesContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString.ConnectionString).Options);
            using var scenario = new BankingScenario(legacy);
            scenario.AddApprovedOpening();
            var closing = scenario.AddSubmittedClosing();
            closing.Status = CashCountStatus.Approved;
            var evidence = new BankEvidenceFile
            {
                BankAccountId = scenario.Account.Id, UploadKey = Guid.NewGuid(), IsStatement = true,
                FileName = "archived-synthetic.csv", ContentType = "text/csv", Content = [1, 2, 3],
                ContentHash = new string('a', 64), UploadedByUserId = scenario.Admin.Id, UploadedAt = BankingScenario.Now
            };
            legacy.BankEvidenceFiles.Add(evidence);
            await legacy.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "BankStatementImports"
                ("Id", "BankAccountId", "EvidenceFileId", "Status", "ProfileKey", "ProfileVersion", "Version", "Revision",
                 "CoverageVerified", "CoverageDeclaredComplete", "BalanceBasis", "ReportedAccountIdentifier", "Currency",
                 "UploadedByUserId", "CreatedAt", "OpeningBalance", "ClosingBalance")
                VALUES (900001, {scenario.Account.Id}, {evidence.Id}, 'Committed', 'archived-synthetic', 1, 1, 1,
                 true, true, 'Ledger', {scenario.Account.AccountIdentifier}, 'UGX', {scenario.Admin.Id},
                 {BankingScenario.Now}, 1000000.00, 950000.00);
                INSERT INTO "CashCountCorrectionRequests"
                ("Id", "CashCountId", "AgentId", "Reason", "AllowedMovementIdsJson", "AllowMissingTransactions",
                 "IsOpen", "Version", "RequestedByUserId", "RequestedAt")
                VALUES (900002, {closing.Id}, {scenario.Agent.Id}, 'Historical synthetic instruction', '[]',
                 true, false, 1, {scenario.Admin.Id}, {BankingScenario.Now});
                """);
            var customer = scenario.AddCustomerTransaction();
            var movement = new CustomerMovement
            {
                CustomerTransactionId = customer.Id, CashSessionId = scenario.Session.Id,
                CorrectionRequestId = 900002, Kind = CustomerMovementKind.Completed, Amount = 50_000m,
                CashEffect = 50_000m, BankEffect = -50_000m, CommandId = Guid.NewGuid(),
                RecordedByUserId = scenario.User.Id, OccurredAt = BankingScenario.Now,
                OccurrenceDate = scenario.Session.SessionDate, RecordedAt = BankingScenario.Now
            };
            legacy.CustomerMovements.Add(movement);
            await legacy.SaveChangesAsync();
            var originalSettlements = await db.SessionFloatSettlements.CountAsync();
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            var archived = await db.BankStatementImports.Include(i => i.EvidenceFile).SingleAsync();
            Assert.Equal(950_000m, archived.ClosingBalance);
            Assert.Equal(new byte[] { 1, 2, 3 }, archived.EvidenceFile!.Content);
            Assert.Equal("Historical synthetic instruction",
                (await db.CustomerMovements.Include(m => m.CorrectionRequest).SingleAsync()).CorrectionRequest!.Reason);
            Assert.Equal(CashCountStatus.Approved, (await db.CashCounts.FindAsync(closing.Id))!.Status);
            Assert.Equal(originalSettlements, await db.SessionFloatSettlements.CountAsync());
            Assert.Empty(await db.ClosingReconciliationRuns.ToListAsync());
            foreach (var sql in new[]
            {
                """UPDATE reconciliation_archive."BankStatementImports" SET "Version" = "Version" + 1""",
                """DELETE FROM reconciliation_archive."BankStatementImports" WHERE "Id" = 900001""",
                """INSERT INTO reconciliation_archive."BankStatementImports" SELECT * FROM reconciliation_archive."BankStatementImports" WHERE "Id" = 900001""",
                """TRUNCATE reconciliation_archive."BankStatementImports" CASCADE"""
            })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            }
            await Assert.ThrowsAsync<NotSupportedException>(() => db.GetService<IMigrator>().MigrateAsync(migrations[cutover - 1]));
            Assert.Equal(1, await db.BankStatementImports.CountAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task DerivedBalanceMigration_PreservesExistingStatementEvidenceAndProtectsNewProvenance()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString);
        var databaseName = "agenti_derived_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        connectionString.Database = databaseName;
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString.ConnectionString).Options;
            await using var db = new ApplicationDbContext(options);
            var migrations = db.Database.GetMigrations().ToList();
            var current = migrations.FindIndex(m => m.EndsWith("_AllowDerivedStatementBalances", StringComparison.Ordinal));
            await db.GetService<IMigrator>().MigrateAsync(migrations[current - 1]);
            await using var previous = new BeforeDerivedBalancesContext(options);
            using var scenario = new BankingScenario(previous);
            scenario.AddApprovedOpening();
            var closing = scenario.AddSubmittedClosing();
            var csv = BankingScenario.StatementCsv(scenario.Session.SessionDate, 1_000_000m);
            var evidence = System.Text.Encoding.UTF8.GetBytes(csv);
            var statement = new DailyBankStatement
            {
                BankAccountId = scenario.Account.Id, StatementDate = scenario.Session.SessionDate, Revision = 1,
                Currency = "UGX", AccountIdentifier = scenario.Account.AccountIdentifier, FileName = "synthetic-v1.csv",
                Content = evidence, ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(evidence)),
                SchemaHash = new string('a', 64), SchemaVersion = 1,
                OpeningBalance = 1_000_000m, ClosingBalance = 1_000_000m, TotalDebits = 1m, TotalCredits = 1m,
                UploadedByUserId = scenario.Admin.Id, UploadedAt = BankingScenario.Now,
                Rows =
                [
                    new() { Sequence = 1, TransactionDate = scenario.Session.SessionDate, Description = "Synthetic debit", Debit = 1m, RunningBalance = 999_999m },
                    new() { Sequence = 2, TransactionDate = scenario.Session.SessionDate, Description = "Synthetic credit", Credit = 1m, RunningBalance = 1_000_000m }
                ]
            };
            previous.DailyBankStatements.Add(statement);
            await previous.SaveChangesAsync();

            await db.Database.MigrateAsync();

            var migrated = await db.DailyBankStatements.AsNoTracking().Include(s => s.Rows).SingleAsync();
            Assert.Equal(StatementBalanceSource.BankReported, migrated.BalanceSource);
            Assert.Null(migrated.OpeningCountId);
            Assert.Equal(evidence, migrated.Content);
            Assert.Equal(statement.ContentHash, migrated.ContentHash);
            Assert.Equal(statement.SchemaHash, migrated.SchemaHash);
            Assert.Equal(1, migrated.SchemaVersion);
            Assert.Equal(2, migrated.Rows.Count);
            var account = await db.BankAccounts.SingleAsync();
            Assert.False(account.AllowMissingRunningBalances);
            account.AllowMissingRunningBalances = true;
            await db.SaveChangesAsync();
            var clock = new BusinessClock(new BankingTestClock { Now = BankingScenario.Now.AddDays(1) });
            var uploads = new DailyBankStatementService(db, new BankingAccessService(db, clock), clock,
                new UniversalBankCsvStatementProfile(), Options.Create(new BankEvidenceOptions()));
            using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                BankingScenario.StatementCsv(scenario.Session.SessionDate, 0m, includeBalances: false)));
            var derived = await uploads.UploadAsync(scenario.Admin.Id,
                new DailyStatementUpload(account.Id, scenario.Session.SessionDate, true, statement.Id,
                    "New complete synthetic export without balances."), "synthetic-v2.csv", content);
            Assert.Equal(StatementBalanceSource.ApprovedOpening, derived.BalanceSource);
            Assert.NotNull(derived.OpeningCountId);
            var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
                db.GetService<IMigrator>().MigrateAsync(migrations[current - 1]));
            Assert.Contains("waiver audit history", error.Message);
            Assert.Equal(2, await db.DailyBankStatements.CountAsync());
            Assert.Equal(CashCountStatus.PendingApproval, (await db.CashCounts.FindAsync(closing.Id))!.Status);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    internal static CustomerTransactionForm Capture(BankingScenario scenario) => new()
    {
        CommandId = Guid.NewGuid(), CashSessionId = scenario.Session.Id, BankAccountId = scenario.Account.Id,
        CashWalletId = scenario.CashWallet.Id, Kind = CustomerTransactionKind.Deposit,
        Outcome = CustomerCaptureOutcome.Completed, Amount = 50_000m, CustomerIdentifier = "0012345",
        BankReference = "SYNTHETIC-DEPOSIT", OccurredAt = BankingScenario.Now
    };
}
