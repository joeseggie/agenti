using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.WalletTypes;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class StatementReconciliationSettingTests
{
    private static IBusinessClock Clock() => new BusinessClock(new ScenarioTimeProvider(BankingScenario.Now.AddDays(1)));
    private static StatementReconciliationSettingService Service(BankingScenario fixture) =>
        new(fixture.Db, new BankingAccessService(fixture.Db, Clock()), Clock());

    [Fact]
    public async Task Admin_SwitchesOffAndOn_WritesOneAuditRowPerChange_AndIsIdempotent()
    {
        using var fixture = new BankingTestContext();
        var wallet = fixture.AddWallet("Bank", WalletTypeEnum.Bank, 1m);
        var service = Service(fixture);

        await service.SetRequiredAsync(fixture.Admin.Id, wallet.WalletTypeId, false);
        await service.SetRequiredAsync(fixture.Admin.Id, wallet.WalletTypeId, false);

        Assert.False((await fixture.Db.WalletTypes.AsNoTracking().SingleAsync(t => t.Id == wallet.WalletTypeId)).RequiresStatementReconciliation);
        var audit = Assert.Single(await fixture.Db.WalletTypeReconciliationAudits.ToListAsync());
        Assert.Equal(fixture.Admin.Id, audit.ChangedByUserId);
        Assert.False(audit.Required);

        await service.SetRequiredAsync(fixture.Admin.Id, wallet.WalletTypeId, true);
        Assert.Equal(2, await fixture.Db.WalletTypeReconciliationAudits.CountAsync());
    }

    [Fact]
    public async Task NonBankType_IsRejected()
    {
        using var fixture = new BankingTestContext();
        var wallet = fixture.AddWallet("Mobile", WalletTypeEnum.MobileMoney, 1m);
        await Assert.ThrowsAsync<BankingRuleException>(() => Service(fixture).SetRequiredAsync(fixture.Admin.Id, wallet.WalletTypeId, false));
    }

    [Theory]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Agent)]
    public async Task NonAdmin_IsRejected(UserRole role)
    {
        using var fixture = new BankingTestContext();
        var wallet = fixture.AddWallet("Bank", WalletTypeEnum.Bank, 1m);
        var user = fixture.Db.Users.FirstOrDefault(u => u.Role == role);
        if (user == null)
        {
            user = new Web.Data.ApplicationUser
            {
                Id = Guid.NewGuid().ToString(), UserName = $"{role}x", Email = $"{role}@x.test", FirstName = "A", LastName = "B",
                Role = role, BranchId = fixture.Admin.BranchId, IsActive = true
            };
            fixture.Db.Users.Add(user);
            fixture.Db.SaveChanges();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(fixture).SetRequiredAsync(user.Id, wallet.WalletTypeId, false));
        Assert.Empty(await fixture.Db.WalletTypeReconciliationAudits.ToListAsync());
    }

    [Fact]
    public async Task WalletTypeWithAuditHistory_CannotBeDeleted_AndWaivedBankCannotBecomeCustom()
    {
        using var fixture = new BankingTestContext();
        var wallet = fixture.AddWallet("Bank", WalletTypeEnum.Bank, 1m);
        await Service(fixture).SetRequiredAsync(fixture.Admin.Id, wallet.WalletTypeId, false);
        var walletTypes = new WalletTypeService(fixture.Db);
        var type = await fixture.Db.WalletTypes.AsNoTracking().SingleAsync(t => t.Id == wallet.WalletTypeId);

        var change = await walletTypes.UpdateAsync(new WalletTypeFormModel
        {
            Id = type.Id, Name = type.Name, Type = WalletTypeEnum.Custom, IsActive = true
        });
        Assert.False(change.Success);

        fixture.Db.Wallets.Remove(await fixture.Db.Wallets.SingleAsync(w => w.Id == wallet.Id));
        fixture.Db.SaveChanges();
        Assert.False((await walletTypes.DeleteAsync(type.Id)).Success);
    }

    [Fact]
    public async Task StaleTrackedWalletType_IsReloadedBeforeSettingOrTypeChange()
    {
        using var fixture = new BankingTestContext();
        var wallet = fixture.AddWallet("Bank", WalletTypeEnum.Bank, 1m);
        var tracked = await fixture.Db.WalletTypes.SingleAsync(t => t.Id == wallet.WalletTypeId);
        Assert.True(tracked.RequiresStatementReconciliation);
        // Another Admin waives the type; the tracked entity keeps its stale value.
        await fixture.Db.WalletTypes.Where(t => t.Id == tracked.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RequiresStatementReconciliation, false));

        var change = await new WalletTypeService(fixture.Db).UpdateAsync(new WalletTypeFormModel
        {
            Id = tracked.Id, Name = tracked.Name, Type = WalletTypeEnum.Custom, IsActive = true
        });
        Assert.False(change.Success);

        await Service(fixture).SetRequiredAsync(fixture.Admin.Id, tracked.Id, true);
        Assert.True((await fixture.Db.WalletTypes.AsNoTracking().SingleAsync(t => t.Id == tracked.Id)).RequiresStatementReconciliation);
        Assert.True(Assert.Single(await fixture.Db.WalletTypeReconciliationAudits.ToListAsync()).Required);
    }
}
