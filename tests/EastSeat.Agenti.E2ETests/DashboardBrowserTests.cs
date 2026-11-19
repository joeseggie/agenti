using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Dashboard;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace EastSeat.Agenti.E2ETests;

public sealed class DashboardBrowserTests(DatabaseFixture database, ITestOutputHelper output)
    : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Dashboard_PreservesDataAndNavigation_AcrossResponsiveLayouts()
    {
        await using var db = database.CreateContext();
        using var scenario = new BankingScenario(db);
        for (var index = 1; index <= 15; index++)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"), UserName = $"synthetic-colleague-{index}",
                FirstName = "Synthetic", LastName = $"Colleague {index:00}",
                Role = UserRole.Agent, BranchId = scenario.Agent.BranchId, IsActive = true
            };
            var agent = new Agent
            {
                User = user, BranchId = scenario.Agent.BranchId, Code = $"U{index:000}",
                IsActive = true, CreatedAt = BankingScenario.Now
            };
            agent.Wallets.Add(new Wallet
            {
                WalletTypeId = 1, Name = $"Synthetic wallet {index:00}", Currency = "UGX",
                Balance = 10_000m, IsActive = true, CreatedAt = BankingScenario.Now
            });
            db.Agents.Add(agent);
        }
        (await db.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        await db.SaveChangesAsync();

        var requests = new DashboardRequests();
        await using var app = new BankingApplicationFactory(database.ConnectionString, services =>
            services.AddScoped<IDashboardService>(provider => new CountingDashboardService(
                new DashboardService(provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
                    provider.GetRequiredService<IBusinessClock>()), requests)));
        app.UseKestrel(0);
        var root = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Channel = Environment.GetEnvironmentVariable("AGENTI_TEST_BROWSER_CHANNEL")
        });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ColorScheme = ColorScheme.Light
        });
        var cookie = app.Cookie(scenario.User);
        await context.AddCookiesAsync([new Cookie { Name = cookie.Name, Value = cookie.Value, Url = root, HttpOnly = true }]);
        var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(root + "/");
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true });
        await Expect(refresh).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(10);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Level = 1 })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Notifications", Exact = true })).ToBeVisibleAsync();
        var agentMetric = page.Locator(".metric-card").Filter(new()
        {
            Has = page.GetByText("Agent wallet balance", new() { Exact = true })
        });
        await Expect(agentMetric).ToContainTextAsync("UGX 1,250,000");
        Assert.True(await ContrastAsync(page.Locator(".session-chip")) >= 4.5);
        Assert.Equal(1, requests.Calls);
        output.WriteLine($"Initial dashboard service calls: {requests.Calls}");
        output.WriteLine("Initial rendered agent rows: " + await page.Locator("tbody tr").CountAsync());
        output.WriteLine($"Browser: {browser.Version}");

        var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", "ui-quality");
        Directory.CreateDirectory(artifacts);
        foreach (var viewport in new (string Name, int Width, int Height)[]
        {
            ("desktop", 1440, 900), ("tablet", 768, 1024), ("mobile", 390, 844), ("reflow", 320, 900)
        })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page);
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(artifacts, $"dashboard-after-{viewport.Name}.png"), FullPage = true
            });
            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - window.innerWidth");
            Assert.Equal(0, overflow);
            output.WriteLine($"{viewport.Name} overflow: {overflow}");
            if (viewport.Name == "mobile")
            {
                var navigation = page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation", Exact = true });
                await navigation.ClickAsync();
                await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Cash Count", Exact = true })).ToBeVisibleAsync();
                await page.Locator(".mud-drawer-overlay").ClickAsync(new()
                {
                    Position = new() { X = viewport.Width - 12, Y = 100 }
                });
            }
        }
        await page.SetViewportSizeAsync(1440, 900);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        var layout = page.Locator(".mud-layout");
        await Expect(layout).ToHaveCSSAsync("background-color", "rgb(17, 26, 39)");
        await SettleLayoutAsync(page);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "dashboard-after-dark.png"), FullPage = true
        });
        var darkContrast = await ContrastAsync(page.Locator(".session-chip"));
        output.WriteLine($"Dark session-status contrast: {darkContrast:F2}:1");
        output.WriteLine("Dark session-status colors: " + await page.Locator(".session-chip").EvaluateAsync<string>(
            "element => getComputedStyle(element).color + ' on ' + getComputedStyle(element).backgroundColor"));
        Assert.True(darkContrast >= 4.5);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Expect(layout).ToHaveCSSAsync("background-color", "rgb(244, 247, 251)");

        await SelectThemeAsync(page, "Use light theme");
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await SettleLayoutAsync(page);
        await Expect(layout).ToHaveCSSAsync("background-color", "rgb(244, 247, 251)");
        await SelectThemeAsync(page, "Use dark theme");
        await Expect(layout).ToHaveCSSAsync("background-color", "rgb(17, 26, 39)");
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await SettleLayoutAsync(page);
        await Expect(layout).ToHaveCSSAsync("background-color", "rgb(17, 26, 39)");
        await SelectThemeAsync(page, "Use system theme");
        await Expect(layout).ToHaveCSSAsync("background-color", "rgb(244, 247, 251)");

        var search = page.GetByLabel("Search agents", new() { Exact = true });
        await search.FillAsync("TEST");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("tbody")).ToContainTextAsync("Test Agent");
        await search.FillAsync("no matching colleague");
        await Expect(page.GetByText("No agents match your search.", new() { Exact = false })).ToBeVisibleAsync();
        await search.FillAsync("");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(10);
        await page.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(6);
        await search.FillAsync("U001");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("tbody")).ToContainTextAsync("Synthetic Colleague 01");
        await search.FillAsync("");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(10);
        Assert.Equal(1, requests.Calls);

        requests.FailNext();
        await refresh.ClickAsync();
        await Expect(page.GetByText("Previously loaded balances are still shown.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(agentMetric).ToContainTextAsync("UGX 1,250,000");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(10);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "dashboard-after-refresh-error.png"), FullPage = true
        });
        Assert.Equal(2, requests.Calls);
        await refresh.ClickAsync();
        await Expect(page.GetByText("Previously loaded balances are still shown.", new() { Exact = false })).ToHaveCountAsync(0);
        await Expect(refresh).ToBeEnabledAsync();
        await Expect(agentMetric).ToContainTextAsync("UGX 1,250,000");
        Assert.Equal(3, requests.Calls);

        var skipLink = page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content", Exact = true });
        await skipLink.FocusAsync();
        await Expect(skipLink).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#main-content")).ToBeFocusedAsync();
        await Expect(page.GetByRole(AriaRole.Main)).ToHaveCountAsync(1);
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        Assert.Equal(3, requests.Calls);
        Assert.Empty(pageErrors);
    }

    private static async Task SelectThemeAsync(IPage page, string label)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "User menu", Exact = true }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(".mud-popover-open").GetByText(label, new() { Exact = true })).ToBeVisibleAsync();
        await page.Locator(".mud-popover-open").GetByText(label, new() { Exact = true }).ClickAsync();
    }

    private static async Task SettleLayoutAsync(IPage page)
    {
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        await page.WaitForFunctionAsync("document.getAnimations().every(animation => animation.playState !== 'running')");
    }

    private static Task<double> ContrastAsync(ILocator locator) => locator.EvaluateAsync<double>("""
        element => {
            const style = getComputedStyle(element);
            const luminance = color => {
                const rgb = color.match(/[\d.]+/g).slice(0, 3).map(value => {
                    const channel = Number(value) / 255;
                    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
                });
                return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
            };
            const text = luminance(style.color);
            const background = luminance(style.backgroundColor);
            return (Math.max(text, background) + 0.05) / (Math.min(text, background) + 0.05);
        }
        """);

    private sealed class DashboardRequests
    {
        private int _calls;
        private int _failNext;
        public int Calls => Volatile.Read(ref _calls);
        public void FailNext() => Interlocked.Exchange(ref _failNext, 1);

        public void Record()
        {
            Interlocked.Increment(ref _calls);
            if (Interlocked.Exchange(ref _failNext, 0) == 1)
                throw new InvalidOperationException("Synthetic dashboard refresh failure.");
        }
    }

    private sealed class CountingDashboardService(IDashboardService inner, DashboardRequests requests) : IDashboardService
    {
        public Task<DashboardViewModel> GetDashboardAsync(string userId)
        {
            requests.Record();
            return inner.GetDashboardAsync(userId);
        }
    }
}
