using System.Text;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankStatementTests
{
    [Fact]
    public async Task MissingInstalledParser_KeepsEvidenceWithoutInventingAnImport()
    {
        using var fixture = new BankingTestContext();
        var clock = Clock();
        var access = new BankingAccessService(fixture.Db, clock);
        var catalog = new StatementProfileCatalog([]);
        var evidence = Evidence(fixture, clock);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "statement.csv", true, Content("unparsed source"));
        var service = Service(fixture, clock, catalog);

        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id);

        Assert.Equal(StatementImportStatus.Failed, import.Status);
        Assert.Contains("No installed statement parser", import.ErrorMessage);
        Assert.False(await service.ProcessNextAsync());
        Assert.Equal(0, await fixture.Db.BankTransactions.CountAsync());
        Assert.Equal("unparsed source", Encoding.UTF8.GetString((await evidence.DownloadAsync(fixture.Admin.Id, file.Id)).Content));
        var accounts = new BankAccountService(fixture.Db, access, clock, catalog);
        var account = (await accounts.GetAccountsAsync(fixture.Admin.Id)).Single();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            accounts.SetWorkflowAsync(fixture.Admin.Id, account.Id, true, account.Version));
    }

    [Fact]
    public async Task IdenticalAndOverlappingSources_ReuseCanonicalBankOccurrences()
    {
        using var fixture = new BankingTestContext();
        var firstRows = new[]
        {
            Row("A", -100m, 900m, 0), Row("B", 50m, 950m, 1)
        };
        var profile = new SyntheticProfile(Statement(firstRows, 950m));
        var (service, evidence) = Setup(fixture, profile);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "daily.csv", true, Content("first source"));
        var first = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id);
        Assert.True(await service.ProcessNextAsync());
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, first.Id);
        Assert.Empty(preview.Errors);
        var commit = new CommitStatementForm { ExpectedVersion = preview.Import.Version };
        var accepted = await service.CommitAsync(fixture.Admin.Id, first.Id, commit);
        var replay = await service.CommitAsync(fixture.Admin.Id, first.Id, commit);
        Assert.Equal(accepted.Id, replay.Id);
        Assert.Equal(first.Id, (await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id)).Id);

        profile.Statement = Statement([.. firstRows, Row("C", 25m, 975m, 2)], 975m);
        var otherFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "weekly.csv", true, Content("overlapping source"));
        var second = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, otherFile.Id);
        await service.ProcessNextAsync();
        var secondPreview = await service.GetPreviewAsync(fixture.Admin.Id, second.Id);
        await service.CommitAsync(fixture.Admin.Id, second.Id, new CommitStatementForm { ExpectedVersion = secondPreview.Import.Version });

        Assert.Equal(3, await fixture.Db.BankTransactions.CountAsync());
        Assert.Equal(5, await fixture.Db.BankStatementOccurrences.CountAsync());
    }

    [Fact]
    public async Task UniversalHeaderOnlyFile_CreatesDistinctPeriodsAndReprocessesAuditedBaselines()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Account.TimeZoneId = "Asia/Tokyo";
        await fixture.Db.SaveChangesAsync();
        var (service, evidence, clock) = UniversalSetup(fixture);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "empty.csv", true, Content("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n"));
        var firstRequest = new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
            123.45m, "Verified opening balance from bank advice.");

        var first = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id, firstRequest);
        Assert.Equal(first.Id, (await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id, firstRequest)).Id);
        Assert.True(await service.ProcessNextAsync());
        var firstPreview = await service.GetPreviewAsync(fixture.Admin.Id, first.Id);
        Assert.Empty(firstPreview.Errors);
        await service.CommitAsync(fixture.Admin.Id, first.Id, new CommitStatementForm { ExpectedVersion = firstPreview.Import.Version });

        var firstRevision = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id, firstRequest, reprocess: true);
        Assert.True(await service.ProcessNextAsync());
        var revisedFirstPreview = await service.GetPreviewAsync(fixture.Admin.Id, firstRevision.Id);
        Assert.Empty(revisedFirstPreview.Errors);
        await service.CommitAsync(fixture.Admin.Id, firstRevision.Id,
            new CommitStatementForm { ExpectedVersion = revisedFirstPreview.Import.Version });

        var secondRequest = new StatementImportRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), null);
        var second = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id, secondRequest);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(123.45m, second.OpeningBaseline);
        Assert.True(await service.ProcessNextAsync());
        var secondPreview = await service.GetPreviewAsync(fixture.Admin.Id, second.Id);
        Assert.Empty(secondPreview.Errors);
        await service.CommitAsync(fixture.Admin.Id, second.Id,
            new CommitStatementForm { ExpectedVersion = secondPreview.Import.Version });

        var secondRevision = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id, secondRequest, reprocess: true);
        Assert.True(await service.ProcessNextAsync());
        var revisedSecondPreview = await service.GetPreviewAsync(fixture.Admin.Id, secondRevision.Id);

        Assert.Empty(revisedSecondPreview.Errors);
        Assert.Equal(second.RequestedFromDate, revisedSecondPreview.Import.RequestedFromDate);
        Assert.Equal(second.RequestedToDate, revisedSecondPreview.Import.RequestedToDate);
        Assert.Equal(1, await fixture.Db.BankReconciliationDecisions.CountAsync(d =>
            d.Kind == ReconciliationDecisionKind.BaselineRecorded));
        Assert.Equal(2, await fixture.Db.BankStatementImports.CountAsync(i => i.BankAccountId == fixture.Account.Id &&
            i.RequestedFromDate == new DateOnly(2026, 11, 1)));
        Assert.Equal(2, await fixture.Db.BankStatementImports.CountAsync(i => i.BankAccountId == fixture.Account.Id &&
            i.RequestedFromDate == new DateOnly(2026, 11, 2)));
        Assert.Equal(clock.Today, new DateOnly(2026, 11, 3));
    }

    [Fact]
    public async Task UniversalStart_RejectsInvalidBaselinePrecisionAndIncompleteCurrentDay()
    {
        using var fixture = new BankingTestContext();
        var (service, evidence, clock) = UniversalSetup(fixture);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "empty.csv", true, Content("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n"));

        var invalidBaseline = await Assert.ThrowsAsync<BankingRuleException>(() => service.StartAsync(
            fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
                100.005m, "Verified opening balance from bank advice.")));
        var currentDay = await Assert.ThrowsAsync<BankingRuleException>(() => service.StartAsync(
            fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(clock.Today, clock.Today, null)));

        Assert.Contains("two decimal places", invalidBaseline.Message);
        Assert.Contains("current business day is not complete", currentDay.Message);
        Assert.Empty(await fixture.Db.BankStatementImports.ToListAsync());
        var oversizedBaseline = await Assert.ThrowsAsync<BankingRuleException>(() => service.StartAsync(
            fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
                10_000_000_000_000_000m, "Verified opening balance from bank advice.")));
        Assert.Contains("9,999,999,999,999,999.99", oversizedBaseline.Message);
        var negativeBaseline = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
                -123.45m, "Verified opening overdraft from bank advice."));
        Assert.Equal(-123.45m, negativeBaseline.OpeningBaseline);
    }

    [Fact]
    public async Task UniversalContinuation_UsesLatestCommittedRevisionAndIgnoresSupersededStagedImport()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.Status = CashSessionStatus.Closed;
        await fixture.Db.SaveChangesAsync();
        var (service, evidence, _) = UniversalSetup(fixture);
        var emptyCsv = "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n";
        var stagedFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "staged.csv", true, Content(emptyCsv));
        var staged = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, stagedFile.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
                100m, "Verified opening balance from bank advice."));
        Assert.True(await service.ProcessNextAsync());

        var staleFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "stale.csv", true, Content(emptyCsv.Replace("\n", "\r\n", StringComparison.Ordinal)));
        var stale = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, staleFile.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
                100m, "Verified opening balance from bank advice."));
        Assert.True(await service.ProcessNextAsync());
        var stagedPreview = await service.GetPreviewAsync(fixture.Admin.Id, staged.Id);
        await service.CommitAsync(fixture.Admin.Id, staged.Id,
            new CommitStatementForm { ExpectedVersion = stagedPreview.Import.Version });

        var correctionFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "corrected.csv", true, Content(emptyCsv + "2026-11-01,,REVISION-1,Deposit,,100.00,200.00\n"));
        var correction = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, correctionFile.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), null));
        Assert.True(await service.ProcessNextAsync());
        var correctionPreview = await service.GetPreviewAsync(fixture.Admin.Id, correction.Id);
        await service.CommitAsync(fixture.Admin.Id, correction.Id, new CommitStatementForm
        {
            ExpectedVersion = correctionPreview.Import.Version,
            Resolutions = [new(correctionPreview.Rows.Single().Id, StatementRowResolutionKind.NewOccurrence, null,
                "The corrected complete statement supplies this transaction date.")]
        });

        var next = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, stagedFile.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), null));
        Assert.Equal(200m, next.OpeningBaseline);
        Assert.True(await service.ProcessNextAsync());
        var nextPreview = await service.GetPreviewAsync(fixture.Admin.Id, next.Id);
        await service.CommitAsync(fixture.Admin.Id, next.Id,
            new CommitStatementForm { ExpectedVersion = nextPreview.Import.Version });

        Assert.Equal(StatementImportStatus.ReadyForReview,
            (await service.GetPreviewAsync(fixture.Admin.Id, stale.Id)).Import.Status);
    }

    [Fact]
    public async Task UniversalCommit_RevalidatesContinuityAgainstNewlyCommittedPredecessor()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.Status = CashSessionStatus.Closed;
        await fixture.Db.SaveChangesAsync();
        var (service, evidence, _) = UniversalSetup(fixture);
        var firstFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "first.csv", true,
            Content("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
                    "2026-11-01,,ONE,Deposit,,100.00,1100.00\n"));
        var secondFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "second.csv", true,
            Content("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
                    "2026-11-02,,TWO,Deposit,,1.00,1000.00\n"));
        var first = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, firstFile.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), null));
        var second = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, secondFile.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2),
                999m, "Test opening balance queued before its predecessor."));
        Assert.True(await service.ProcessNextAsync());
        Assert.True(await service.ProcessNextAsync());
        var firstPreview = await service.GetPreviewAsync(fixture.Admin.Id, first.Id);
        var secondPreview = await service.GetPreviewAsync(fixture.Admin.Id, second.Id);
        await service.CommitAsync(fixture.Admin.Id, first.Id,
            new CommitStatementForm { ExpectedVersion = firstPreview.Import.Version });

        var error = await Assert.ThrowsAsync<BankingRuleException>(() => service.CommitAsync(fixture.Admin.Id, second.Id,
            new CommitStatementForm { ExpectedVersion = secondPreview.Import.Version }));

        Assert.Contains("latest committed closing balance", error.Message);
    }

    [Fact]
    public async Task UniversalRows_KeepRequestedDatesAndTransactionDatesAcrossTimeZones()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Account.TimeZoneId = "Asia/Tokyo";
        await fixture.Db.SaveChangesAsync();
        var (service, evidence, _) = UniversalSetup(fixture);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "tokyo.csv", true,
            Content("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
                    "2026-11-01,,TOKYO-1,Deposit,,100.00,1100.00\n"));
        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), null));

        Assert.True(await service.ProcessNextAsync());
        var row = await fixture.Db.BankStatementRows.SingleAsync();
        var processed = await service.GetPreviewAsync(fixture.Admin.Id, import.Id);

        Assert.Equal(new DateOnly(2026, 11, 1), row.LedgerDate);
        Assert.Equal(new DateOnly(2026, 11, 1), processed.Import.RequestedFromDate);
        Assert.Equal(new DateOnly(2026, 11, 1), processed.Import.RequestedToDate);
        Assert.Equal(new DateOnly(2026, 11, 1), await fixture.Db.BankStatementImports
            .Where(i => i.Id == import.Id).Select(i => i.CoverageFromDate).SingleAsync());
        Assert.Equal(new DateOnly(2026, 11, 1), await fixture.Db.BankStatementImports
            .Where(i => i.Id == import.Id).Select(i => i.CoverageToDate).SingleAsync());
    }

    [Fact]
    public async Task UniversalCorrection_DerivesTransactionDateFromCorrectedTimeInAccountZone()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Account.TimeZoneId = "Asia/Tokyo";
        await fixture.Db.SaveChangesAsync();
        var (service, evidence, _) = UniversalSetup(fixture);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "tokyo.csv", true,
            Content("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
                    "2026-11-01,09:00:00,FIRST,Deposit,,100.00,1100.00\n" +
                    "2026-11-02,,SECOND,Withdrawal,50.00,,1050.00\n"));
        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null));
        Assert.True(await service.ProcessNextAsync());
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, import.Id);
        var corrected = await service.CorrectRowAsync(fixture.Admin.Id, import.Id, preview.Rows[0].Id,
            new CorrectStatementRowForm
            {
                ExpectedVersion = preview.Import.Version, TransactionNumber = preview.Rows[0].TransactionNumber,
                Reference = preview.Rows[0].Reference, CustomerIdentifier = preview.Rows[0].CustomerIdentifier,
                Narration = preview.Rows[0].Narration, Currency = preview.Rows[0].Currency,
                SignedAmount = preview.Rows[0].SignedAmount, RunningBalance = preview.Rows[0].RunningBalance,
                OccurredAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(9)),
                ValueDate = preview.Rows[0].ValueDate, HasPreciseTimestamp = true,
                Reason = "Corrected the transaction date from the bank source."
            });

        Assert.Empty(corrected.Errors);
        Assert.Equal(new DateOnly(2026, 11, 2), corrected.Rows[0].ValueDate);
        Assert.Equal(new DateOnly(2026, 11, 2),
            await fixture.Db.BankStatementRows.Where(r => r.Id == corrected.Rows[0].Id).Select(r => r.LedgerDate).SingleAsync());
    }

    [Fact]
    public async Task UniversalImport_UsesSharedOrderingRulesForMixedTimeRows()
    {
        using var fixture = new BankingTestContext();
        var (service, evidence, _) = UniversalSetup(fixture);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "mixed-time.csv", true,
            Content("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
                    "2026-11-01,09:15:00,REF-1,Deposit,,10.00,110.00\n" +
                    "2026-11-01,,REF-2,Fee,1.00,,109.00\n"));
        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), null));
        Assert.True(await service.ProcessNextAsync());
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, import.Id);

        Assert.Empty(preview.Errors);
        var committed = await service.CommitAsync(fixture.Admin.Id, import.Id,
            new CommitStatementForm { ExpectedVersion = preview.Import.Version });
        Assert.Equal(StatementImportStatus.Committed, committed.Status);
    }

    [Fact]
    public async Task UniversalRevision_CorrectsEarliestCoverageAndLeavesBrokenLaterChainUnresolved()
    {
        using var fixture = new BankingTestContext();
        fixture.Session.Status = CashSessionStatus.Closed;
        await fixture.Db.SaveChangesAsync();
        var (service, evidence, clock) = UniversalSetup(fixture);
        var emptyCsv = "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n";
        var evidenceFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "period.csv", true, Content(emptyCsv));
        var firstDate = new DateOnly(2026, 11, 1);
        var secondDate = new DateOnly(2026, 11, 2);
        var first = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, evidenceFile.Id,
            new StatementImportRequest(firstDate, firstDate, 123.45m, "Verified opening balance from bank advice."));
        Assert.True(await service.ProcessNextAsync());
        var firstPreview = await service.GetPreviewAsync(fixture.Admin.Id, first.Id);
        await service.CommitAsync(fixture.Admin.Id, first.Id, new CommitStatementForm { ExpectedVersion = firstPreview.Import.Version });
        var second = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, evidenceFile.Id,
            new StatementImportRequest(secondDate, secondDate, null));
        Assert.True(await service.ProcessNextAsync());
        var secondPreview = await service.GetPreviewAsync(fixture.Admin.Id, second.Id);
        await service.CommitAsync(fixture.Admin.Id, second.Id, new CommitStatementForm { ExpectedVersion = secondPreview.Import.Version });

        var correctedFile = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "corrected.csv", true, Content(emptyCsv +
                "2026-11-01,,REVISION-1,Deposit,,100.00,223.45\n"));
        var correction = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, correctedFile.Id,
            new StatementImportRequest(firstDate, firstDate, null));
        Assert.True(await service.ProcessNextAsync());
        var correctionPreview = await service.GetPreviewAsync(fixture.Admin.Id, correction.Id);
        var decision = correctionPreview.Rows.Single();
        await service.CommitAsync(fixture.Admin.Id, correction.Id, new CommitStatementForm
        {
            ExpectedVersion = correctionPreview.Import.Version,
            Resolutions = [new(decision.Id, StatementRowResolutionKind.NewOccurrence, null,
                "The corrected complete statement supplies this previously empty transaction date.")]
        });

        var reconciliation = new BankReconciliationService(fixture.Db,
            new BankingAccessService(fixture.Db, clock), clock, new SessionBalanceService(fixture.Db, clock));
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id, secondDate, secondDate);

        Assert.Equal(DailyBankControlStatus.NeedsReview, review.DailyControls.Single().Status);
        Assert.Null(review.DailyControls.Single().OpeningBalance);
        Assert.Null(review.DailyControls.Single().ClosingBalance);
    }

    [Fact]
    public async Task UniversalProcessing_BoundsPersistedAggregatedRowErrors()
    {
        using var fixture = new BankingTestContext();
        var (service, evidence, _) = UniversalSetup(fixture);
        var csv = "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            string.Concat(Enumerable.Repeat("not-date,,REF,Fee,1.00,,bad\n", 200));
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "invalid.csv", true, Content(csv));
        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id,
            new StatementImportRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), null));

        Assert.True(await service.ProcessNextAsync());
        var failed = await service.GetPreviewAsync(fixture.Admin.Id, import.Id);

        Assert.Equal(StatementImportStatus.Failed, failed.Import.Status);
        Assert.NotNull(failed.Import.ErrorMessage);
        Assert.True(failed.Import.ErrorMessage.Length <= 2000);
    }

    [Fact]
    public async Task EnablingUniversalWorkflow_PersistsVerificationAndAcceptsConfirmedAvailableBasis()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.BalanceBasis = BankBalanceBasis.Available;
        fixture.Account.ImportProfileKey = null;
        fixture.Account.ImportProfileVerified = false;
        await fixture.Db.SaveChangesAsync();
        var (service, _, clock) = UniversalSetup(fixture);
        var access = new BankingAccessService(fixture.Db, clock);
        var accounts = new BankAccountService(fixture.Db, access,
            new BusinessClock(new FixedTimeProvider()), new StatementProfileCatalog([new UniversalBankCsvStatementProfile()]));
        var configured = await accounts.ConfigureAsync(fixture.Admin.Id, fixture.Account.Id, new BankAccountForm
        {
            WalletId = fixture.BankWallet.Id, BankName = fixture.Account.BankName,
            AccountIdentifier = fixture.Account.AccountIdentifier, TimeZoneId = fixture.Account.TimeZoneId,
            BalanceBasis = BankBalanceBasis.Available
        }, fixture.Account.Version);

        var enabled = await accounts.SetWorkflowAsync(fixture.Admin.Id, fixture.Account.Id, true, configured.Version);

        Assert.True(enabled.WorkflowEnabled);
        Assert.Equal(BankBalanceBasis.Available, enabled.BalanceBasis);
        Assert.Equal(UniversalBankCsvStatementProfile.ProfileKey, fixture.Account.ImportProfileKey);
        Assert.True(fixture.Account.ImportProfileVerified);
    }

    [Fact]
    public async Task WeakIdentity_PreservesRepeatedRowsAndRequiresOverlapDecisions()
    {
        using var fixture = new BankingTestContext();
        var profile = new SyntheticProfile(Statement(
            [Row(null, -100m, 900m, 0), Row(null, 100m, 1000m, 0), Row(null, -100m, 900m, 0)], 900m),
            reliableIdentity: false);
        var (service, evidence) = Setup(fixture, profile);
        var first = await StartProcessedAsync(fixture, service, evidence, "weak-first.csv", "first");
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, first);
        await service.CommitAsync(fixture.Admin.Id, first, new CommitStatementForm { ExpectedVersion = preview.Import.Version });
        var canonical = await fixture.Db.BankTransactions.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(3, canonical.Count);
        var second = await StartProcessedAsync(fixture, service, evidence, "weak-second.csv", "second");
        var overlap = await service.GetPreviewAsync(fixture.Admin.Id, second);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CommitAsync(fixture.Admin.Id, second,
            new CommitStatementForm { ExpectedVersion = overlap.Import.Version }));

        await service.CommitAsync(fixture.Admin.Id, second, new CommitStatementForm
        {
            ExpectedVersion = overlap.Import.Version,
            Resolutions = overlap.Rows.Select((row, index) => new StatementRowResolution(
                row.Id, StatementRowResolutionKind.LinkExisting, canonical[index].Id,
                "Verified the same occurrence and bank statement order.")).ToList()
        });
        Assert.Equal(3, await fixture.Db.BankTransactions.CountAsync());
        Assert.Equal(6, await fixture.Db.BankStatementOccurrences.CountAsync());
    }

    [Fact]
    public async Task CorrectedWeakIdentity_DifferentDecimalScalesRetainOverlapCandidate()
    {
        using var fixture = new BankingTestContext();
        var profile = new SyntheticProfile(Statement([Row(null, -100.00m, 800.00m, 0)], 900m),
            reliableIdentity: false);
        var (service, evidence) = Setup(fixture, profile);
        var first = await StartProcessedAsync(fixture, service, evidence, "correction.csv", "original");
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, first);
        var row = preview.Rows.Single();
        var corrected = await service.CorrectRowAsync(fixture.Admin.Id, first, row.Id, new CorrectStatementRowForm
        {
            ExpectedVersion = preview.Import.Version, Narration = row.Narration,
            CustomerIdentifier = row.CustomerIdentifier, Currency = row.Currency,
            SignedAmount = -100m, RunningBalance = 900m, OccurredAt = row.OccurredAt,
            HasPreciseTimestamp = true, Reason = "Corrected against the retained statement."
        });
        await service.CommitAsync(fixture.Admin.Id, first,
            new CommitStatementForm { ExpectedVersion = corrected.Import.Version });
        var existingId = (await fixture.Db.BankTransactions.SingleAsync()).Id;

        profile.Statement = Statement([Row(null, -100.00m, 900.00m, 0)], 900.00m);
        var second = await StartProcessedAsync(fixture, service, evidence, "overlap.csv", "overlap");
        var overlap = await service.GetPreviewAsync(fixture.Admin.Id, second);
        var overlappingRow = overlap.Rows.Single();

        Assert.Equal([existingId], overlappingRow.PossibleExistingTransactionIds);
        Assert.True(overlappingRow.RequiresIdentityDecision);
        await service.CommitAsync(fixture.Admin.Id, second, new CommitStatementForm
        {
            ExpectedVersion = overlap.Import.Version,
            Resolutions = [new(overlappingRow.Id, StatementRowResolutionKind.LinkExisting, existingId,
                "Verified the same financial occurrence.")]
        });
        Assert.Equal(1, await fixture.Db.BankTransactions.CountAsync());
        Assert.Equal(2, await fixture.Db.BankStatementOccurrences.CountAsync());
    }

    [Fact]
    public async Task ExtractionCorrection_RetainsOriginalAndCannotCommitBadArithmetic()
    {
        using var fixture = new BankingTestContext();
        var profile = new SyntheticProfile(Statement([Row("A", -100m, 800m, 0)], 900m));
        var (service, evidence) = Setup(fixture, profile);
        var importId = await StartProcessedAsync(fixture, service, evidence, "correction.csv", "original source");
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, importId);
        Assert.NotEmpty(preview.Errors);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CommitAsync(fixture.Admin.Id, importId,
            new CommitStatementForm { ExpectedVersion = preview.Import.Version }));
        var row = preview.Rows.Single();

        var corrected = await service.CorrectRowAsync(fixture.Admin.Id, importId, row.Id, new CorrectStatementRowForm
        {
            ExpectedVersion = preview.Import.Version, TransactionNumber = row.TransactionNumber,
            Reference = row.Reference, CustomerIdentifier = row.CustomerIdentifier, Narration = row.Narration,
            Currency = row.Currency, SignedAmount = row.SignedAmount, RunningBalance = 900m,
            OccurredAt = row.OccurredAt, HasPreciseTimestamp = true,
            Reason = "Corrected the extraction against the retained original statement."
        });

        Assert.Empty(corrected.Errors);
        Assert.Equal(2, await fixture.Db.BankStatementRows.CountAsync());
        Assert.Equal(800m, (await fixture.Db.BankStatementRows.FindAsync(row.Id))!.RunningBalance);
        await service.CommitAsync(fixture.Admin.Id, importId,
            new CommitStatementForm { ExpectedVersion = corrected.Import.Version });
        Assert.Equal(corrected.Rows.Single().Id, (await fixture.Db.BankTransactions.SingleAsync()).AcceptedRowId);
    }

    [Fact]
    public async Task CompleteZeroTransactionStatement_IsEvidenceNotAnEmptyFailure()
    {
        using var fixture = new BankingTestContext();
        var profile = new SyntheticProfile(Statement([], 1000m));
        var (service, evidence) = Setup(fixture, profile);
        var id = await StartProcessedAsync(fixture, service, evidence, "empty-day.csv", "complete empty day");
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, id);

        var result = await service.CommitAsync(fixture.Admin.Id, id,
            new CommitStatementForm { ExpectedVersion = preview.Import.Version });

        Assert.True(result.CoverageVerified);
        Assert.Equal(StatementImportStatus.Committed, result.Status);
        Assert.Empty(preview.Rows);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public async Task BackgroundProcessing_CanBeCommittedFromTheOriginalBlazorContext()
    {
        using var fixture = new BankingTestContext();
        var profile = new SyntheticProfile(Statement([Row("A", -100m, 900m, 0)], 900m));
        var (service, evidence) = Setup(fixture, profile);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "background.csv", true, Content("synthetic background processing"));
        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id);
        await using (var backgroundDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(fixture.Db.Database.GetDbConnection()).Options))
        {
            var clock = Clock();
            var worker = new BankStatementService(backgroundDb, new BankingAccessService(backgroundDb, clock), clock,
                new StatementProfileCatalog([profile]), Options.Create(new BankStatementOptions()), NullLogger<BankStatementService>.Instance);
            Assert.True(await worker.ProcessNextAsync());
        }
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, import.Id);
        Assert.Equal(StatementImportStatus.ReadyForReview, preview.Import.Status);

        var committed = await service.CommitAsync(fixture.Admin.Id, import.Id, new CommitStatementForm { ExpectedVersion = preview.Import.Version });

        Assert.Equal(StatementImportStatus.Committed, committed.Status);
        Assert.Equal(1, await fixture.Db.BankTransactions.CountAsync());
    }

    private static async Task<long> StartProcessedAsync(
        BankingTestContext fixture, IBankStatementService service, IBankEvidenceService evidence, string name, string content)
    {
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), name, true, Content(content));
        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id);
        Assert.True(await service.ProcessNextAsync());
        return import.Id;
    }

    private static (BankStatementService Service, BankEvidenceService Evidence) Setup(BankingTestContext fixture, SyntheticProfile profile)
    {
        fixture.Account.ImportProfileKey = profile.Key;
        fixture.Account.ImportProfileVerified = true;
        fixture.Account.BankIdentifier = profile.BankIdentifier;
        fixture.Db.SaveChanges();
        var clock = Clock();
        return (Service(fixture, clock, new StatementProfileCatalog([profile])), Evidence(fixture, clock));
    }

    private static BankEvidenceService Evidence(BankingTestContext fixture, IBusinessClock clock) => new(
        fixture.Db, new BankingAccessService(fixture.Db, clock), clock, Options.Create(new BankEvidenceOptions()));

    private static BankStatementService Service(BankingTestContext fixture, IBusinessClock clock, StatementProfileCatalog catalog) => new(
        fixture.Db, new BankingAccessService(fixture.Db, clock), clock, catalog,
        Options.Create(new BankStatementOptions()), NullLogger<BankStatementService>.Instance);

    private static (BankStatementService Service, BankEvidenceService Evidence, IBusinessClock Clock) UniversalSetup(
        BankingTestContext fixture)
    {
        var profile = new UniversalBankCsvStatementProfile();
        fixture.Account.ImportProfileKey = profile.Key;
        fixture.Account.ImportProfileVerified = false;
        fixture.Account.BankIdentifier = "TEST BANK";
        fixture.Db.SaveChanges();
        var clock = Clock();
        var access = new BankingAccessService(fixture.Db, clock);
        return (new BankStatementService(fixture.Db, access, clock, new StatementProfileCatalog([profile]),
                Options.Create(new BankStatementOptions()), NullLogger<BankStatementService>.Instance),
            new BankEvidenceService(fixture.Db, access, clock, Options.Create(new BankEvidenceOptions())), clock);
    }

    private static IBusinessClock Clock() => new BusinessClock(new FixedTimeProvider());
    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));
    private static ParsedStatement Statement(IReadOnlyList<ParsedStatementEntry> rows, decimal closing) => new(
        "SYNTHETIC-ACCOUNT", "UGX", BankBalanceBasis.Ledger,
        new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(3)),
        new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.FromHours(3)), 1000m, closing, true, rows);
    private static ParsedStatementEntry Row(string? number, decimal amount, decimal balance, int minutes) => new(
        "synthetic row", "retained original fields", number, number, "0000123", "synthetic transaction",
        "UGX", amount, balance, BankingTestContext.Now.AddMinutes(minutes), null, null, true);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => BankingTestContext.Now.AddDays(1);
    }

    private sealed class SyntheticProfile(ParsedStatement statement, bool reliableIdentity = true) : IStatementImportProfile
    {
        public ParsedStatement Statement { get; set; } = statement;
        public string Key => "synthetic-test-v1";
        public string BankIdentifier => "synthetic-test-bank";
        public string DisplayName => "Synthetic test profile; not a bank adapter";
        public int Version => 1;
        public bool IsVerified => true;
        public BankBalanceBasis BalanceBasis => BankBalanceBasis.Ledger;
        public IReadOnlyList<string> SupportedContentTypes => ["text/csv"];
        public Task<ParsedStatement> ParseAsync(Stream source, string contentType, StatementAccountContext account, CancellationToken cancellationToken) =>
            Task.FromResult(Statement);
        public string? GetReliableIdentity(ParsedStatementEntry entry) => reliableIdentity ? entry.TransactionNumber : null;
    }
}
