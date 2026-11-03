using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class CustomerTransactionTests
{
    [Fact]
    public async Task Capture_IsTallyOnlyAndReplaysAfterSessionLocks()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var service = CreateService(fixture);
        var form = CaptureForm(fixture);

        var captured = await service.CaptureAsync(fixture.User.Id, form);
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Session.OperationalClosedAt = BankingTestContext.Now.AddMinutes(10);
        await fixture.Db.SaveChangesAsync();
        var replay = await service.CaptureAsync(fixture.User.Id, form);

        Assert.Equal(captured.Id, replay.Id);
        Assert.Equal(1, await fixture.Db.CustomerTransactions.CountAsync());
        Assert.Equal(1, await fixture.Db.CustomerMovements.CountAsync());
        Assert.Equal(100_000m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
        Assert.Equal(1_000_000m, (await fixture.Db.Wallets.FindAsync(fixture.BankWallet.Id))!.Balance);
        form.CommandId = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CaptureAsync(fixture.User.Id, form));
    }

    [Fact]
    public async Task Capture_RejectsReusedIdentityForChangedDetails()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var service = CreateService(fixture);
        var form = CaptureForm(fixture);
        await service.CaptureAsync(fixture.User.Id, form);
        form.Amount += 1m;

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CaptureAsync(fixture.User.Id, form));
        Assert.Equal(1, await fixture.Db.CustomerMovements.CountAsync());
    }

    [Fact]
    public async Task Failure_ReversalAndRetryNeverReceiveCashTwice()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var service = CreateService(fixture);
        var form = CaptureForm(fixture);
        form.Outcome = CustomerCaptureOutcome.Failed;
        form.Notes = "The customer's account was not credited.";
        var captured = await service.CaptureAsync(fixture.User.Id, form);
        var evidenceId = AddEvidence(fixture);
        var reversal = FollowUp(fixture, captured.Movements.Single().Id, evidenceId, CustomerMovementKind.BankReversal);
        var reversed = await service.RecordFollowUpAsync(fixture.User.Id, captured.Id, reversal);
        var retry = FollowUp(fixture, reversed.Movements.Last().Id, evidenceId, CustomerMovementKind.Retry);
        retry.BankReference = "RETRY-REFERENCE";
        retry.OccurredAt = BankingTestContext.Now.AddMinutes(2);

        var completed = await service.RecordFollowUpAsync(fixture.User.Id, captured.Id, retry);

        Assert.Equal(50_000m, completed.Movements.Sum(m => m.CashEffect));
        Assert.Equal(-50_000m, completed.Movements.Sum(m => m.BankEffect));
        Assert.Equal(50_000m, completed.CustomerSettledAmount);
        Assert.Equal(0m, completed.CashHeld);
        Assert.Equal(3, completed.Movements.Count);
        Assert.Equal(100_000m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
    }

    [Fact]
    public async Task Refund_RequiresVerifiedBankReturnAndCannotBePaidTwice()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var service = CreateService(fixture);
        var form = CaptureForm(fixture);
        form.Outcome = CustomerCaptureOutcome.Failed;
        form.Notes = "Bank debit occurred without a customer credit.";
        var captured = await service.CaptureAsync(fixture.User.Id, form);
        var evidenceId = AddEvidence(fixture);
        var refund = FollowUp(fixture, captured.Movements.Single().Id, evidenceId, CustomerMovementKind.Refund);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.RecordFollowUpAsync(fixture.User.Id, captured.Id, refund));

        var reversal = FollowUp(fixture, captured.Movements.Single().Id, evidenceId, CustomerMovementKind.BankReversal);
        var reversed = await service.RecordFollowUpAsync(fixture.User.Id, captured.Id, reversal);
        refund.RelatedMovementId = reversed.Movements.Last().Id;
        refund.OccurredAt = BankingTestContext.Now.AddMinutes(2);
        var refunded = await service.RecordFollowUpAsync(fixture.User.Id, captured.Id, refund);
        var replay = await service.RecordFollowUpAsync(fixture.User.Id, captured.Id, refund);

        Assert.Equal(refunded.Movements.Count, replay.Movements.Count);
        Assert.Equal(50_000m, refunded.RefundedAmount);
        Assert.Equal(0m, refunded.CashHeld);
        Assert.Equal(0m, refunded.Movements.Sum(m => m.CashEffect));
        refund.CommandId = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.RecordFollowUpAsync(fixture.User.Id, captured.Id, refund));
    }

    [Fact]
    public async Task Correction_RequiresGrantAndPreservesOriginalMovement()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var service = CreateService(fixture);
        var captured = await service.CaptureAsync(fixture.User.Id, CaptureForm(fixture));
        var closing = new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            CountDate = fixture.Session.SessionDate, Status = CashCountStatus.PendingApproval,
            TotalAmount = 1_100_000m, CreatedAt = BankingTestContext.Now
        };
        fixture.Db.CashCounts.Add(closing);
        await fixture.Db.SaveChangesAsync();
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Session.OperationalClosedAt = BankingTestContext.Now.AddMinutes(10);
        var request = new CashCountCorrectionRequest
        {
            CashCountId = closing.Id, AgentId = fixture.Agent.Id, Reason = "Correct the receipt amount.",
            AllowedMovementIdsJson = $"[{captured.Movements.Single().Id}]",
            RequestedByUserId = fixture.Admin.Id, RequestedAt = BankingTestContext.Now
        };
        fixture.Db.CashCountCorrectionRequests.Add(request);
        await fixture.Db.SaveChangesAsync();
        var correction = new CustomerMovementCorrectionForm
        {
            CommandId = Guid.NewGuid(), CorrectionRequestId = request.Id,
            MovementId = captured.Movements.Single().Id, Amount = 40_000m,
            TransactionKind = CustomerTransactionKind.Deposit, Outcome = CustomerCaptureOutcome.Completed,
            CustomerIdentifier = "SYNTHETIC-CUSTOMER", BankReference = "RECEIPT-REFERENCE",
            OccurredAt = BankingTestContext.Now, EvidenceFileId = AddEvidence(fixture),
            Reason = "The bank receipt confirms the corrected amount."
        };

        var corrected = await service.CorrectAsync(fixture.User.Id, correction);

        Assert.Equal(40_000m, corrected.Amount);
        Assert.Equal(2, corrected.Movements.Count);
        Assert.Equal(50_000m, corrected.Movements.Single(m => m.IsSuperseded).Amount);
        Assert.Equal(CashSessionStatus.Closed, (await fixture.Db.CashSessions.FindAsync(fixture.Session.Id))!.Status);
        Assert.Equal(1_100_000m, (await fixture.Db.CashCounts.FindAsync(closing.Id))!.TotalAmount);
        Assert.Equal(100_000m, (await fixture.Db.Wallets.FindAsync(fixture.CashWallet.Id))!.Balance);
    }

    private static CustomerTransactionService CreateService(BankingTestContext fixture, DateTimeOffset? now = null)
    {
        var clock = new BusinessClock(new FixedTimeProvider(now));
        return new CustomerTransactionService(fixture.Db, new BankingAccessService(fixture.Db, clock), clock);
    }

    [Fact]
    public async Task LaterBankReturnAndRefund_UseActualWindowsWithoutRewritingTheOldClosing()
    {
        using var fixture = new BankingTestContext();
        fixture.AddApprovedOpening();
        var capture = CaptureForm(fixture);
        capture.Outcome = CustomerCaptureOutcome.Failed;
        capture.Notes = "The customer's bank credit is still pending.";
        var entry = await CreateService(fixture).CaptureAsync(fixture.User.Id, capture);
        var oldClosing = Snapshot(fixture.Session, false, 150_000m, 950_000m, BankingTestContext.Now.AddMinutes(30));
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Session.OperationalClosedAt = oldClosing.CapturedAt;
        var laterSession = new CashSession
        {
            BranchId = fixture.Account.BranchId, SessionDate = fixture.Session.SessionDate.AddDays(1),
            WorkflowVersion = SessionWorkflowVersion.BankReconciliation, Status = CashSessionStatus.Open,
            OpenedAt = BankingTestContext.Now.AddDays(1).AddHours(-1)
        };
        fixture.Db.CashSessions.Add(laterSession);
        await fixture.Db.SaveChangesAsync();
        Snapshot(laterSession, true, 150_000m, 950_000m, laterSession.OpenedAt);
        var now = BankingTestContext.Now.AddDays(1).AddHours(1);
        var service = CreateService(fixture, now);
        var returnForm = FollowUp(fixture, entry.Movements.Single().Id, AddEvidence(fixture), CustomerMovementKind.BankReversal);
        returnForm.CashSessionId = 0;
        returnForm.OccurredAt = BankingTestContext.Now.AddDays(1);
        entry = await service.RecordFollowUpAsync(fixture.User.Id, entry.Id, returnForm);
        var refund = FollowUp(fixture, entry.Movements.Last().Id, AddEvidence(fixture), CustomerMovementKind.Refund);
        refund.CashSessionId = laterSession.Id;
        refund.OccurredAt = returnForm.OccurredAt.AddMinutes(1);

        var completed = await service.RecordFollowUpAsync(fixture.User.Id, entry.Id, refund);
        var balances = new SessionBalanceService(fixture.Db, new BusinessClock(new FixedTimeProvider(now)));
        var oldPositions = await balances.GetPositionsAtSnapshotAsync(oldClosing.Id);
        var current = await balances.GetPositionsAsync(laterSession.Id, fixture.Agent.Id);

        Assert.Equal(150_000m, oldPositions.Single(p => p.WalletId == fixture.CashWallet.Id).ExpectedBalance);
        Assert.Equal(950_000m, oldPositions.Single(p => p.WalletId == fixture.BankWallet.Id).ExpectedBalance);
        Assert.Equal(100_000m, current.Single(p => p.WalletId == fixture.CashWallet.Id).ExpectedBalance);
        Assert.Equal(1_000_000m, current.Single(p => p.WalletId == fixture.BankWallet.Id).ExpectedBalance);
        Assert.Equal(laterSession.Id, completed.Movements.Single(m => m.Kind == CustomerMovementKind.Refund).CashSessionId);
        Assert.True(completed.IsOperationallyResolved);
        Assert.Equal(1_100_000m, (await fixture.Db.CashCounts.FindAsync(oldClosing.CashCountId))!.TotalAmount);

        CashCountSnapshot Snapshot(CashSession session, bool opening, decimal cash, decimal bank, DateTimeOffset observed)
        {
            var count = new CashCount
            {
                CashSessionId = session.Id, AgentId = fixture.Agent.Id, IsOpening = opening,
                CountDate = session.SessionDate, Status = opening ? CashCountStatus.Approved : CashCountStatus.PendingApproval,
                TotalAmount = cash + bank, CreatedAt = observed, SubmittedAt = observed
            };
            fixture.Db.CashCounts.Add(count);
            fixture.Db.SaveChanges();
            var snapshot = new CashCountSnapshot
            {
                CashCountId = count.Id, TotalAmount = cash + bank, CapturedAt = observed, CapturedByUserId = fixture.User.Id,
                WalletsJson = CountSnapshotCodec.Serialize(
                [
                    new(fixture.CashWallet.Id, "cash", WalletTypeEnum.Cash, "UGX", cash, observed, null),
                    new(fixture.BankWallet.Id, "bank", WalletTypeEnum.Bank, "UGX", bank, observed, fixture.Account.Id)
                ])
            };
            fixture.Db.CashCountSnapshots.Add(snapshot);
            fixture.Db.SaveChanges();
            return snapshot;
        }
    }

    [Fact]
    public async Task LinkedCorrection_PreservesSingleBankLegAndSupportsCorrectionDescendants()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var service = CreateService(fixture);
        var failed = CaptureForm(fixture);
        failed.Outcome = CustomerCaptureOutcome.Failed;
        failed.Notes = "Customer was not credited by the bank.";
        var entry = await service.CaptureAsync(fixture.User.Id, failed);
        entry = await service.RecordFollowUpAsync(fixture.User.Id, entry.Id,
            FollowUp(fixture, entry.Movements.Single().Id, AddEvidence(fixture), CustomerMovementKind.BankReversal));
        var reversal = entry.Movements.Last();
        var request = await GrantAsync(fixture, reversal.Id);
        var form = new CustomerMovementCorrectionForm
        {
            CommandId = Guid.NewGuid(), CorrectionRequestId = request.Id, MovementId = reversal.Id,
            TransactionKind = CustomerTransactionKind.Deposit, Outcome = CustomerCaptureOutcome.Completed,
            Amount = 50_000m, CashExchanged = false, BankMoved = true, CustomerIdentifier = entry.CustomerIdentifier,
            BankReference = "CORRECTED-RETURN", OccurredAt = reversal.OccurredAt,
            EvidenceFileId = AddEvidence(fixture), Reason = "Correct the bank return reference from its receipt."
        };
        var corrected = await service.CorrectAsync(fixture.User.Id, form);
        form.CommandId = Guid.NewGuid();
        form.MovementId = corrected.Movements.Last().Id;
        form.BankReference = "VERIFIED-RETURN";

        corrected = await service.CorrectAsync(fixture.User.Id, form);

        var active = corrected.Movements.Where(m => !m.IsSuperseded).ToList();
        Assert.Equal(50_000m, active.Sum(m => m.CashEffect));
        Assert.Equal(0m, active.Sum(m => m.BankEffect));
        Assert.Equal("VERIFIED-RETURN", active.Single(m => m.Kind == CustomerMovementKind.BankReversal).BankReference);
    }

    [Fact]
    public async Task Correction_CannotReduceBankReturnBelowAnExistingRefund()
    {
        using var fixture = new BankingTestContext();
        SessionBalanceTests.AddOpening(fixture);
        var service = CreateService(fixture);
        var failed = CaptureForm(fixture);
        failed.Outcome = CustomerCaptureOutcome.Failed;
        failed.Notes = "Customer did not receive the bank deposit.";
        var entry = await service.CaptureAsync(fixture.User.Id, failed);
        entry = await service.RecordFollowUpAsync(fixture.User.Id, entry.Id,
            FollowUp(fixture, entry.Movements.Single().Id, AddEvidence(fixture), CustomerMovementKind.BankReversal));
        var reversal = entry.Movements.Last();
        var refund = FollowUp(fixture, reversal.Id, AddEvidence(fixture), CustomerMovementKind.Refund);
        refund.OccurredAt = BankingTestContext.Now.AddMinutes(2);
        await service.RecordFollowUpAsync(fixture.User.Id, entry.Id, refund);
        var request = await GrantAsync(fixture, reversal.Id);

        await Assert.ThrowsAsync<BankingRuleException>(() => service.CorrectAsync(fixture.User.Id,
            new CustomerMovementCorrectionForm
            {
                CommandId = Guid.NewGuid(), CorrectionRequestId = request.Id, MovementId = reversal.Id,
                TransactionKind = CustomerTransactionKind.Deposit, Outcome = CustomerCaptureOutcome.Completed,
                Amount = 25_000m, CashExchanged = false, BankMoved = true, CustomerIdentifier = entry.CustomerIdentifier,
                BankReference = "SHORT-RETURN", OccurredAt = reversal.OccurredAt,
                EvidenceFileId = AddEvidence(fixture), Reason = "The corrected amount would not cover the paid refund."
            }));
        Assert.Equal(3, await fixture.Db.CustomerMovements.CountAsync());
    }

    private static async Task<CashCountCorrectionRequest> GrantAsync(BankingTestContext fixture, long movementId)
    {
        var closing = new CashCount
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id, IsOpening = false,
            CountDate = fixture.Session.SessionDate, Status = CashCountStatus.PendingApproval,
            TotalAmount = 1_100_000m, CreatedAt = BankingTestContext.Now.AddMinutes(30)
        };
        fixture.Db.CashCounts.Add(closing);
        await fixture.Db.SaveChangesAsync();
        fixture.Session.Status = CashSessionStatus.Closed;
        fixture.Session.OperationalClosedAt = BankingTestContext.Now.AddMinutes(30);
        var request = new CashCountCorrectionRequest
        {
            CashCountId = closing.Id, AgentId = fixture.Agent.Id, Reason = "Correct the original receipt details.",
            AllowedMovementIdsJson = $"[{movementId}]", RequestedByUserId = fixture.Admin.Id, RequestedAt = BankingTestContext.Now
        };
        fixture.Db.CashCountCorrectionRequests.Add(request);
        await fixture.Db.SaveChangesAsync();
        return request;
    }

    private static CustomerTransactionForm CaptureForm(BankingTestContext fixture) => new()
    {
        CommandId = Guid.NewGuid(), CashSessionId = fixture.Session.Id, BankAccountId = fixture.Account.Id,
        CashWalletId = fixture.CashWallet.Id, Kind = CustomerTransactionKind.Deposit,
        Outcome = CustomerCaptureOutcome.Completed, Amount = 50_000m,
        CustomerIdentifier = "SYNTHETIC-CUSTOMER", BankReference = "RECEIPT-REFERENCE",
        OccurredAt = BankingTestContext.Now
    };

    private static CustomerFollowUpForm FollowUp(
        BankingTestContext fixture, long movementId, long evidenceId, CustomerMovementKind kind) => new()
    {
        CommandId = Guid.NewGuid(), CashSessionId = fixture.Session.Id, RelatedMovementId = movementId,
        Kind = kind, Amount = 50_000m, Outcome = CustomerCaptureOutcome.Completed,
        BankReference = "BANK-FOLLOW-UP", OccurredAt = BankingTestContext.Now.AddMinutes(1),
        EvidenceFileId = evidenceId, Notes = "Verified the bank entry and attached its evidence."
    };

    private static long AddEvidence(BankingTestContext fixture)
    {
        var file = new BankEvidenceFile
        {
            BankAccountId = fixture.Account.Id, UploadKey = Guid.NewGuid(), FileName = "receipt.pdf",
            ContentType = "application/pdf", ContentHash = new string('1', 64), Content = [1],
            UploadedByUserId = fixture.User.Id, UploadedAt = BankingTestContext.Now
        };
        fixture.Db.BankEvidenceFiles.Add(file);
        fixture.Db.SaveChanges();
        return file.Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset? now = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now ?? BankingTestContext.Now.AddHours(1);
    }
}
