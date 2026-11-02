using System.Text;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankStatementTests
{
    [Fact]
    public async Task MissingVerifiedProfile_KeepsEvidenceWithoutInventingAnImport()
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
        Assert.Contains("verified profile", import.ErrorMessage);
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
