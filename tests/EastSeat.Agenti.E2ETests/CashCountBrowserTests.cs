using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.CashCounts;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EastSeat.Agenti.E2ETests;

public sealed class CashCountBrowserTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Closing_OffsettingWalletVariances_CanEnterExplanationAndSubmit()
    {
        await using var db = database.CreateContext();
        using var scenario = new BankingScenario(db);
        scenario.AddApprovedOpening(withSettlement: true);
        scenario.Account.WorkflowEnabled = true;
        (await db.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        await db.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(0);
        var root = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        using var scope = app.Services.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<ICashCountService>();
        var closing = await counts.InitializeCashCountFormAsync(scenario.User.Id, false);
        foreach (var wallet in closing.WalletEntries)
        {
            wallet.CountedAmount = wallet.ExpectedBalance + (wallet.WalletId == scenario.CashWallet.Id ? 50_000m : -50_000m);
            wallet.Denominations = null;
            wallet.ObservedAt = app.Clock.Now;
        }
        Assert.Equal(0m, closing.TotalVariance);
        var saved = await counts.SaveCashCountAsync(scenario.User.Id, closing);
        Assert.True(saved.Success, saved.ErrorMessage);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Channel = Environment.GetEnvironmentVariable("AGENTI_TEST_BROWSER_CHANNEL")
        });
        await using var agent = await browser.NewContextAsync();
        var cookie = app.Cookie(scenario.User);
        await agent.AddCookiesAsync([new Cookie { Name = cookie.Name, Value = cookie.Value, Url = root, HttpOnly = true }]);
        var page = await agent.NewPageAsync();
        await page.GotoAsync(root + "/customer-transactions");
        // Wait for the circuit before navigating so the closing button is not clicked during prerendering.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save transaction", Exact = true }))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });
        await page.GetByRole(AriaRole.Link, new() { Name = "Cash Count", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue Closing Draft", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save Draft", Exact = true })).ToBeVisibleAsync();
        var explanation = page.GetByLabel("Explanation for wallet differences");
        await Expect(explanation).ToBeVisibleAsync();
        const string reason = "An unrecorded customer deposit explains the offsetting wallet differences.";
        await explanation.FillAsync(reason);
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit for Approval", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Submitted closing observations are locked.", new() { Exact = false })).ToBeVisibleAsync();

        await using var verify = database.CreateContext();
        var submitted = await verify.CashCounts.FindAsync(saved.CashCountId);
        Assert.Equal(CashCountStatus.PendingApproval, submitted!.Status);
        Assert.Equal(reason, submitted.Explanation);
        Assert.Single(await verify.SessionFloatSettlements.Where(s => s.Stage == FloatSettlementStage.Closing).ToListAsync());
    }
}
