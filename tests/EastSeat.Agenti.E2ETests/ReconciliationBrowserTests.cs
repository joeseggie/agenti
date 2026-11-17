using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Features.CashCounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EastSeat.Agenti.E2ETests;

public sealed class ReconciliationBrowserTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Closing_ReconcilesAllBanksWithRealCsv_AndPreservesNextDayBalances()
    {
        await using var db = database.CreateContext();
        using var scenario = new BankingScenario(db);
        var secondBank = scenario.AddWallet("ABSA", WalletTypeEnum.Bank, 200_000m);
        var secondAccount = scenario.BindBankAccount(secondBank);
        var mobile = scenario.AddWallet("Airtel", WalletTypeEnum.MobileMoney, 300_000m);
        scenario.AddApprovedOpening(withSettlement: true, additionalWallets: [secondBank, mobile]);
        scenario.Account.WorkflowEnabled = true;
        scenario.Account.ImportProfileVerified = true;
        scenario.Account.ImportProfileKey = UniversalBankCsvStatementProfile.ProfileKey;
        var supervisor = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = "closing-supervisor",
            FirstName = "Synthetic", LastName = "Supervisor", Role = UserRole.Supervisor,
            BranchId = scenario.Admin.BranchId, IsActive = true
        };
        db.Users.Add(supervisor);
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
        await SelectAsync(page, "Bank wallet", $"{scenario.Account.BankName} - {scenario.BankWallet.Name} ({scenario.Account.Currency})");
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
        await page.GetByRole(AriaRole.Link, new() { Name = "Cash Count", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue Closing Draft", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save Draft", Exact = true })).ToBeVisibleAsync();
        foreach (var wallet in closing.WalletEntries.Where(w => !w.SupportsDenominations))
        {
            var row = page.Locator("tbody tr").Filter(new() { Has = page.GetByText(wallet.WalletName, new() { Exact = true }) });
            await row.Locator("input").First.FillAsync(wallet.CountedAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit for Approval", Exact = true }).ClickAsync();
        await ExpectVisibleAsync(page, page.GetByText("Submitted closing observations are locked.", new() { Exact = false }));
        var frozen = await GetAsync<CashCountFormModel>(client, $"/api/banking/counts/{saved.CashCountId}");
        Assert.True(frozen.IsReadOnly);
        using (var edit = await client.PostAsJsonAsync("/api/banking/counts", frozen))
            Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);

        app.Clock.Now = BankingScenario.Now.AddDays(1).AddHours(1);
        var opening = await GetAsync<CashCountFormModel>(client, "/api/banking/counts/initialize?isOpening=true");
        foreach (var wallet in opening.WalletEntries)
        {
            wallet.CountedAmount = wallet.WalletId == scenario.CashWallet.Id ? 150_000m :
                wallet.WalletId == scenario.BankWallet.Id ? 950_000m :
                wallet.WalletId == secondBank.Id ? 200_000m : 300_000m;
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
        var adminPage = await administrator.NewPageAsync();
        await LoginAdminAsync(adminPage, app, scenario.Admin.Id, root);
        var reviewUrl = $"{root}/closing-approvals/{saved.CashCountId}";
        await adminPage.GotoAsync(reviewUrl);
        await Expect(adminPage.GetByRole(AriaRole.Button, new() { Name = "Reconcile closing", Exact = true })).ToBeEnabledAsync();
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Reconcile closing", Exact = true }).ClickAsync();
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("^Reconciliation blocked"))).ToBeVisibleAsync();
        await Expect(adminPage.Locator("input[type=file]")).ToHaveCountAsync(0);
        var day = scenario.Session.SessionDate;
        await UploadCsvAsync(adminPage, root, scenario.Account.Id, day, BankingScenario.StatementCsv(day.AddDays(-1), 1_000_000m));
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("must match the selected statement date"))).ToBeVisibleAsync();
        await UploadCsvAsync(adminPage, root, scenario.Account.Id, day, BankingScenario.StatementCsv(day, 1_000_000m, -50_000m));
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("^Statement accepted"))).ToBeVisibleAsync();
        await adminPage.GotoAsync(reviewUrl);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Reconcile closing", Exact = true }).ClickAsync();
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("ABSA: statement.*missing"))).ToBeVisibleAsync();
        await UploadCsvAsync(adminPage, root, secondAccount.Id, day, BankingScenario.StatementCsv(day, 200_000m));
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("^Statement accepted"))).ToBeVisibleAsync();
        await adminPage.GotoAsync(reviewUrl);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Reconcile closing", Exact = true }).ClickAsync();
        await ExpectVisibleAsync(adminPage, adminPage.GetByText(new System.Text.RegularExpressions.Regex("Reconciliation: Success")));
        await Expect(adminPage.GetByText("Closing status: PendingApproval", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(adminPage.GetByRole(AriaRole.Button, new() { Name = "Reconcile closing", Exact = true })).ToHaveCountAsync(0);
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        await adminPage.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "TestResults", "closing-success.png"), FullPage = true });
        await using var reviewer = await browser.NewContextAsync();
        await AddCookieAsync(reviewer, app, supervisor, root);
        var supervisorPage = await reviewer.NewPageAsync();
        await supervisorPage.GotoAsync(reviewUrl);
        await supervisorPage.GetByRole(AriaRole.Button, new() { Name = "Approve closing", Exact = true }).ClickAsync();
        await Expect(supervisorPage.GetByText("Closing status: Approved", new() { Exact = true })).ToBeVisibleAsync();

        await using var final = database.CreateContext();
        Assert.Equal(150_000m, (await final.Wallets.FindAsync(scenario.CashWallet.Id))!.Balance);
        Assert.Equal(950_000m, (await final.Wallets.FindAsync(scenario.BankWallet.Id))!.Balance);
        Assert.Equal(200_000m, (await final.Wallets.FindAsync(secondBank.Id))!.Balance);
        Assert.Equal(300_000m, (await final.Wallets.FindAsync(mobile.Id))!.Balance);
        Assert.Equal(0m, (await final.Vaults.SingleAsync(v => v.BranchId == scenario.Agent.BranchId)).CurrentBalance);
        Assert.Equal(CashCountStatus.Approved, (await final.CashCounts.FindAsync(saved.CashCountId))!.Status);
        Assert.Equal(1, await final.SessionFloatSettlements.CountAsync(s => s.Stage == FloatSettlementStage.Closing));
        Assert.Equal(2, await final.ClosingWalletResults.CountAsync());
        var notices = await final.Notifications.Where(n => n.Type == NotificationType.CountPendingApproval &&
            n.LinkUrl == $"/closing-approvals/{saved.CashCountId}").ToListAsync();
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, n => n.RecipientUserId == supervisor.Id);
        Assert.Contains(notices, n => n.RecipientUserId == scenario.Admin.Id);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));
        var nextClosing = await GetAsync<CashCountFormModel>(client, "/api/banking/counts/initialize?isOpening=false");
        foreach (var wallet in nextClosing.WalletEntries)
        {
            wallet.CountedAmount = wallet.ExpectedBalance;
            wallet.Denominations = null;
            wallet.ObservedAt = app.Clock.Now;
        }
        var nextSaved = await PostAsync<CashCountFormModel, CashCountSaveResult>(client, "/api/banking/counts", nextClosing);
        using (var submitted = await client.PostAsync($"/api/banking/counts/{nextSaved.CashCountId}/submit", null))
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var nextDay = day.AddDays(1);
        await UploadCsvAsync(adminPage, root, scenario.Account.Id, nextDay, BankingScenario.StatementCsv(nextDay, 950_000m, 10m));
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("^Statement accepted"))).ToBeVisibleAsync();
        await UploadCsvAsync(adminPage, root, secondAccount.Id, nextDay, BankingScenario.StatementCsv(nextDay, 200_000m));
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("^Statement accepted"))).ToBeVisibleAsync();
        var failedUrl = $"{root}/closing-approvals/{nextSaved.CashCountId}";
        await adminPage.GotoAsync(failedUrl);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Reconcile closing", Exact = true }).ClickAsync();
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("Reconciliation: Failed"))).ToBeVisibleAsync();
        await supervisorPage.GotoAsync(failedUrl);
        await Expect(supervisorPage.GetByRole(AriaRole.Button, new() { Name = "Approve closing", Exact = true })).ToHaveCountAsync(0);
        await Expect(supervisorPage.GetByRole(AriaRole.Button, new() { Name = "Approve with reconciliation exception", Exact = true })).ToHaveCountAsync(0);
        await adminPage.GetByLabel("Exception approval reason", new() { Exact = true }).FillAsync("short");
        await adminPage.GetByLabel("I acknowledge all failed wallets and differences; the reconciliation remains Failed", new() { Exact = true }).CheckAsync();
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Approve with reconciliation exception", Exact = true }).ClickAsync();
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("between 10 and 2,000 characters"))).ToBeVisibleAsync();
        await adminPage.EvaluateAsync("url => Blazor.navigateTo(url)", reviewUrl);
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("Reconciliation: Success"))).ToBeVisibleAsync();
        await adminPage.EvaluateAsync("url => Blazor.navigateTo(url)", failedUrl);
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("Reconciliation: Failed"))).ToBeVisibleAsync();
        await Expect(adminPage.GetByLabel("Exception approval reason", new() { Exact = true })).ToHaveValueAsync("");
        await Expect(adminPage.GetByLabel("I acknowledge all failed wallets and differences; the reconciliation remains Failed", new() { Exact = true })).Not.ToBeCheckedAsync();
        await adminPage.GetByLabel("I acknowledge all failed wallets and differences; the reconciliation remains Failed", new() { Exact = true }).CheckAsync();
        await adminPage.GetByLabel("Exception approval reason", new() { Exact = true }).FillAsync("Bank difference investigated and separately documented.");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Approve with reconciliation exception", Exact = true }).ClickAsync();
        await Expect(adminPage.GetByText("Closing status: Approved", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("Reconciliation: Failed"))).ToBeVisibleAsync();
        await adminPage.ReloadAsync();
        await Expect(adminPage.GetByText(new System.Text.RegularExpressions.Regex("Approved with reconciliation exception"))).ToBeVisibleAsync();
        await adminPage.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "TestResults", "closing-exception.png"), FullPage = true });
        await using var verifyFailed = database.CreateContext();
        var failedRun = await verifyFailed.ClosingReconciliationRuns.Include(r => r.Wallets).SingleAsync(r => r.ClosingCountId == nextSaved.CashCountId);
        Assert.False(failedRun.Succeeded);
        Assert.Equal(1, failedRun.Wallets.Count(w => !w.Succeeded));
        Assert.True((await verifyFailed.ClosingApprovalDecisions.SingleAsync(a => a.ClosingCountId == nextSaved.CashCountId)).IsException);
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

        await page.GotoAsync($"{root}/bank-statements?accountId={long.MaxValue}");
        await Expect(page.GetByText("You do not have access to this banking operation.", new() { Exact = true })).ToBeVisibleAsync();
        await NavigateBankPageAsync(page, "bank-statements");
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
        await page.GetByText("Open statements", new() { Exact = true }).ScrollIntoViewIfNeededAsync();
        await Expect(page.GetByText("Open statements", new() { Exact = true })).ToBeInViewportAsync();
        await page.GetByText("Open statements", new() { Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync($"{root}/bank-statements?accountId={account.Id}");
        await Expect(page.Locator("input[type=file]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByText("Placement bank configured / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
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
        var lookupFailures = new AccountLookupFailures();
        await using var app = CreateLookupControlledApp(lookupFailures);
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
        lookupFailures.FailNext();
        await SelectAsync(page, "Bank account", "Alpha bank - ALPHA-ACCOUNT");
        await Expect(page).ToHaveURLAsync($"{root}/bank-accounts?accountId={first.Id}");
        await Expect(page.GetByText("Simulated account lookup failure.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Open statements", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByText("Account configuration", new() { Exact = true })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Alpha bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true }).ClickAsync();
        await Expect(page.Locator("a").Filter(new() { HasText = "Open statements" }))
            .ToHaveAttributeAsync("href", $"/bank-statements?accountId={first.Id}");
        await page.GetByText("Open statements", new() { Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync($"{root}/bank-statements?accountId={first.Id}");
        await Expect(page.Locator("input[type=file]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await FillDateAsync(page, "Statement date", "2026-10-28");
        await Expect(page).ToHaveURLAsync($"{root}/bank-statements?accountId={first.Id}&date=2026-10-28");
        await Expect(page.Locator("input[type=file]")).ToBeEnabledAsync();
        await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "unsubmitted.csv", MimeType = "text/csv",
            Buffer = System.Text.Encoding.UTF8.GetBytes("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n")
        });
        await ExpectVisibleAsync(page, page.GetByText("unsubmitted.csv", new() { Exact = true }));
        lookupFailures.FailNext();
        await SelectAsync(page, "Bank account", "Second bank wallet - Zulu bank (ZULU-ACCOUNT)");
        await Expect(page).ToHaveURLAsync($"{root}/bank-statements?accountId={second.Id}&date=2026-10-28");
        await Expect(page.GetByText("Simulated account lookup failure.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("input[type=file]")).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Zulu bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("unsubmitted.csv", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByLabel("Initial opening balance (first empty period only)", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByLabel("Statement date", new() { Exact = true })).ToHaveValueAsync("2026-10-28");
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
        await supervisorPage.GotoAsync(root + "/cashcount-approvals");
        await Expect(supervisorPage.GetByRole(AriaRole.Heading, new() { Name = "Cash Count Approvals", Exact = true })).ToBeVisibleAsync();
        await Expect(supervisorPage.GetByText("Closing approvals", new() { Exact = true })).ToBeVisibleAsync();
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
            await NavigateBankPageAsync(page, route);
            await Expect(page.GetByText("Own branch bank / UGX / Ledger", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByText("You do not have access to this banking operation.", new() { Exact = true })).ToHaveCountAsync(0);
        }
    }

    private BankingApplicationFactory CreateLookupControlledApp(AccountLookupFailures failures) =>
        new(database.ConnectionString, services =>
            services.AddScoped<IBankAccountService>(provider => new LookupFailingAccountService(
                new BankAccountService(provider.GetRequiredService<ApplicationDbContext>(),
                    provider.GetRequiredService<IBankingAccessService>(), provider.GetRequiredService<IBusinessClock>(),
                    provider.GetRequiredService<StatementProfileCatalog>()), failures)));

    private sealed class AccountLookupFailures
    {
        private int _failNext;
        public void FailNext() => Interlocked.Exchange(ref _failNext, 1);
        public bool TakeFailure() => Interlocked.Exchange(ref _failNext, 0) == 1;
    }

    private sealed class LookupFailingAccountService(IBankAccountService inner, AccountLookupFailures failures) : IBankAccountService
    {
        public async Task<List<BankAccountDto>> GetAccountsAsync(string userId, CancellationToken cancellationToken = default)
        {
            if (failures.TakeFailure()) throw new BankingRuleException("Simulated account lookup failure.");
            return await inner.GetAccountsAsync(userId, cancellationToken);
        }

        public Task<BankAccountSetupDto> GetSetupAsync(string userId, CancellationToken cancellationToken = default) =>
            inner.GetSetupAsync(userId, cancellationToken);
        public Task<BankAccountDto> CreateAsync(string userId, BankAccountForm form, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(userId, form, cancellationToken);
        public Task<BankAccountDto> ConfigureAsync(string userId, long accountId, BankAccountForm form, int expectedVersion, CancellationToken cancellationToken = default) =>
            inner.ConfigureAsync(userId, accountId, form, expectedVersion, cancellationToken);
        public Task<BankAccountDto> SetWorkflowAsync(string userId, long accountId, bool enabled, int expectedVersion, CancellationToken cancellationToken = default) =>
            inner.SetWorkflowAsync(userId, accountId, enabled, expectedVersion, cancellationToken);
    }

    private static async Task ExpectVisibleAsync(IPage page, ILocator locator)
    {
        try
        {
            await Expect(locator).ToBeVisibleAsync();
        }
        catch (PlaywrightException exception)
        {
            throw new Xunit.Sdk.XunitException(exception.Message + Environment.NewLine + await page.Locator("body").InnerTextAsync());
        }
    }

    private static async Task LoginAdminAsync(IPage page, BankingApplicationFactory app, string userId, string root)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = (await users.FindByIdAsync(userId))!;
        user.UserName = "synthetic-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        user.Email = user.UserName;
        user.EmailConfirmed = true;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
        var password = "Synthetic!" + Guid.NewGuid().ToString("N") + "1a";
        Assert.True((await users.AddPasswordAsync(user, password)).Succeeded);
        if (!await roles.RoleExistsAsync("Admin"))
            Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        await page.GotoAsync(root + "/Account/Login?ReturnUrl=%2Fcashcount-approvals");
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync(user.Email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(root + "/cashcount-approvals");
    }

    private static async Task UploadCsvAsync(IPage page, string root, long accountId, DateOnly date, string csv)
    {
        await page.GotoAsync($"{root}/bank-statements?accountId={accountId}&date={date:yyyy-MM-dd}");
        await Expect(page.Locator("input[type=file]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "synthetic-statement.csv", MimeType = "text/csv", Buffer = Encoding.UTF8.GetBytes(csv)
        });
        await page.GetByLabel("I confirm this file belongs to the selected bank account and covers the complete selected day", new() { Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload statement", Exact = true }).ClickAsync();
    }

    private static async Task NavigateBankPageAsync(IPage page, string route)
    {
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true })).ToBeEnabledAsync(new() { Timeout = 30_000 });
        var toggle = page.GetByRole(AriaRole.Button, new() { Name = "Toggle Bank Reconciliation", Exact = true });
        var link = page.Locator($"a[href='{route}']");
        if (await toggle.GetAttributeAsync("aria-expanded") != "true")
            await toggle.ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await link.ClickAsync();
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
