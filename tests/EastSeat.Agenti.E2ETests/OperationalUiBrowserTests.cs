using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace EastSeat.Agenti.E2ETests;

public sealed class OperationalUiBrowserTests(DatabaseFixture database, ITestOutputHelper output)
    : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Histories_PageFilterAndReflow_KeepPendingDecisionsSeparate()
    {
        await using var db = database.CreateContext();
        using var scenario = new BankingScenario(db);
        for (var index = 1; index <= 15; index++)
            db.CashSessions.Add(new CashSession
            {
                BranchId = scenario.Session.BranchId, SessionDate = scenario.Session.SessionDate.AddDays(-index),
                OpenedAt = BankingScenario.Now.AddDays(-index), Status = CashSessionStatus.Closed
            });
        var vault = await db.Vaults.SingleAsync(v => v.BranchId == scenario.Session.BranchId);
        for (var index = 0; index < 105; index++)
            db.VaultTransactions.Add(new VaultTransaction
            {
                VaultId = vault.Id, CreatedAt = BankingScenario.Now.AddMinutes(-index),
                CreatedByUserId = scenario.Admin.Id, Amount = 100m, BalanceAfter = 10_000m,
                Type = VaultTransactionType.ManualDeposit, Status = VaultTransactionStatus.Completed
            });
        var pending = new VaultTransaction
        {
            VaultId = vault.Id, CreatedAt = BankingScenario.Now.AddDays(-2),
            CreatedByUserId = scenario.User.Id, Amount = 200m, Notes = "Synthetic request for review",
            Type = VaultTransactionType.ManualDeposit, Status = VaultTransactionStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.VaultTransactions.Add(pending);
        for (var index = 0; index < 30; index++)
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(), RecipientUserId = scenario.Admin.Id,
                CreatedAt = BankingScenario.Now.AddMinutes(-index), IsRead = index % 2 != 0,
                Message = index % 2 == 0 ? $"Synthetic unread reminder {index}" : $"Synthetic read reminder {index}",
                TransactionId = index == 0 ? pending.PublicId : null
            });
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = scenario.User.Id,
            CreatedAt = BankingScenario.Now, Message = "Other recipient private reminder"
        });
        (await db.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        await db.SaveChangesAsync();

        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(0);
        var root = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true, Channel = Environment.GetEnvironmentVariable("AGENTI_TEST_BROWSER_CHANNEL")
        });
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var cookie = app.Cookie(scenario.Admin);
        await context.AddCookiesAsync([new Cookie { Name = cookie.Name, Value = cookie.Value, Url = root, HttpOnly = true }]);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(root + "/cashsessions");
        var sessions = page.GetByTestId("session-history");
        await Expect(sessions.Locator("tbody tr")).ToHaveCountAsync(10);
        await Expect(sessions.Locator("tbody td").First).ToHaveCSSAsync("display", "table-cell");
        await Expect(sessions.GetByRole(AriaRole.Button, new() { Name = "View session for 2026-11-02", Exact = true })).ToBeVisibleAsync();
        await sessions.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(sessions.Locator("tbody tr")).ToHaveCountAsync(6);
        await SelectAsync(page, "Session status", "Open");
        await Expect(sessions.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(sessions.Locator("tbody")).ToContainTextAsync("Nov 02, 2026");
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear filters", Exact = true }).ClickAsync();
        await Expect(sessions.Locator("tbody tr")).ToHaveCountAsync(10);
        await page.GetByLabel("From business date", new() { Exact = true }).FillAsync("2026-11-03");
        await page.GetByLabel("From business date", new() { Exact = true }).PressAsync("Tab");
        await page.GetByLabel("To business date", new() { Exact = true }).FillAsync("2026-11-01");
        await page.GetByLabel("To business date", new() { Exact = true }).PressAsync("Tab");
        await Expect(page.GetByText("The start date must be on or before the end date.", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear filters", Exact = true }).ClickAsync();
        await Expect(sessions.Locator("tbody tr")).ToHaveCountAsync(10);
        await CaptureLayoutsAsync(page, "sessions");
        await page.SetViewportSizeAsync(1280, 800);
        await sessions.GetByRole(AriaRole.Button, new() { Name = "View session for 2026-11-02", Exact = true }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page).ToHaveURLAsync(root + $"/cashsessions/{scenario.Session.Id}");
        await Expect(page.Locator("main h1")).ToHaveCountAsync(1);

        await page.GotoAsync(root + "/vault");
        var history = page.GetByTestId("vault-history");
        var queue = page.GetByTestId("vault-pending");
        await Expect(history.Locator("tbody tr")).ToHaveCountAsync(10);
        await Expect(history.Locator("tbody td").First).ToHaveCSSAsync("display", "table-cell");
        await Expect(queue.Locator("tbody tr")).ToHaveCountAsync(1);
        await history.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await SelectAsync(page, "Transaction status", "Completed");
        await Expect(history.Locator("tbody tr")).ToHaveCountAsync(10);
        await Expect(history.Locator("tbody")).Not.ToContainTextAsync("Pending");
        await Expect(queue.GetByText("Synthetic request for review", new() { Exact = true })).ToBeVisibleAsync();
        await queue.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog)).ToContainTextAsync("UGX 200");
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
        await CaptureLayoutsAsync(page, "vault");
        await page.SetViewportSizeAsync(1280, 800);

        await page.GotoAsync(root + "/notifications");
        var notifications = page.GetByTestId("notification-history");
        await Expect(notifications.Locator("tbody tr")).ToHaveCountAsync(10);
        await Expect(notifications.Locator("tbody td").First).ToHaveCSSAsync("display", "table-cell");
        await Expect(notifications.GetByText("Other recipient private reminder", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(notifications.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true })).ToHaveCountAsync(0);
        await Expect(notifications.GetByRole(AriaRole.Link, new() { Name = "Review vault request", Exact = true })).ToBeVisibleAsync();
        await SelectAsync(page, "Read status", "Unread");
        await Expect(notifications.Locator("tbody tr")).ToHaveCountAsync(10);
        await notifications.GetByRole(AriaRole.Button, new() { Name = "Mark as read", Exact = true }).First.ClickAsync();
        await Expect(page.GetByText("Read status updated.", new() { Exact = true })).ToBeVisibleAsync();
        await page.Locator(".mud-snackbar").GetByRole(AriaRole.Button).ClickAsync();
        var search = page.GetByLabel("Search notifications", new() { Exact = true });
        await search.FillAsync("no matching reminder");
        await Expect(page.GetByText("No notifications match these filters.", new() { Exact = false })).ToBeVisibleAsync();
        await search.FillAsync("Synthetic unread");
        await Expect(notifications.Locator("tbody tr")).ToHaveCountAsync(10);
        await notifications.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(notifications.Locator("tbody tr")).ToHaveCountAsync(4);
        await search.FillAsync("");
        await Expect(notifications.Locator("tbody tr")).ToHaveCountAsync(10);
        await CaptureLayoutsAsync(page, "notifications");
        await page.SetViewportSizeAsync(1280, 800);

        foreach (var route in new[] { "/", "/cashcount", "/customer-transactions", "/pending-transactions", "/bank-runs",
            "/cashcount-approvals", "/transaction-flags", "/agents", "/wallettypes", "/users", "/bank-statements", "/bank-accounts" })
        {
            await page.GotoAsync(root + route);
            await Expect(page.GetByRole(AriaRole.Main)).ToHaveCountAsync(1);
            await Expect(page.Locator("main h1")).ToHaveCountAsync(1);
            await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        }
        await Expect(page.Locator("nav a[href='cashcount-approvals'], .mud-navmenu a[href='cashcount-approvals']")).ToHaveCountAsync(1);
        await using var final = database.CreateContext();
        Assert.Equal(VaultTransactionStatus.Pending, (await final.VaultTransactions.FindAsync(pending.Id))!.Status);
        Assert.Equal(106, await final.VaultTransactions.CountAsync(t => t.VaultId == vault.Id));
        Assert.Equal(14, await final.Notifications.CountAsync(n => n.RecipientUserId == scenario.Admin.Id && !n.IsRead));
        Assert.Empty(errors);
        await VerifyPublicAuthenticationAsync(browser, root);
        output.WriteLine($"Chromium {browser.Version}; histories render ten rows; filters, paging, cancellation and read-only review navigation verified.");
    }

    private static async Task VerifyPublicAuthenticationAsync(IBrowser browser, string root)
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        foreach (var route in new[] { "Login", "Register", "ForgotPassword" })
        {
            await page.GotoAsync($"{root}/Account/{route}");
            await Expect(page.GetByRole(AriaRole.Main)).ToHaveCountAsync(1);
            await Expect(page.Locator("main h1")).ToHaveCountAsync(1);
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation", Exact = true })).ToHaveCountAsync(0);
            await Expect(page.GetByText("There are no external authentication services configured.", new() { Exact = false })).ToHaveCountAsync(0);
            await CaptureLayoutsAsync(page, $"auth-{route.ToLowerInvariant()}");
        }
        var email = page.GetByLabel("Email", new() { Exact = true });
        await Expect(email).ToHaveAttributeAsync("type", "email");
        Assert.True(await email.EvaluateAsync<double>("e => e.getBoundingClientRect().height") >= 44);
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset password", Exact = true }).ClickAsync();
        await Expect(page.GetByText("The Email field is required.", new() { Exact = true }).First).ToBeVisibleAsync();
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync("unknown-synthetic@example.invalid");
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset password", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(root + "/Account/ForgotPasswordConfirmation");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Check your email", Level = 1 })).ToBeVisibleAsync();
    }

    private static async Task SelectAsync(IPage page, string label, string option)
    {
        var select = page.Locator(".mud-input-control.mud-select").Filter(new() { Has = page.GetByText(label, new() { Exact = true }) });
        await select.Locator(".mud-input").ClickAsync();
        await page.Locator(".mud-popover-open").GetByText(option, new() { Exact = true }).ClickAsync();
    }

    private static async Task CaptureLayoutsAsync(IPage page, string name)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "ui-quality");
        Directory.CreateDirectory(folder);
        foreach (var viewport in new (string Name, int Width, int Height)[]
        {
            ("desktop", 1280, 800), ("tablet", 768, 1024), ("mobile", 390, 844), ("reflow", 320, 900)
        })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
            await page.WaitForFunctionAsync("document.getAnimations().every(a => a.playState !== 'running')");
            Assert.Equal(0, await page.EvaluateAsync<int>("Math.max(0, document.documentElement.scrollWidth - innerWidth)"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"{name}-{viewport.Name}.png"), FullPage = true });
        }
    }
}
