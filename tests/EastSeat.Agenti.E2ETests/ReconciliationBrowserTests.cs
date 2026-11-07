using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
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
        await using var browser = await LaunchBrowserAsync(playwright);
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
        await Expect(adminPage.GetByText("Register a bank account", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(adminPage.GetByText("Account configuration", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(adminPage.GetByText("Pause new enrollment", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(adminPage.GetByText("New-session enrollment: Enabled", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(adminPage.Locator("a").Filter(new() { HasText = "Manage account" }))
            .ToHaveAttributeAsync("href", $"/bank-accounts?accountId={scenario.Account.Id}");
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
        Assert.Equal(0m, (await final.Vaults.SingleAsync(v => v.BranchId == scenario.Agent.BranchId)).CurrentBalance);
        Assert.Equal(CashCountStatus.Approved, (await final.CashCounts.FindAsync(saved.CashCountId))!.Status);
        Assert.Equal(1, await final.SessionFloatSettlements.CountAsync(s => s.Stage == FloatSettlementStage.Closing));
        Assert.Equal(1, await final.BankReconciliationResults.CountAsync());
    }

    [Fact]
    public async Task BankAccounts_FirstRegistration_ManagesWorkflowWithoutMovingMoney()
    {
        await using var db = database.CreateContext();
        var setup = await SeedAccountSetupAsync(db);
        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(0);
        var root = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var administrator = await browser.NewContextAsync();
        await AddCookieAsync(administrator, app, setup.Admin, root);
        var page = await administrator.NewPageAsync();

        await page.GotoAsync(root + "/bank-statements");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true })).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await Expect(page.Locator("input[type=file]")).ToHaveCountAsync(0);
        await Expect(page.GetByLabel("Bank account", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByText("Register a bank account", new() { Exact = true })).ToHaveCountAsync(0);
        await page.Locator(".mud-alert").GetByText("Bank accounts", new() { Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(root + "/bank-accounts");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true })).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await page.GetByText("Register a bank account", new() { Exact = true }).ClickAsync();
        await SelectAsync(page, "Agent bank wallet", "Placement Teller - Placement bank (UGX)");
        await page.GetByLabel("Bank name", new() { Exact = true }).FillAsync("Placement bank");
        await page.GetByLabel("Account identifier shown on statement", new() { Exact = true }).FillAsync("PLACEMENT-ACCOUNT");
        await SelectAsync(page, "Confirmed balance basis", "Ledger");
        await page.GetByLabel("Statement time zone", new() { Exact = true }).FillAsync("Invalid/Zone");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save account binding", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Select a valid bank statement time zone.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Bank name", new() { Exact = true })).ToHaveValueAsync("Placement bank");
        await page.GetByLabel("Statement time zone", new() { Exact = true }).FillAsync("Africa/Kampala");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save account binding", Exact = true }).ClickAsync();
        await Expect(page.GetByText("New-session enrollment: Paused", new() { Exact = true })).ToBeVisibleAsync();

        await using var verify = database.CreateContext();
        var account = await verify.BankAccounts.SingleAsync(a => a.WalletId == setup.BankWallet.Id);
        Assert.False(account.WorkflowEnabled);
        await Expect(page).ToHaveURLAsync($"{root}/bank-accounts?accountId={account.Id}");
        var configuration = page.Locator(".mud-expand-panel").Filter(new() { HasText = "Account configuration" });
        await configuration.GetByText("Account configuration", new() { Exact = true }).ClickAsync();
        await configuration.GetByLabel("Bank name", new() { Exact = true }).FillAsync("Placement bank configured");
        await configuration.GetByLabel("Account identifier", new() { Exact = true }).FillAsync("PLACEMENT-UPDATED");
        await configuration.GetByRole(AriaRole.Button, new() { Name = "Save configuration", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Placement bank configured / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Enable new-session workflow", Exact = true }).ClickAsync();
        await Expect(page.GetByText("New-session enrollment: Enabled", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Pause new enrollment", Exact = true }).ClickAsync();
        await Expect(page.GetByText("New-session enrollment: Paused", new() { Exact = true })).ToBeVisibleAsync();

        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.GetByText("Open statements", new() { Exact = true })).ToBeInViewportAsync();
        await page.GetByText("Open statements", new() { Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync($"{root}/bank-statements?accountId={account.Id}");
        await Expect(page.Locator("input[type=file]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByText("Placement bank configured / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("New-session enrollment: Paused", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Manage account", new() { Exact = true })).ToBeInViewportAsync();
        await Expect(page.GetByText("Account configuration", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Enable new-session workflow", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByText("Register a bank account", new() { Exact = true })).ToHaveCountAsync(0);

        await verify.Entry(account).ReloadAsync();
        Assert.Equal("PLACEMENT-UPDATED", account.AccountIdentifier);
        Assert.False(account.WorkflowEnabled);
        Assert.Equal(3, account.Version);
        Assert.Equal(100_000m, (await verify.Wallets.FindAsync(setup.CashWallet.Id))!.Balance);
        Assert.Equal(1_000_000m, (await verify.Wallets.FindAsync(setup.BankWallet.Id))!.Balance);
        Assert.Equal(0m, (await verify.Vaults.SingleAsync(v => v.BranchId == setup.Admin.BranchId)).CurrentBalance);
    }

    [Fact]
    public async Task BankAccounts_LinksAndSelection_PreserveAccountContextAndClearUploadDrafts()
    {
        await using var db = database.CreateContext();
        var setup = await SeedAccountSetupAsync(db);
        var first = new BankAccount
        {
            BranchId = setup.Admin.BranchId!.Value, WalletId = setup.BankWallet.Id, BankName = "Alpha bank",
            AccountIdentifier = "ALPHA-ACCOUNT", BalanceBasis = BankBalanceBasis.Ledger, CreatedAt = BankingScenario.Now
        };
        var secondTeller = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = Guid.NewGuid().ToString("N"),
            FirstName = "Second", LastName = "Teller", Role = UserRole.Agent, BranchId = setup.Admin.BranchId, IsActive = true
        };
        db.Users.Add(secondTeller);
        await db.SaveChangesAsync();
        var secondAgent = new Agent
        {
            UserId = secondTeller.Id, BranchId = setup.Admin.BranchId.Value, Code = "LINK",
            IsActive = true, CreatedAt = BankingScenario.Now
        };
        db.Agents.Add(secondAgent);
        await db.SaveChangesAsync();
        secondTeller.AgentId = secondAgent.Id;
        var secondWallet = new Wallet
        {
            AgentId = secondAgent.Id, WalletTypeId = 4, Name = "Second bank wallet",
            Currency = "UGX", IsActive = true, CreatedAt = BankingScenario.Now
        };
        db.Wallets.Add(secondWallet);
        await db.SaveChangesAsync();
        var second = new BankAccount
        {
            BranchId = setup.Admin.BranchId.Value, WalletId = secondWallet.Id, BankName = "Zulu bank",
            AccountIdentifier = "ZULU-ACCOUNT", BalanceBasis = BankBalanceBasis.Ledger, CreatedAt = BankingScenario.Now
        };
        db.BankAccounts.AddRange(first, second);
        await db.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(0);
        var root = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var administrator = await browser.NewContextAsync();
        await AddCookieAsync(administrator, app, setup.Admin, root);
        var page = await administrator.NewPageAsync();

        await page.GotoAsync($"{root}/bank-statements?accountId={second.Id}");
        await Expect(page.Locator("input[type=file]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByText("Zulu bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByText("Manage account", new() { Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync($"{root}/bank-accounts?accountId={second.Id}");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true })).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByText("Zulu bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await SelectAsync(page, "Bank account", "Alpha bank - ALPHA-ACCOUNT");
        await Expect(page).ToHaveURLAsync($"{root}/bank-accounts?accountId={first.Id}");
        await Expect(page.GetByText("Alpha bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true }).ClickAsync();
        await Expect(page.Locator("a").Filter(new() { HasText = "Open statements" }))
            .ToHaveAttributeAsync("href", $"/bank-statements?accountId={first.Id}");
        await page.GetByText("Open statements", new() { Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync($"{root}/bank-statements?accountId={first.Id}");
        await Expect(page.Locator("input[type=file]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "unsubmitted.csv", MimeType = "text/csv",
            Buffer = System.Text.Encoding.UTF8.GetBytes("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n")
        });
        await Expect(page.GetByText("unsubmitted.csv", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByLabel("Initial opening balance (first empty period only)", new() { Exact = true }).FillAsync("1000000");
        await SelectAsync(page, "Bank account", "Zulu bank - ZULU-ACCOUNT");
        await Expect(page).ToHaveURLAsync($"{root}/bank-statements?accountId={second.Id}");
        await Expect(page.GetByText("Zulu bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("unsubmitted.csv", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByLabel("Initial opening balance (first empty period only)", new() { Exact = true })).ToHaveValueAsync("");
        Assert.Equal(0, await page.Locator("input[type=file]").EvaluateAsync<int>("input => input.files.length"));
        await page.ReloadAsync();
        await Expect(page.Locator("a").Filter(new() { HasText = "Manage account" }))
            .ToHaveAttributeAsync("href", $"/bank-accounts?accountId={second.Id}");
    }

    [Fact]
    public async Task BankAccounts_ManagementAndStatementLinks_RespectRoleAndBranchBoundaries()
    {
        await using var db = database.CreateContext();
        var setup = await SeedAccountSetupAsync(db);
        var other = await SeedAccountSetupAsync(db);
        var supervisor = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = "placement-supervisor",
            FirstName = "Placement", LastName = "Supervisor", BranchId = setup.Admin.BranchId,
            Role = UserRole.Supervisor, IsActive = true
        };
        var account = new BankAccount
        {
            BranchId = setup.Admin.BranchId!.Value, WalletId = setup.BankWallet.Id, BankName = "Own branch bank",
            AccountIdentifier = "OWN-ACCOUNT", BalanceBasis = BankBalanceBasis.Ledger, CreatedAt = BankingScenario.Now
        };
        var foreignAccount = new BankAccount
        {
            BranchId = other.Admin.BranchId!.Value, WalletId = other.BankWallet.Id, BankName = "Other branch secret bank",
            AccountIdentifier = "OTHER-ACCOUNT", BalanceBasis = BankBalanceBasis.Ledger, CreatedAt = BankingScenario.Now
        };
        db.Users.Add(supervisor);
        db.BankAccounts.AddRange(account, foreignAccount);
        await db.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(0);
        var root = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        using var client = app.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri(root) });
        using (var anonymous = await client.GetAsync("/bank-accounts"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        foreach (var user in new[] { supervisor, setup.Teller })
        {
            var cookie = app.Cookie(user);
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");
            using var denied = await client.GetAsync("/bank-accounts");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var supervisorBrowser = await browser.NewContextAsync();
        await AddCookieAsync(supervisorBrowser, app, supervisor, root);
        var supervisorPage = await supervisorBrowser.NewPageAsync();
        await supervisorPage.GotoAsync(root + "/bank-reconciliation");
        await Expect(supervisorPage.GetByRole(AriaRole.Heading, new() { Name = "Bank reconciliation", Exact = true })).ToBeVisibleAsync();
        await Expect(supervisorPage.GetByText("Reconciliation Reviews", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(supervisorPage.GetByText("Bank accounts", new() { Exact = true })).ToHaveCountAsync(0);

        await using var administrator = await browser.NewContextAsync();
        await AddCookieAsync(administrator, app, setup.Admin, root);
        var page = await administrator.NewPageAsync();
        foreach (var route in new[] { "bank-accounts", "bank-statements" })
        {
            await page.GotoAsync($"{root}/{route}?accountId={account.Id}");
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true })).ToBeEnabledAsync(new() { Timeout = 30_000 });
            await Expect(page.GetByText("Own branch bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
            foreach (var invalidId in new[] { foreignAccount.Id, long.MaxValue })
            {
                await page.GotoAsync($"{root}/{route}?accountId={invalidId}");
                await Expect(page.GetByText("You do not have access to this banking operation.", new() { Exact = true })).ToBeVisibleAsync();
                await Expect(page.GetByText("Other branch secret bank / UGX / Ledger", new() { Exact = true })).ToHaveCountAsync(0);
                await Expect(page.GetByText("Own branch bank / UGX / Ledger", new() { Exact = true })).ToHaveCountAsync(0);
                await Expect(page.GetByText("Open statements", new() { Exact = true })).ToHaveCountAsync(0);
                await Expect(page.Locator("input[type=file]")).ToHaveCountAsync(0);
            }
        }
    }

    private static Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright) => playwright.Chromium.LaunchAsync(new()
    {
        Headless = true,
        Channel = Environment.GetEnvironmentVariable("AGENTI_TEST_BROWSER_CHANNEL")
    });

    private static async Task SelectAsync(IPage page, string label, string option)
    {
        var select = page.Locator(".mud-input-control.mud-select").Filter(new() { Has = page.GetByText(label, new() { Exact = true }) });
        await select.Locator(".mud-input").ClickAsync();
        await page.Locator(".mud-popover-open").GetByText(option, new() { Exact = true }).ClickAsync();
    }

    private static async Task<(ApplicationUser Admin, ApplicationUser Teller, Wallet CashWallet, Wallet BankWallet)> SeedAccountSetupAsync(ApplicationDbContext db)
    {
        var branch = new Branch { Name = "Placement branch " + Guid.NewGuid().ToString("N"), CreatedAt = BankingScenario.Now };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = Guid.NewGuid().ToString("N"),
            FirstName = "Placement", LastName = "Admin", Role = UserRole.Admin, BranchId = branch.Id, IsActive = true
        };
        var teller = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = Guid.NewGuid().ToString("N"),
            FirstName = "Placement", LastName = "Teller", Role = UserRole.Agent, BranchId = branch.Id, IsActive = true
        };
        db.Users.AddRange(admin, teller);
        await db.SaveChangesAsync();
        var agent = new Agent
        {
            UserId = teller.Id, BranchId = branch.Id, Code = $"P{branch.Id:000}",
            IsActive = true, CreatedAt = BankingScenario.Now
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        teller.AgentId = agent.Id;
        var cashWallet = new Wallet
        {
            AgentId = agent.Id, WalletTypeId = 1, Name = "Placement cash", Currency = "UGX",
            Balance = 100_000m, IsActive = true, CreatedAt = BankingScenario.Now
        };
        var bankWallet = new Wallet
        {
            AgentId = agent.Id, WalletTypeId = 4, Name = "Placement bank", Currency = "UGX",
            Balance = 1_000_000m, IsActive = true, CreatedAt = BankingScenario.Now
        };
        db.Wallets.AddRange(cashWallet, bankWallet);
        (await db.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        await db.SaveChangesAsync();
        return (admin, teller, cashWallet, bankWallet);
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
