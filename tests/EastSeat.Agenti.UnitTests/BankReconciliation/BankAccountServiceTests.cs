using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankAccountServiceTests
{
    [Fact]
    public async Task MissingBalancePermission_DefaultsOffAndRequiresAdminWithCurrentVersion()
    {
        using var fixture = new BankingTestContext();
        var service = CreateService(fixture);
        var original = Assert.Single(await service.GetAccountsAsync(fixture.Admin.Id));
        Assert.False(original.AllowMissingRunningBalances);
        var form = new BankAccountForm
        {
            WalletId = original.WalletId, BankName = original.BankName,
            AccountIdentifier = original.AccountIdentifier, TimeZoneId = original.TimeZoneId,
            BalanceBasis = original.BalanceBasis, AllowMissingRunningBalances = true
        };
        fixture.User.Role = UserRole.Supervisor;
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ConfigureAsync(fixture.User.Id, original.Id, form, original.Version));
        var changed = await service.ConfigureAsync(fixture.Admin.Id, original.Id, form, original.Version);
        Assert.True(changed.AllowMissingRunningBalances);
        Assert.True(Assert.Single(await service.GetAccountsAsync(fixture.Admin.Id)).AllowMissingRunningBalances);
        form.AllowMissingRunningBalances = false;
        await Assert.ThrowsAsync<BankingConflictException>(() =>
            service.ConfigureAsync(fixture.Admin.Id, original.Id, form, original.Version));
        var disabled = await service.ConfigureAsync(fixture.Admin.Id, original.Id, form, changed.Version);
        Assert.False(disabled.AllowMissingRunningBalances);
    }

    [Fact]
    public async Task CreatingUniversalAccount_BoundsDerivedBankIdentifierLength()
    {
        using var fixture = new BankingTestContext();
        fixture.Db.BankAccounts.Remove(fixture.Account);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var accounts = CreateService(fixture);
        var created = await accounts.CreateAsync(fixture.Admin.Id, new BankAccountForm
        {
            WalletId = fixture.BankWallet.Id, BankName = new string('b', 150),
            AccountIdentifier = "001234567890", BalanceBasis = BankBalanceBasis.Ledger
        });
        var stored = await fixture.Db.BankAccounts.AsNoTracking().SingleAsync(a => a.Id == created.Id);
        Assert.Equal(new string('B', 100), stored.BankIdentifier);
        Assert.Equal(fixture.BankWallet.Name, created.WalletName);
        Assert.Equal(fixture.User.FullName, created.AgentName);
        Assert.Equal(fixture.Agent.Code, created.AgentCode);
    }

    [Fact]
    public async Task EnablingUniversalWorkflow_PersistsVerificationAndAcceptsConfirmedAvailableBasis()
    {
        using var fixture = new BankingTestContext();
        fixture.Account.BalanceBasis = BankBalanceBasis.Available;
        fixture.Account.ImportProfileKey = null;
        fixture.Account.ImportProfileVerified = false;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var accounts = CreateService(fixture);
        var configured = await accounts.ConfigureAsync(fixture.Admin.Id, fixture.Account.Id, new BankAccountForm
        {
            WalletId = fixture.BankWallet.Id, BankName = fixture.Account.BankName,
            AccountIdentifier = fixture.Account.AccountIdentifier, TimeZoneId = fixture.Account.TimeZoneId,
            BalanceBasis = BankBalanceBasis.Available
        }, fixture.Account.Version);
        Assert.Equal(fixture.BankWallet.Name, configured.WalletName);
        Assert.Equal(fixture.User.FullName, configured.AgentName);
        Assert.Equal(fixture.Agent.Code, configured.AgentCode);
        fixture.Db.ChangeTracker.Clear();
        var enabled = await accounts.SetWorkflowAsync(fixture.Admin.Id, fixture.Account.Id, true, configured.Version);
        Assert.True(enabled.WorkflowEnabled);
        Assert.Equal(BankBalanceBasis.Available, enabled.BalanceBasis);
        Assert.Equal(fixture.BankWallet.Name, enabled.WalletName);
        Assert.Equal(fixture.User.FullName, enabled.AgentName);
        Assert.Equal(fixture.Agent.Code, enabled.AgentCode);
        var stored = await fixture.Db.BankAccounts.AsNoTracking().SingleAsync(a => a.Id == enabled.Id);
        Assert.Equal(UniversalBankCsvStatementProfile.ProfileKey, stored.ImportProfileKey);
        Assert.True(stored.ImportProfileVerified);
    }

    [Fact]
    public async Task GetAccountsAsync_UsesEachWalletOwnerAndExcludesOtherBranches()
    {
        using var fixture = new BankingTestContext();
        var second = await AddAccountAsync(fixture.Db, fixture.Account.BranchId, "OTHR");
        var otherBranch = new Branch { Name = "Other branch", CreatedAt = BankingScenario.Now };
        fixture.Db.Branches.Add(otherBranch);
        await fixture.Db.SaveChangesAsync();
        var foreign = await AddAccountAsync(fixture.Db, otherBranch.Id, "FRGN");
        fixture.Db.ChangeTracker.Clear();

        var accounts = await CreateService(fixture).GetAccountsAsync(fixture.Admin.Id);

        Assert.Equal(2, accounts.Count);
        Assert.All(accounts, account =>
        {
            Assert.Equal(fixture.User.FullName, account.AgentName);
            Assert.Equal(fixture.BankWallet.Name, account.WalletName);
        });
        Assert.Equal(fixture.Agent.Code, accounts.Single(a => a.WalletId == fixture.BankWallet.Id).AgentCode);
        Assert.Equal("OTHR", accounts.Single(a => a.WalletId == second.WalletId).AgentCode);
        Assert.DoesNotContain(accounts, a => a.Id == foreign.Id);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task GetAccountsAsync_InactiveOrDeletedOwner_KeepsAccount(
        bool agentActive, bool userActive, bool userDeleted)
    {
        using var fixture = new BankingTestContext();
        fixture.Agent.IsActive = agentActive;
        fixture.User.IsActive = userActive;
        fixture.User.IsDeleted = userDeleted;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var account = Assert.Single(await CreateService(fixture).GetAccountsAsync(fixture.Admin.Id));

        Assert.Equal(fixture.Account.Id, account.Id);
        Assert.Equal(fixture.BankWallet.Name, account.WalletName);
        Assert.Equal(fixture.Agent.Code, account.AgentCode);
        Assert.Equal(userDeleted ? null : fixture.User.FullName, account.AgentName);
    }

    [Fact]
    public async Task GetAccountsAsync_AfterUserRename_ReturnsCurrentNameWithStatementHistory()
    {
        using var fixture = new BankingTestContext();
        var clock = new BusinessClock(new ScenarioTimeProvider());
        await fixture.UploadStatementAsync(fixture.Account, clock,
            BankingScenario.StatementCsv(fixture.Session.SessionDate, 1_000_000m));
        fixture.User.FirstName = "Renamed";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var account = Assert.Single(await CreateService(fixture).GetAccountsAsync(fixture.Admin.Id));

        Assert.Equal("Renamed Agent", account.AgentName);
        Assert.Equal(fixture.Agent.Code, account.AgentCode);
    }

    private static BankAccountService CreateService(BankingTestContext fixture)
    {
        var clock = new BusinessClock(new ScenarioTimeProvider());
        return new BankAccountService(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            new StatementProfileCatalog([new UniversalBankCsvStatementProfile()]));
    }

    private static async Task<BankAccount> AddAccountAsync(ApplicationDbContext db, long branchId, string code)
    {
        var account = new BankAccount
        {
            BranchId = branchId, BankName = "Test bank", AccountIdentifier = $"SYNTHETIC-{code}",
            BalanceBasis = BankBalanceBasis.Ledger, CreatedAt = BankingScenario.Now,
            Wallet = new Wallet
            {
                WalletTypeId = 4, Name = "Test bank", Currency = "UGX", CreatedAt = BankingScenario.Now,
                Agent = new Agent
                {
                    BranchId = branchId, Code = code, CreatedAt = BankingScenario.Now,
                    User = new ApplicationUser
                    {
                        Id = Guid.NewGuid().ToString("N"), FirstName = "Test", LastName = "Agent",
                        BranchId = branchId, Role = UserRole.Agent
                    }
                }
            }
        };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }
}
