using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Features.CashCounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EastSeat.Agenti.E2ETests;

public sealed class ReconciliationBrowserTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Reconciliation_ReceiptToRetrospectiveApproval_PreservesLaterSessionBalances()
    {
        await using var db = database.CreateContext();
        using var scenario = new BankingScenario(db);
        scenario.AddApprovedOpening(withSettlement: true);
        scenario.Account.WorkflowEnabled = true;
        scenario.Account.ImportProfileVerified = true;
        scenario.Account.ImportProfileKey = SyntheticStatementProfile.ProfileKey;
        scenario.Account.BankIdentifier = "synthetic-test-bank";
        (await db.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        await db.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(0);
        var root = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        using var client = app.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri(root) });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Channel = Environment.GetEnvironmentVariable("AGENTI_TEST_BROWSER_CHANNEL")
        });
        await using var agent = await browser.NewContextAsync();
        await AddCookieAsync(agent, app, scenario.User, root);
        var page = await agent.NewPageAsync();
        await page.GotoAsync(root + "/customer-transactions");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Customer transactions", Exact = true })).ToBeVisibleAsync();
        // Banking controls stay disabled until the Blazor circuit is interactive; wait so input is not lost to prerendered markup.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save transaction", Exact = true })).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await page.GetByLabel("Amount", new() { Exact = true }).FillAsync("50000");
        await page.GetByLabel("Customer account / phone", new() { Exact = true }).FillAsync("0012345");
        await page.GetByLabel("Bank transaction number", new() { Exact = true }).FillAsync("SYNTHETIC-DEPOSIT");
        await page.GetByLabel("Receipt date/time (Uganda)", new() { Exact = true }).FillAsync("2026-11-02 12:00:00");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save transaction", Exact = true }).ClickAsync();
        await Expect(page.Locator("tbody tr").Filter(new() { HasText = "0012345" })).ToHaveCountAsync(1);
        await using (var verify = database.CreateContext())
        {
            Assert.Equal(1, await verify.CustomerTransactions.CountAsync());
            Assert.Equal(100_000m, (await verify.Wallets.FindAsync(scenario.CashWallet.Id))!.Balance);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));
        var closing = await GetAsync<CashCountFormModel>(client, "/api/banking/counts/initialize?isOpening=false");
        foreach (var wallet in closing.WalletEntries)
        {
            wallet.CountedAmount = wallet.ExpectedBalance;
            wallet.Denominations = null;
            wallet.ObservedAt = app.Clock.Now;
        }
        var saved = await PostAsync<CashCountFormModel, CashCountSaveResult>(client, "/api/banking/counts", closing);
        using (var submitted = await client.PostAsync($"/api/banking/counts/{saved.CashCountId}/submit", null))
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var frozen = await GetAsync<CashCountFormModel>(client, $"/api/banking/counts/{saved.CashCountId}");
        Assert.True(frozen.IsReadOnly);
        using (var edit = await client.PostAsJsonAsync("/api/banking/counts", frozen))
            Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);

        app.Clock.Now = BankingScenario.Now.AddDays(1).AddHours(1);
        var opening = await GetAsync<CashCountFormModel>(client, "/api/banking/counts/initialize?isOpening=true");
        foreach (var wallet in opening.WalletEntries)
        {
            wallet.CountedAmount = wallet.WalletId == scenario.CashWallet.Id ? 150_000m : 950_000m;
            wallet.Denominations = null;
            wallet.ObservedAt = app.Clock.Now;
        }
        var laterOpening = await PostAsync<CashCountFormModel, CashCountSaveResult>(client, "/api/banking/counts", opening);
        using (var submitted = await client.PostAsync($"/api/banking/counts/{laterOpening.CashCountId}/submit", null))
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.Admin));
        using (var approved = await client.PostAsync($"/api/banking/counts/{laterOpening.CashCountId}/approve", null))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        await using var administrator = await browser.NewContextAsync();
        await AddCookieAsync(administrator, app, scenario.Admin, root);
        var adminPage = await administrator.NewPageAsync();
        await adminPage.GotoAsync(root + "/bank-statements");
        await Expect(adminPage.Locator("input[type=file]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await FillDateAsync(adminPage, "Start date", "2026-11-02");
        await FillDateAsync(adminPage, "End date", "2026-11-02");
        var source = new ParsedStatement("SYNTHETIC-ACCOUNT", "UGX", BankBalanceBasis.Ledger,
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.FromHours(3)),
            1_000_000m, 950_000m, true,
            [new("synthetic row", "synthetic fixture", "SYNTHETIC-DEPOSIT", "SYNTHETIC-DEPOSIT",
                "0012345", "synthetic customer deposit", "UGX", -50_000m, 950_000m,
                BankingScenario.Now, null, null, true)]);
        await adminPage.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "synthetic-statement.csv", MimeType = "text/csv", Buffer = JsonSerializer.SerializeToUtf8Bytes(source)
        });
        var upload = adminPage.GetByRole(AriaRole.Button, new() { Name = "Upload and validate", Exact = true });
        await Expect(adminPage.GetByText("synthetic-statement.csv", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(upload).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await upload.ClickAsync();
        var refresh = adminPage.GetByRole(AriaRole.Button, new() { Name = "Refresh preview", Exact = true });
        await Expect(refresh).ToBeVisibleAsync();
        var commit = adminPage.GetByRole(AriaRole.Button, new() { Name = "Commit reviewed bank entries", Exact = true });
        for (var attempt = 0; attempt < 30 && !await commit.IsEnabledAsync(); attempt++)
        {
            await Task.Delay(500);
            await refresh.ClickAsync();
        }
        await Expect(commit).ToBeEnabledAsync();
        await commit.ClickAsync();
        await Expect(adminPage.GetByRole(AriaRole.Heading, new() { NameRegex = new("Import #.*Committed") })).ToBeVisibleAsync();

        await adminPage.GotoAsync($"{root}/bank-reconciliation?sessionId={scenario.Session.Id}");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Start review", Exact = true }).ClickAsync();
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Match unique exact entries", Exact = true }).ClickAsync();
        var approve = adminPage.GetByRole(AriaRole.Button, new() { Name = "Approve against this evidence", Exact = true });
        await Expect(approve).ToBeEnabledAsync();
        await approve.ClickAsync();
        await Expect(adminPage.GetByText("Closing approval: Approved; reconciliation: Reconciled", new() { Exact = true })).ToBeVisibleAsync();

        await using var final = database.CreateContext();
        Assert.Equal(150_000m, (await final.Wallets.FindAsync(scenario.CashWallet.Id))!.Balance);
        Assert.Equal(950_000m, (await final.Wallets.FindAsync(scenario.BankWallet.Id))!.Balance);
        Assert.Equal(0m, (await final.Vaults.SingleAsync()).CurrentBalance);
        Assert.Equal(CashCountStatus.Approved, (await final.CashCounts.FindAsync(saved.CashCountId))!.Status);
        Assert.Equal(1, await final.SessionFloatSettlements.CountAsync(s => s.Stage == FloatSettlementStage.Closing));
        Assert.Equal(1, await final.BankReconciliationResults.CountAsync());
    }

    private static async Task FillDateAsync(IPage page, string label, string isoDate)
    {
        var picker = page.GetByLabel(label, new() { Exact = true });
        await picker.FillAsync(isoDate);
        // MudBlazor parses typed dates when the field loses focus.
        await picker.PressAsync("Tab");
        await Expect(picker).ToHaveValueAsync(isoDate);
    }

    private static async Task AddCookieAsync(IBrowserContext browser, BankingApplicationFactory app, ApplicationUser user, string root)
    {
        var cookie = app.Cookie(user);
        await browser.AddCookiesAsync([new Microsoft.Playwright.Cookie { Name = cookie.Name, Value = cookie.Value, Url = root, HttpOnly = true }]);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static async Task<TResult> PostAsync<T, TResult>(HttpClient client, string path, T request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<TResult>>())!.Data!;
    }
}
