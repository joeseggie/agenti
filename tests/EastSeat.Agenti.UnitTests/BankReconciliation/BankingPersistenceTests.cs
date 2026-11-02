using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankingPersistenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FinancialHistory_AllSaveOverloadsRejectMutation(int overload)
    {
        using var fixture = new BankingTestContext();
        var transaction = fixture.AddCustomerTransaction();
        transaction.Amount += 1m;

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () =>
        {
            switch (overload)
            {
                case 0: fixture.Db.SaveChanges(); break;
                case 1: fixture.Db.SaveChanges(true); break;
                case 2: await fixture.Db.SaveChangesAsync(); break;
                case 3: await fixture.Db.SaveChangesAsync(true); break;
            }
        });

        Assert.Contains("immutable", error.Message);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(50_000m, (await fixture.Db.CustomerTransactions.SingleAsync()).Amount);
    }

    [Fact]
    public async Task FinancialHistory_DeletionIsRejected()
    {
        using var fixture = new BankingTestContext();
        fixture.Db.CustomerTransactions.Remove(fixture.AddCustomerTransaction());

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task StatementRows_IdenticalFallbackFieldsPreserveBothOccurrences()
    {
        using var fixture = new BankingTestContext();
        var import = fixture.AddImport();
        for (var sequence = 1; sequence <= 2; sequence++)
        {
            fixture.Db.BankStatementRows.Add(new BankStatementRow
            {
                ImportId = import.Id, Sequence = sequence, SourceLocation = $"row {sequence}",
                RawData = "synthetic", CustomerIdentifier = "SAME-CUSTOMER", SignedAmount = -50_000m,
                RunningBalance = 1_000_000m - sequence * 50_000m, OccurredAt = BankingTestContext.Now
            });
        }
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(2, await fixture.Db.BankStatementRows.CountAsync());
    }

    [Fact]
    public async Task CustomerCommands_DuplicateIdentityIsRejectedByDatabase()
    {
        using var fixture = new BankingTestContext();
        var first = fixture.AddCustomerTransaction();
        fixture.Db.CustomerTransactions.Add(new CustomerTransaction
        {
            BankAccountId = first.BankAccountId, AgentId = first.AgentId,
            OriginatingSessionId = first.OriginatingSessionId, CashWalletId = first.CashWalletId,
            Kind = first.Kind, Amount = first.Amount, CustomerIdentifier = first.CustomerIdentifier,
            CommandId = first.CommandId, RecordedByUserId = first.RecordedByUserId,
            OccurredAt = first.OccurredAt, RecordedAt = first.RecordedAt
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Match_MultipleInternalSourcesAreRejectedByDatabase()
    {
        using var fixture = new BankingTestContext();
        var customer = fixture.AddCustomerTransaction();
        var movement = new CustomerMovement
        {
            CustomerTransactionId = customer.Id, CashSessionId = fixture.Session.Id,
            Kind = CustomerMovementKind.Completed, CashEffect = 50_000m, BankEffect = -50_000m,
            CommandId = Guid.NewGuid(), RecordedByUserId = fixture.User.Id,
            OccurredAt = BankingTestContext.Now, RecordedAt = BankingTestContext.Now
        };
        var bankRun = new BankRun
        {
            CashSessionId = fixture.Session.Id, AgentId = fixture.Agent.Id,
            FromWalletId = fixture.CashWallet.Id, ToWalletId = fixture.BankWallet.Id,
            Amount = 50_000m, RecordedByUserId = fixture.User.Id, CreatedAt = BankingTestContext.Now
        };
        fixture.Db.AddRange(movement, bankRun);
        var import = fixture.AddImport();
        var row = new BankStatementRow
        {
            ImportId = import.Id, Sequence = 1, SourceLocation = "row 1", RawData = "synthetic",
            SignedAmount = -50_000m, RunningBalance = 950_000m, OccurredAt = BankingTestContext.Now
        };
        fixture.Db.BankStatementRows.Add(row);
        await fixture.Db.SaveChangesAsync();
        var bankTransaction = new BankTransaction
        {
            BankAccountId = fixture.Account.Id, AcceptedRowId = row.Id, CreatedAt = BankingTestContext.Now
        };
        fixture.Db.BankTransactions.Add(bankTransaction);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.BankReconciliationMatches.Add(new BankReconciliationMatch
        {
            BankTransactionId = bankTransaction.Id, CustomerMovementId = movement.Id, BankRunId = bankRun.Id,
            MatchedByUserId = fixture.Admin.Id, MatchedAt = BankingTestContext.Now
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }
}
