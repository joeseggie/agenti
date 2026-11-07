using System.Text;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class ReconciliationApprovalTests
{
    [Fact]
    public async Task DailyControl_MatchesUniqueDateOnlyAmountAndDoesNotCertifySessionCutoff()
    {
        using var fixture = new BankingTestContext();
        var (customer, reconciliation, time, _) = Services(fixture);
        SessionBalanceTests.AddOpening(fixture);
        await customer.CaptureAsync(fixture.User.Id, Deposit(fixture));
        FreezeClosing(fixture, 150_000m, 950_000m);
        time.Now = BankingTestContext.Now.AddDays(1);
        var dateOnlyRow = new ParsedStatementEntry(
            "synthetic line", "synthetic bank fields", null, null, null, "synthetic bank posting",
            "UGX", -50_000m, 950_000m, BankingTestContext.Now, null, null, false);
        await ImportAsync(fixture, time, [dateOnlyRow], 950_000m);

        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);
        review = await reconciliation.AutoMatchAsync(fixture.Admin.Id, review.Id, review.Version);

        Assert.True(review.DailyControls.Single().Status == DailyBankControlStatus.Reconciled,
            $"status={review.DailyControls.Single().Status}, opening={review.DailyControls.Single().OpeningBalance}, " +
            $"closing={review.DailyControls.Single().ClosingBalance}, " +
            $"statement={review.DailyControls.Single().StatementMovements}, register={review.DailyControls.Single().RegisterMovements}, " +
            $"difference={review.DailyControls.Single().Difference}, unaccounted={review.DailyControls.Single().UnaccountedBankEntries}");
        Assert.Equal(BankReconciliationStatus.Reconciled, review.DailyControlStatus);
        Assert.Single(review.Matches);
        Assert.False(review.Sessions.Single().CanApprove);
        Assert.Contains(review.Sessions.Single().BlockingReasons,
            reason => reason.Contains("cutoff", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SupervisorFlag_RequiresAdministratorResponseAndPreservesBothDecisions()
    {
        using var fixture = new BankingTestContext();
        var (_, reconciliation, _, _) = Services(fixture);
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);
        var administrator = await fixture.Db.Users.SingleAsync(u => u.Id == fixture.Admin.Id);
        administrator.Role = UserRole.Supervisor;
        await fixture.Db.SaveChangesAsync();

        review = await reconciliation.FlagAsync(fixture.Admin.Id, review.Id, review.Version,
            "The transaction-date control needs a second-person review.");

        Assert.True(review.Flags.Single().IsOpen);
        Assert.Equal(BankReconciliationStatus.NeedsReview, review.DailyControlStatus);
        administrator.Role = UserRole.Admin;
        await fixture.Db.SaveChangesAsync();
        review = await reconciliation.RespondToFlagAsync(fixture.Admin.Id, review.Id,
            new BankFlagResponseForm(review.Version, review.Flags.Single().Id,
                "The statement coverage gap is documented and accepted for follow-up.", Void: false));

        Assert.False(review.Flags.Single().IsOpen);
        Assert.Equal(fixture.Admin.Id, review.Flags.Single().ResponseByUserId);
        Assert.Equal("The statement coverage gap is documented and accepted for follow-up.", review.Flags.Single().Response);
    }

    [Fact]
    public async Task UnknownClassification_KeepsDailyControlOpen()
    {
        using var fixture = new BankingTestContext();
        var (_, reconciliation, time, _) = Services(fixture);
        time.Now = BankingTestContext.Now.AddDays(1);
        await ImportAsync(fixture, time, [Row("UNKNOWN-BANK-ROW", -10_000m, 990_000m, 0)], 990_000m);
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);
        var bank = review.BankEntries.Single();

        review = await reconciliation.ClassifyAsync(fixture.Admin.Id, review.Id, new BankClassificationForm
        {
            ExpectedReviewVersion = review.Version,
            BankTransactionId = bank.Id,
            Classification = BankTransactionClassification.Unknown,
            EvidenceFileId = bank.EvidenceFileId,
            Reason = "The bank-only movement has not been identified."
        });

        Assert.Equal(DailyBankControlStatus.NeedsReview, review.DailyControls.Single().Status);
        Assert.Equal(1, review.DailyControls.Single().UnaccountedBankEntries);
    }

    [Fact]
    public async Task Approval_UsesMatchedEvidenceWithoutChangingCurrentBalances()
    {
        using var fixture = new BankingTestContext();
        var (customer, reconciliation, time, _) = Services(fixture);
        SessionBalanceTests.AddOpening(fixture);
        await customer.CaptureAsync(fixture.User.Id, Deposit(fixture));
        var closingId = FreezeClosing(fixture, 150_000m, 950_000m);
        time.Now = BankingTestContext.Now.AddDays(1);
        await ImportAsync(fixture, time, [Row("RECEIPT-50", -50_000m, 950_000m, 0)], 950_000m);
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);
        Assert.False(review.Sessions.Single().CanApprove);
        review = await reconciliation.AutoMatchAsync(fixture.Admin.Id, review.Id, review.Version);
        Assert.True(review.Sessions.Single().CanApprove, string.Join(" ", review.Sessions.Single().BlockingReasons));
        var basis = review.Sessions.Single().BasisHash;
        var version = review.Version;
        var command = Guid.NewGuid();
        (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance = 333_000m;
        (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance = 777_000m;
        (await fixture.Db.Vaults.SingleAsync()).CurrentBalance = 123m;
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Users.Where(u => u.Id == fixture.User.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Admin));
        var selfCertification = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            reconciliation.ApproveClosingAsync(fixture.User.Id, review.Id, fixture.Session.Id, Guid.NewGuid(), version, basis));
        Assert.Contains("your own closing", selfCertification.Message);
        Assert.Equal(CashCountStatus.PendingApproval, (await fixture.Db.CashCounts.AsNoTracking()
            .SingleAsync(c => c.Id == closingId)).Status);
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Users.Where(u => u.Id == fixture.User.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Agent));

        var approved = await reconciliation.ApproveClosingAsync(fixture.Admin.Id, review.Id, fixture.Session.Id, command, version, basis);
        await reconciliation.ApproveClosingAsync(fixture.Admin.Id, review.Id, fixture.Session.Id, command, version, basis);

        Assert.Equal(CashCountStatus.Approved, approved.Sessions.Single().ApprovalStatus);
        Assert.Equal(CashCountStatus.Approved, (await fixture.Db.CashCounts.FindAsync(closingId))!.Status);
        Assert.Equal(333_000m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
        Assert.Equal(777_000m, (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance);
        Assert.Equal(123m, (await fixture.Db.Vaults.SingleAsync()).CurrentBalance);
        Assert.Equal(1, await fixture.Db.BankReconciliationResults.CountAsync());
    }

    [Fact]
    public async Task EqualBalances_DoNotHideMissingOffsettingCustomerTransactions()
    {
        using var fixture = new BankingTestContext();
        var (_, reconciliation, time, _) = Services(fixture);
        SessionBalanceTests.AddOpening(fixture);
        FreezeClosing(fixture, 100_000m, 1_000_000m);
        time.Now = BankingTestContext.Now.AddDays(1);
        await ImportAsync(fixture, time,
            [Row("MISSING-DEPOSIT", -50_000m, 950_000m, 0), Row("MISSING-WITHDRAWAL", 50_000m, 1_000_000m, 1)],
            1_000_000m);
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);
        review = await reconciliation.AutoMatchAsync(fixture.Admin.Id, review.Id, review.Version);

        Assert.False(review.Sessions.Single().CanApprove);
        Assert.Contains(review.Sessions.Single().BlockingReasons, r => r.Contains("no matching register"));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => reconciliation.ApproveClosingAsync(fixture.Admin.Id,
            review.Id, fixture.Session.Id, Guid.NewGuid(), review.Version, review.Sessions.Single().BasisHash));
    }

    [Fact]
    public async Task VerifiedBankFailure_CanRemainFlaggedAfterApproval()
    {
        using var fixture = new BankingTestContext();
        var (customer, reconciliation, time, evidence) = Services(fixture);
        SessionBalanceTests.AddOpening(fixture);
        var form = Deposit(fixture);
        form.Outcome = CustomerCaptureOutcome.Failed;
        form.Notes = "The bank debited the float but did not credit the customer.";
        var entry = await customer.CaptureAsync(fixture.User.Id, form);
        FreezeClosing(fixture, 150_000m, 950_000m);
        time.Now = BankingTestContext.Now.AddDays(1);
        await ImportAsync(fixture, time, [Row("RECEIPT-50", -50_000m, 950_000m, 0)], 950_000m);
        var proof = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "bank-explanation.pdf", false, new MemoryStream(Encoding.UTF8.GetBytes("%PDF-synthetic bank explanation")));
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);
        review = await reconciliation.AutoMatchAsync(fixture.Admin.Id, review.Id, review.Version);
        Assert.False(review.Sessions.Single().CanApprove);
        review = await reconciliation.RecordExceptionAsync(fixture.Admin.Id, review.Id, new BankExceptionForm
        {
            ExpectedReviewVersion = review.Version, CashSessionId = fixture.Session.Id,
            Kind = BankExceptionKind.BankFailure, Amount = 50_000m,
            CustomerTransactionId = entry.Id, CustomerMovementId = entry.Movements.Single().Id,
            EvidenceFileId = proof.Id, BankExplanationVerified = true,
            Explanation = "Bank support verified the failure; the reversal remains outstanding."
        });

        Assert.True(review.Sessions.Single().CanApprove, string.Join(" ", review.Sessions.Single().BlockingReasons));
        var approved = await reconciliation.ApproveClosingAsync(fixture.Admin.Id, review.Id, fixture.Session.Id,
            Guid.NewGuid(), review.Version, review.Sessions.Single().BasisHash);

        Assert.Equal(BankReconciliationStatus.WithExceptions, approved.Sessions.Single().Status);
        Assert.False(approved.Exceptions.Single().IsResolved);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reconciliation.GetAsync(fixture.User.Id, review.Id));
    }

    private static (CustomerTransactionService Customer, BankReconciliationService Reconciliation,
        TestTimeProvider Time, BankEvidenceService Evidence) Services(BankingTestContext fixture)
    {
        var time = new TestTimeProvider { Now = BankingTestContext.Now.AddHours(1) };
        var clock = new BusinessClock(time);
        var access = new BankingAccessService(fixture.Db, clock);
        return (new CustomerTransactionService(fixture.Db, access, clock),
            new BankReconciliationService(fixture.Db, access, clock, new SessionBalanceService(fixture.Db, clock)), time,
            new BankEvidenceService(fixture.Db, access, clock, Options.Create(new BankEvidenceOptions())));
    }

    [Theory]
    [InlineData(true, 950000)]
    [InlineData(false, 1000000)]
    public async Task PostingDelay_ReconcilesOnlyItsExplicitObservedBalanceBasis(bool reflected, decimal reportedBank)
    {
        using var fixture = new BankingTestContext();
        var (customer, reconciliation, time, evidence) = Services(fixture);
        SessionBalanceTests.AddOpening(fixture);
        var entry = await customer.CaptureAsync(fixture.User.Id, Deposit(fixture));
        FreezeClosing(fixture, 150_000m, reportedBank);
        time.Now = BankingTestContext.Now.AddDays(1);
        await ImportAsync(fixture, time, [], 1_000_000m);
        var proof = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "delay.pdf", false,
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-synthetic posting-delay evidence")));
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);
        Assert.False(review.Sessions.Single().CanApprove);

        review = await reconciliation.RecordExceptionAsync(fixture.Admin.Id, review.Id, new BankExceptionForm
        {
            ExpectedReviewVersion = review.Version, CashSessionId = fixture.Session.Id,
            Kind = BankExceptionKind.PostingDelay, Amount = 50_000m,
            CustomerMovementId = entry.Movements.Single().Id, CustomerTransactionId = entry.Id,
            EvidenceFileId = proof.Id, BankExplanationVerified = true, ReflectedInClosingBalance = reflected,
            Explanation = "Bank support verified the pending ledger posting and the observed balance basis."
        });

        Assert.True(review.Sessions.Single().CanApprove, string.Join(" ", review.Sessions.Single().BlockingReasons));
        Assert.Equal(reflected ? -50_000m : 0m, review.Sessions.Single().ExplainedBankDifference);
        Assert.Equal(BankReconciliationStatus.WithExceptions, review.Sessions.Single().Status);
    }

    [Fact]
    public async Task NewCompleteSource_OmittingOffsettingRowsCannotFallBackToOldCoverage()
    {
        using var fixture = new BankingTestContext();
        var (_, reconciliation, time, _) = Services(fixture);
        SessionBalanceTests.AddOpening(fixture);
        FreezeClosing(fixture, 100_000m, 1_000_000m);
        time.Now = BankingTestContext.Now.AddDays(1);
        await ImportAsync(fixture, time,
            [Row("FIRST", -50_000m, 950_000m, 0), Row("SECOND", 50_000m, 1_000_000m, 1)], 1_000_000m);
        var contradictory = await ImportAsync(fixture, time, [], 1_000_000m);

        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);

        Assert.False(contradictory.CoverageVerified);
        Assert.Contains("omits", contradictory.ErrorMessage);
        Assert.Null(review.Sessions.Single().StatementClosingBalance);
        Assert.Contains(review.Sessions.Single().BlockingReasons, reason => reason.Contains("coverage"));
        Assert.Equal(2, await fixture.Db.BankTransactions.CountAsync());
    }

    [Fact]
    public async Task RevisedSource_InvalidatesPriorApprovalAndItsMatchesWithoutEditingCounts()
    {
        using var fixture = new BankingTestContext();
        var (customer, reconciliation, time, _) = Services(fixture);
        SessionBalanceTests.AddOpening(fixture);
        await customer.CaptureAsync(fixture.User.Id, Deposit(fixture));
        var closingId = FreezeClosing(fixture, 150_000m, 950_000m);
        time.Now = BankingTestContext.Now.AddDays(1);
        await ImportAsync(fixture, time, [Row("RECEIPT-50", -50_000m, 950_000m, 0)], 950_000m);
        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id, fixture.Session.SessionDate, fixture.Session.SessionDate);
        review = await reconciliation.AutoMatchAsync(fixture.Admin.Id, review.Id, review.Version);
        var oldBasis = review.Sessions.Single().BasisHash;
        var oldVersion = review.Version;
        await reconciliation.ApproveClosingAsync(fixture.Admin.Id, review.Id, fixture.Session.Id, Guid.NewGuid(), oldVersion, oldBasis);
        var approvedAt = (await fixture.Db.CashCounts.AsNoTracking().SingleAsync(c => c.Id == closingId)).ApprovedAt;

        await ImportAsync(fixture, time, [Row("RECEIPT-50", -60_000m, 940_000m, 0)], 940_000m, acceptRevisions: true);
        var changed = await reconciliation.GetAsync(fixture.Admin.Id, review.Id);

        Assert.Equal(BankReconciliationStatus.NeedsReview, changed.Status);
        Assert.False(changed.Sessions.Single().CanApprove);
        Assert.NotEqual(oldBasis, changed.Sessions.Single().BasisHash);
        Assert.Empty(changed.Matches);
        var closing = await fixture.Db.CashCounts.AsNoTracking().SingleAsync(c => c.Id == closingId);
        Assert.Equal(1_100_000m, closing.TotalAmount);
        Assert.Equal(approvedAt, closing.ApprovedAt);
        Assert.Equal(1, await fixture.Db.BankReconciliationResults.CountAsync());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => reconciliation.ApproveClosingAsync(fixture.Admin.Id,
            review.Id, fixture.Session.Id, Guid.NewGuid(), oldVersion, oldBasis));
    }

    [Fact]
    public async Task CommittedOriginal_RemainsReadableWithoutItsParserInstalled()
    {
        using var fixture = new BankingTestContext();
        var (_, _, time, _) = Services(fixture);
        time.Now = BankingTestContext.Now.AddDays(1);
        var committed = await ImportAsync(fixture, time, [Row("RECEIPT-50", -50_000m, 950_000m, 0)], 950_000m);
        var clock = new BusinessClock(time);
        var imports = new BankStatementService(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            new StatementProfileCatalog([]), Options.Create(new BankStatementOptions()), NullLogger<BankStatementService>.Instance);

        var preview = await imports.GetPreviewAsync(fixture.Admin.Id, committed.Id);

        Assert.Equal(StatementImportStatus.Committed, preview.Import.Status);
        Assert.Equal("RECEIPT-50", preview.Rows.Single().TransactionNumber);
        Assert.False(preview.Rows.Single().RequiresIdentityDecision);
    }

    [Fact]
    public async Task AdjacentStatementHeaders_CannotHideAnUnexplainedOvernightBalanceJump()
    {
        using var fixture = new BankingTestContext();
        var (_, reconciliation, time, _) = Services(fixture);
        fixture.AddApprovedOpening(bankAmount: 1_050_000m);
        FreezeClosing(fixture, 100_000m, 1_050_000m);
        var previousSession = new CashSession
        {
            BranchId = fixture.Account.BranchId, SessionDate = fixture.Session.SessionDate.AddDays(-1),
            Status = CashSessionStatus.Closed, WorkflowVersion = SessionWorkflowVersion.BankReconciliation,
            OpenedAt = BankingTestContext.Now.AddDays(-1).AddHours(-1), OperationalClosedAt = BankingTestContext.Now.AddDays(-1)
        };
        fixture.Db.CashSessions.Add(previousSession);
        await fixture.Db.SaveChangesAsync();
        var previousCount = new CashCount
        {
            CashSessionId = previousSession.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            CountDate = previousSession.SessionDate, Status = CashCountStatus.PendingApproval,
            TotalAmount = 1_100_000m, CreatedAt = BankingTestContext.Now.AddDays(-1)
        };
        fixture.Db.CashCounts.Add(previousCount);
        await fixture.Db.SaveChangesAsync();
        var openingSnapshot = await fixture.Db.CashCountSnapshots.SingleAsync(s => s.CashCount!.IsOpening);
        fixture.Db.OpeningBankVariances.Add(new OpeningBankVariance
        {
            BankAccountId = fixture.Account.Id, OpeningSnapshotId = openingSnapshot.Id, PreviousClosingCountId = previousCount.Id,
            PreviousBankBalance = 1_000_000m, ObservedBankBalance = 1_050_000m,
            PreviousObservedAt = BankingTestContext.Now.AddDays(-1), ObservedAt = fixture.Session.OpenedAt,
            RecordedAt = fixture.Session.OpenedAt
        });
        ZeroSource(previousSession.SessionDate, 1_000_000m);
        ZeroSource(fixture.Session.SessionDate, 1_050_000m);
        await fixture.Db.SaveChangesAsync();
        time.Now = BankingTestContext.Now.AddDays(1);

        var review = await reconciliation.CreateAsync(fixture.Admin.Id, fixture.Account.Id,
            fixture.Session.SessionDate, fixture.Session.SessionDate);

        Assert.False(review.Sessions.Single().CanApprove);
        Assert.Contains(review.Sessions.Single().BlockingReasons, reason => reason.Contains("previous closing"));
        Assert.All(review.Sessions.Single().Wallets, wallet => Assert.Equal(0m, wallet.Variance));

        void ZeroSource(DateOnly date, decimal balance)
        {
            var source = fixture.AddImport();
            source.CoverageFrom = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime();
            source.CoverageTo = source.CoverageFrom.Value.AddDays(1);
            source.CoverageFromDate = source.CoverageToDate = date;
            source.OpeningBalance = source.ClosingBalance = balance;
            source.CoverageVerified = source.CoverageDeclaredComplete = true;
            source.ReportedAccountIdentifier = fixture.Account.AccountIdentifier;
            source.Currency = "UGX";
            source.BalanceBasis = BankBalanceBasis.Ledger;
        }
    }

    private static CustomerTransactionForm Deposit(BankingTestContext fixture) => new()
    {
        CommandId = Guid.NewGuid(), CashSessionId = fixture.Session.Id, BankAccountId = fixture.Account.Id,
        CashWalletId = fixture.CashWallet.Id, Kind = CustomerTransactionKind.Deposit,
        Outcome = CustomerCaptureOutcome.Completed, Amount = 50_000m, CustomerIdentifier = "SYNTHETIC-CUSTOMER",
        BankReference = "RECEIPT-50", OccurredAt = BankingTestContext.Now
    };

    private static long FreezeClosing(BankingTestContext fixture, decimal cash, decimal bank)
    {
        var closing = new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            CountDate = fixture.Session.SessionDate, Status = CashCountStatus.PendingApproval,
            TotalAmount = cash + bank, CreatedAt = BankingTestContext.Now.AddMinutes(30)
        };
        fixture.Db.CashCounts.Add(closing);
        fixture.Db.SaveChanges();
        var snapshot = new CashCountSnapshot
        {
            CashCountId = closing.Id, RegisterVersion = fixture.Session.RegisterVersion,
            TotalAmount = closing.TotalAmount, CapturedAt = BankingTestContext.Now.AddMinutes(30),
            CapturedByUserId = fixture.User.Id,
            WalletsJson = CountSnapshotCodec.Serialize(
            [
                new(fixture.CashWallet.Id, "cash", WalletTypeEnum.Cash, "UGX", cash, BankingTestContext.Now.AddMinutes(30), null),
                new(fixture.BankWallet.Id, "bank", WalletTypeEnum.Bank, "UGX", bank, BankingTestContext.Now.AddMinutes(30), fixture.Account.Id)
            ])
        };
        fixture.Db.CashCountSnapshots.Add(snapshot);
        fixture.Db.SaveChanges();
        fixture.Db.SessionFloatSettlements.Add(new SessionFloatSettlement
        {
            CashCountId = closing.Id, SnapshotId = snapshot.Id, Stage = FloatSettlementStage.Closing,
            ReportedAmount = closing.TotalAmount, VaultAmount = 0, PostedAt = snapshot.CapturedAt,
            PostedByUserId = fixture.User.Id
        });
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Session.OperationalClosedAt = snapshot.CapturedAt;
        fixture.Db.SaveChanges();
        return closing.Id;
    }

    private static async Task<BankStatementImportDto> ImportAsync(
        BankingTestContext fixture, TestTimeProvider time, IReadOnlyList<ParsedStatementEntry> rows, decimal closing,
        bool acceptRevisions = false)
    {
        var profile = new TestProfile(rows, closing);
        var account = await fixture.Db.BankAccounts.FindAsync(fixture.Account.Id);
        account!.ImportProfileKey = profile.Key;
        account.ImportProfileVerified = true;
        account.BankIdentifier = profile.BankIdentifier;
        await fixture.Db.SaveChangesAsync();
        var clock = new BusinessClock(time);
        var access = new BankingAccessService(fixture.Db, clock);
        var evidence = new BankEvidenceService(fixture.Db, access, clock, Options.Create(new BankEvidenceOptions()));
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "source.csv", true,
            new MemoryStream(Encoding.UTF8.GetBytes($"synthetic reconciliation source {Guid.NewGuid()}")));
        var imports = new BankStatementService(fixture.Db, access, clock, new StatementProfileCatalog([profile]),
            Options.Create(new BankStatementOptions()), NullLogger<BankStatementService>.Instance);
        var import = await imports.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id);
        await imports.ProcessNextAsync();
        var preview = await imports.GetPreviewAsync(fixture.Admin.Id, import.Id);
        return await imports.CommitAsync(fixture.Admin.Id, import.Id, new CommitStatementForm
        {
            ExpectedVersion = preview.Import.Version,
            Resolutions = acceptRevisions ? preview.Rows.Where(r => r.RequiresIdentityDecision).Select(r =>
                new StatementRowResolution(r.Id, StatementRowResolutionKind.AcceptRevision, r.PossibleExistingTransactionIds.Single(),
                    "The bank supplied a corrected source for this occurrence.")).ToList() : []
        });
    }

    private static ParsedStatementEntry Row(string id, decimal amount, decimal balance, int minutes) => new(
        "synthetic line", "synthetic bank fields", id, id, "SYNTHETIC-CUSTOMER", "synthetic bank posting",
        "UGX", amount, balance, BankingTestContext.Now.AddMinutes(minutes), null, null, true);

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestProfile(IReadOnlyList<ParsedStatementEntry> rows, decimal closing) : IStatementImportProfile
    {
        public string Key => "synthetic-reconciliation-v1";
        public string BankIdentifier => "synthetic-bank";
        public string DisplayName => "Synthetic integration data";
        public int Version => 1;
        public bool IsVerified => true;
        public BankBalanceBasis BalanceBasis => BankBalanceBasis.Ledger;
        public IReadOnlyList<string> SupportedContentTypes => ["text/csv"];
        public Task<ParsedStatement> ParseAsync(Stream source, string contentType, StatementAccountContext account, CancellationToken cancellationToken) =>
            Task.FromResult(new ParsedStatement("SYNTHETIC-ACCOUNT", "UGX", BankBalanceBasis.Ledger,
                new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(3)),
                new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.FromHours(3)),
                1_000_000m, closing, true, rows));
        public string? GetReliableIdentity(ParsedStatementEntry entry) => entry.TransactionNumber;
    }
}
