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
    public async Task LegacyReconciliationArchive_RejectsNewWrites()
    {
        using var fixture = new BankingTestContext();
        fixture.Db.BankStatementImports.Add(new BankStatementImport
        {
            BankAccountId = fixture.Account.Id, UploadedByUserId = fixture.Admin.Id
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
        Assert.Contains("archived and read-only", error.Message);
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

}
