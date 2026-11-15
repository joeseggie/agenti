using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.Agents;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.WalletTypes;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class WalletEnrollmentTests
{
    [Fact]
    public async Task Enrollment_AllowsDistinctBankAndMobileMoneyWalletsAndBothBankAccounts()
    {
        using var fixture = new BankingTestContext();
        var otherBank = fixture.AddWallet("ABSA", WalletTypeEnum.Bank);
        var otherAccount = fixture.BindBankAccount(otherBank);
        fixture.AddWallet("Airtel Money", WalletTypeEnum.MobileMoney);
        fixture.AddWallet("MTN", WalletTypeEnum.MobileMoney);
        var inactiveCash = fixture.AddWallet("Inactive historical cash", WalletTypeEnum.Cash);
        inactiveCash.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        var service = Accounts(fixture);

        var first = await service.SetWorkflowAsync(fixture.Admin.Id, fixture.Account.Id, true, fixture.Account.Version);
        var second = await service.SetWorkflowAsync(fixture.Admin.Id, otherAccount.Id, true, otherAccount.Version);

        Assert.True(first.WorkflowEnabled);
        Assert.True(second.WorkflowEnabled);
        Assert.Equal(fixture.BankWallet.Id, first.WalletId);
        Assert.Equal(otherBank.Id, second.WalletId);
        Assert.Empty(await fixture.Db.VaultTransactions.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Enrollment_RejectsInvalidActiveCashCount(int cashCount)
    {
        using var fixture = new BankingTestContext();
        if (cashCount == 0) fixture.CashWallet.IsActive = false;
        else fixture.AddWallet("Other cash", WalletTypeEnum.Cash);
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<BankingRuleException>(() =>
            Accounts(fixture).SetWorkflowAsync(fixture.Admin.Id, fixture.Account.Id, true, fixture.Account.Version));

        Assert.Contains("exactly one active Cash wallet", exception.Message);
        Assert.False((await fixture.Db.BankAccounts.AsNoTracking().SingleAsync()).WorkflowEnabled);
    }

    [Fact]
    public async Task WalletAssignments_AllowDistinctProvidersButRejectDuplicateCatalogType()
    {
        using var fixture = new BankingTestContext();
        var service = new AgentService(fixture.Db);
        var types = new[]
        {
            new WalletType { Name = "ABSA", Type = WalletTypeEnum.Bank, IsActive = true },
            new WalletType { Name = "Airtel Money test", Type = WalletTypeEnum.MobileMoney, IsActive = true },
            new WalletType { Name = "MTN test", Type = WalletTypeEnum.MobileMoney, IsActive = true }
        };
        fixture.Db.WalletTypes.AddRange(types);
        await fixture.Db.SaveChangesAsync();
        foreach (var type in types)
        {
            var form = new WalletFormModel
                { AgentId = fixture.Agent.Id, WalletTypeId = type.Id, Name = type.Name, Currency = "UGX", IsActive = true };
            Assert.True((await service.AddWalletAsync(form)).Success);
            Assert.False((await service.AddWalletAsync(form)).Success);
        }
        var wallet = await fixture.Db.Wallets.SingleAsync(w => w.WalletTypeId == types[0].Id);
        var edit = await service.UpdateWalletAsync(new WalletFormModel
        {
            Id = wallet.Id, AgentId = fixture.Agent.Id, WalletTypeId = fixture.BankWallet.WalletTypeId,
            Name = "Duplicate", Currency = "UGX", IsActive = true
        });

        Assert.False(edit.Success);
        Assert.Equal(types[0].Id, (await fixture.Db.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id)).WalletTypeId);
    }

    [Fact]
    public async Task CashCategory_UsesCurrentCatalogCategoryForAssignmentAndWalletEdits()
    {
        using var fixture = new BankingTestContext();
        var type = new WalletType { Name = "Reclassified provider", Type = WalletTypeEnum.Bank, IsActive = true };
        fixture.Db.WalletTypes.Add(type);
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.WalletTypes.Where(t => t.Id == type.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Type, WalletTypeEnum.Cash));
        var service = new AgentService(fixture.Db);
        var form = new WalletFormModel
            { AgentId = fixture.Agent.Id, WalletTypeId = type.Id, Name = "Other cash", Currency = "UGX", IsActive = true };

        Assert.False((await service.AddWalletAsync(form)).Success);
        form.Id = fixture.BankWallet.Id;
        Assert.False((await service.UpdateWalletAsync(form)).Success);
        Assert.Equal(fixture.BankWallet.WalletTypeId,
            (await fixture.Db.Wallets.AsNoTracking().SingleAsync(w => w.Id == fixture.BankWallet.Id)).WalletTypeId);
        Assert.Equal(2, await fixture.Db.Wallets.CountAsync(w => w.AgentId == fixture.Agent.Id));
    }

    [Fact]
    public async Task CashCategory_CannotBeDuplicatedByAssignmentEditOrCatalogReclassification()
    {
        using var fixture = new BankingTestContext();
        var type = new WalletType { Name = "Second cash type", Type = WalletTypeEnum.Cash, IsActive = true };
        fixture.Db.WalletTypes.Add(type);
        await fixture.Db.SaveChangesAsync();
        var service = new AgentService(fixture.Db);
        var form = new WalletFormModel
            { AgentId = fixture.Agent.Id, WalletTypeId = type.Id, Name = "Other cash", Currency = "UGX", IsActive = true };

        Assert.False((await service.AddWalletAsync(form)).Success);
        form.Id = fixture.BankWallet.Id;
        Assert.False((await service.UpdateWalletAsync(form)).Success);
        Assert.DoesNotContain(await service.GetAvailableWalletTypesForAgentAsync(fixture.Agent.Id), t => t.Id == type.Id);
        var bankType = await fixture.Db.WalletTypes.FindAsync(fixture.BankWallet.WalletTypeId);
        var changed = await new WalletTypeService(fixture.Db).UpdateAsync(new WalletTypeFormModel
        {
            Id = bankType!.Id, Name = bankType.Name, Type = WalletTypeEnum.Cash, IsActive = true
        });

        Assert.False(changed.Success);
        Assert.Equal(WalletTypeEnum.Bank, bankType.Type);
        Assert.Equal(2, await fixture.Db.Wallets.CountAsync(w => w.AgentId == fixture.Agent.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonBankAgents_KeepExistingSessionRouting(bool withMobileMoney)
    {
        using var fixture = new BankingTestContext();
        fixture.Db.BankAccounts.Remove(fixture.Account);
        fixture.Db.Wallets.Remove(fixture.BankWallet);
        fixture.Session.WorkflowVersion = SessionWorkflowVersion.Legacy;
        await fixture.Db.SaveChangesAsync();
        if (withMobileMoney) fixture.AddWallet("Airtel", WalletTypeEnum.MobileMoney);
        var clock = new BusinessClock(new FixedTimeProvider());
        var workflow = new BankSessionWorkflow(fixture.Db, new BankingAccessService(fixture.Db, clock),
            clock, new SessionBalanceService(fixture.Db, clock), Mock.Of<ISessionSettlementService>());

        Assert.False(await workflow.IsModernBranchAsync(fixture.Account.BranchId));
        Assert.False(await workflow.HandlesFormAsync(fixture.Account.BranchId,
            new CashCountFormModel { IsOpening = true, CountDate = fixture.Session.SessionDate }));
    }

    private static BankAccountService Accounts(BankingTestContext fixture)
    {
        var clock = new BusinessClock(new FixedTimeProvider());
        return new BankAccountService(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            new StatementProfileCatalog([new UniversalBankCsvStatementProfile()]));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => BankingTestContext.Now.AddHours(1);
    }
}
