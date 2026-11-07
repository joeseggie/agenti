using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.Agents;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.Vaults;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EastSeat.Agenti.UnitTests.Api;

public sealed class MobileContractTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1.001)]
    public async Task SaveCashCount_RejectsInvalidMoneyWithoutWrites(decimal amount)
    {
        using var scenario = new BankingTestContext();
        var service = Counts(scenario);
        var result = await service.SaveCashCountAsync(scenario.User.Id, new CashCountFormModel
        {
            IsOpening = true,
            WalletEntries = [new() { WalletId = scenario.CashWallet.Id, CountedAmount = amount }]
        });
        Assert.False(result.Success);
        Assert.Contains("non-negative", result.ErrorMessage);
        Assert.Equal(0, await scenario.Db.CashCounts.CountAsync());
    }

    [Fact]
    public async Task GetAgents_UsesBothScopeFilters()
    {
        using var scenario = new BankingTestContext();
        var service = new AgentService(scenario.Db);
        Assert.Single(await service.GetAgentsAsync(scenario.Agent.BranchId, scenario.Agent.Id));
        Assert.Empty(await service.GetAgentsAsync(scenario.Agent.BranchId!.Value + 1, scenario.Agent.Id));
        Assert.Empty(await service.GetAgentsAsync(scenario.Agent.BranchId, scenario.Agent.Id + 1));
    }

    [Fact]
    public async Task GetCashCountForm_ApprovedLegacyCountIsReadOnly()
    {
        using var scenario = new BankingTestContext();
        scenario.Session.WorkflowVersion = SessionWorkflowVersion.Legacy;
        scenario.Db.SaveChanges();
        scenario.AddApprovedOpening();
        var count = await scenario.Db.CashCounts.SingleAsync();
        var form = await Counts(scenario).GetCashCountFormAsync(scenario.User.Id, count.Id);
        Assert.True(form!.IsReadOnly);
    }

    [Fact]
    public void DenominationTotals_DoNotOverflowForLargeValidQuantities()
    {
        var denominations = new DenominationBreakdown
        {
            Notes = new() { ["50000"] = 50_000 },
            Coins = new() { ["1000"] = 1 }
        };
        Assert.Equal(2_500_001_000m, denominations.Total);
        Assert.Equal(denominations.Total, DenominationBreakdown.FromJson(denominations.ToJson())!.Total);
    }

    private static CashCountService Counts(BankingTestContext scenario) => new(scenario.Db,
        Mock.Of<IVaultService>(), Mock.Of<INotificationService>(), Mock.Of<IWalletAdjustmentService>(),
        Mock.Of<IBankSessionWorkflow>());
}
