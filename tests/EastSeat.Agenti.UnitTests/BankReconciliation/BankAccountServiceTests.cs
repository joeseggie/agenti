using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankAccountServiceTests
{
    [Fact]
    public async Task CreatingUniversalAccount_BoundsDerivedBankIdentifierLength()
    {
        using var fixture = new BankingTestContext();
        fixture.Db.BankAccounts.Remove(fixture.Account);
        await fixture.Db.SaveChangesAsync();
        var clock = new BusinessClock(new ScenarioTimeProvider());
        var accounts = new BankAccountService(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            new StatementProfileCatalog([new UniversalBankCsvStatementProfile()]));
        var created = await accounts.CreateAsync(fixture.Admin.Id, new BankAccountForm
        {
            WalletId = fixture.BankWallet.Id, BankName = new string('b', 150),
            AccountIdentifier = "001234567890", BalanceBasis = BankBalanceBasis.Ledger
        });
        var stored = await fixture.Db.BankAccounts.AsNoTracking().SingleAsync(a => a.Id == created.Id);
        Assert.Equal(new string('B', 100), stored.BankIdentifier);
    }

    [Fact]
    public async Task EnablingUniversalWorkflow_PersistsVerificationAndAcceptsConfirmedAvailableBasis()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.BalanceBasis = BankBalanceBasis.Available;
        fixture.Account.ImportProfileKey = null;
        fixture.Account.ImportProfileVerified = false;
        await fixture.Db.SaveChangesAsync();
        var clock = new BusinessClock(new ScenarioTimeProvider());
        var accounts = new BankAccountService(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            new StatementProfileCatalog([new UniversalBankCsvStatementProfile()]));
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
}
