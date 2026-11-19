using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Agents;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace EastSeat.Agenti.E2ETests;

public sealed class AgentsBrowserTests(DatabaseFixture database, ITestOutputHelper output)
    : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Agents_SearchPagingAndEditing_PreserveDataAndRecoverFromErrors()
    {
        await using var db = database.CreateContext();
        using var scenario = new BankingScenario(db);
        for (var index = 1; index <= 15; index++)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"), UserName = $"synthetic-agent-{index}",
                FirstName = "Synthetic", LastName = $"Colleague {index:00}",
                Email = $"synthetic.colleague.{index:00}@example.invalid",
                Role = UserRole.Agent, BranchId = scenario.Agent.BranchId, IsActive = true
            };
            var agent = new Agent
            {
                User = user, BranchId = scenario.Agent.BranchId, Code = $"S{index:000}",
                IsActive = index % 5 != 0, CreatedAt = BankingScenario.Now
            };
            agent.Wallets.Add(new Wallet
            {
                WalletTypeId = 1, Name = $"Synthetic wallet {index:00}", Currency = "UGX",
                Balance = 10_000m, IsActive = true, CreatedAt = BankingScenario.Now
            });
            db.Agents.Add(agent);
        }
        await db.SaveChangesAsync();
        foreach (var entry in db.ChangeTracker.Entries<Agent>())
            if (entry.Entity.User is { } user)
                user.AgentId = entry.Entity.Id;
        (await db.AppConfigs.SingleAsync(config => config.Key == "SetupComplete")).Value = "true";
        await db.SaveChangesAsync();
        var branchName = (await db.Branches.SingleAsync(branch => branch.Id == scenario.Agent.BranchId)).Name;

        var requests = new AgentRequests();
        await using var app = new BankingApplicationFactory(database.ConnectionString, services =>
            services.AddScoped<IAgentService>(provider => new ObservedAgentService(
                provider.GetRequiredService<ApplicationDbContext>(), requests)));
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
        var cookie = app.Cookie(scenario.Admin);
        await context.AddCookiesAsync([new Cookie { Name = cookie.Name, Value = cookie.Value, Url = root, HttpOnly = true }]);
        var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(root + "/agents");
        var add = page.GetByRole(AriaRole.Button, new() { Name = "Add Agent", Exact = true });
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true });
        var rows = page.Locator(".agents-list tbody tr");
        await Expect(add).ToBeEnabledAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Agents", Level = 1 })).ToBeVisibleAsync();
        await Expect(rows).ToHaveCountAsync(10);
        Assert.Equal(1, requests.ListCalls);
        output.WriteLine($"Initial agent-list requests: {requests.ListCalls}; rendered rows: {await rows.CountAsync()}");
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
                Path = Path.Combine(artifacts, $"agents-after-{viewport.Name}.png"), FullPage = true
            });
            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - window.innerWidth");
            Assert.Equal(0, overflow);
            output.WriteLine($"{viewport.Name} overflow: {overflow}");
        }
        await page.SetViewportSizeAsync(1440, 900);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Expect(page.Locator(".mud-layout")).ToHaveCSSAsync("background-color", "rgb(17, 26, 39)");
        await SettleLayoutAsync(page);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "agents-after-dark.png"), FullPage = true
        });
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Expect(page.Locator(".mud-layout")).ToHaveCSSAsync("background-color", "rgb(244, 247, 251)");

        var search = page.GetByLabel("Search agents", new() { Exact = true });
        await page.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(rows).ToHaveCountAsync(6);
        await search.FillAsync("synthetic.colleague.01@");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows).ToContainTextAsync("S001");
        await search.FillAsync("no matching colleague");
        await Expect(page.GetByText("No agents match these filters.", new() { Exact = false })).ToBeVisibleAsync();
        await search.FillAsync("");
        await Expect(rows).ToHaveCountAsync(10);
        await SelectAsync(page, "Agent status", "Inactive");
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows).ToContainTextAsync(["S005", "S010", "S015"]);
        await search.FillAsync("S010");
        await Expect(rows).ToHaveCountAsync(1);
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear filters", Exact = true }).ClickAsync();
        await Expect(search).ToHaveValueAsync("");
        await Expect(rows).ToHaveCountAsync(10);
        Assert.Equal(1, requests.ListCalls);

        requests.FailNextList();
        await refresh.ClickAsync();
        await Expect(page.GetByText("Previously loaded information is still shown.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(rows).ToHaveCountAsync(10);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "agents-after-refresh-error.png"), FullPage = true
        });
        await refresh.ClickAsync();
        await Expect(page.GetByText("Previously loaded information is still shown.", new() { Exact = false })).ToHaveCountAsync(0);
        await Expect(refresh).ToBeEnabledAsync();
        Assert.Equal(3, requests.ListCalls);

        await search.FillAsync("TEST");
        await Expect(rows).ToHaveCountAsync(1);
        var edit = page.GetByRole(AriaRole.Button, new() { Name = "Edit Test Agent", Exact = true });
        var dialog = page.GetByRole(AriaRole.Dialog);
        var save = dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        var cancel = dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });
        var code = dialog.GetByLabel("Agent Code", new() { Exact = true });
        var detailGate = requests.HoldNextDetails();
        await edit.ClickAsync();
        await detailGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Edit Agent", Exact = true })).ToBeVisibleAsync();
        await Expect(dialog.GetByText("Loading agent data...", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(code).ToHaveCountAsync(0);
        await Expect(save).ToBeDisabledAsync();
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "agents-after-dialog-loading.png"), FullPage = true
        });
        await cancel.ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(add).ToBeDisabledAsync();
        detailGate.Release.TrySetResult();
        await Expect(add).ToBeEnabledAsync();
        await Expect(dialog).ToHaveCountAsync(0);

        requests.FailNextDetails();
        await edit.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(dialog.GetByText("Agent data could not be loaded.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(save).ToBeDisabledAsync();
        await Expect(dialog.Locator(".agent-form-error")).ToBeFocusedAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Retry loading", Exact = true }).ClickAsync();
        await Expect(code).ToHaveValueAsync("TEST");
        await Expect(save).ToBeEnabledAsync();
        await code.FillAsync("");
        await code.PressAsync("Tab");
        await save.ClickAsync();
        await Expect(dialog.GetByText("Enter an agent code before saving.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal(0, requests.UpdateCalls);
        await code.FillAsync("S001");
        await code.PressAsync("Tab");
        await save.ClickAsync();
        await Expect(dialog.GetByText("already exists", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(code).ToHaveValueAsync("S001");
        await Expect(dialog.Locator(".agent-form-error")).ToBeFocusedAsync();
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "agents-after-dialog-rejection.png"), FullPage = true
        });

        await code.FillAsync("UPDT");
        await code.PressAsync("Tab");
        var saveGate = requests.HoldNextUpdate();
        await save.ClickAsync(new() { ClickCount = 2 });
        await saveGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Expect(cancel).ToBeDisabledAsync();
        await Expect(code).ToBeDisabledAsync();
        await Expect(dialog.GetByRole(AriaRole.Button).Filter(new() { HasText = "Saving..." })).ToBeDisabledAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeVisibleAsync();
        Assert.Equal(2, requests.UpdateCalls);
        saveGate.Release.TrySetResult();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(page.GetByText("Agent updated successfully.", new() { Exact = true })).ToBeVisibleAsync();
        await search.FillAsync("UPDT");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows).ToContainTextAsync("Test Agent");
        Assert.Equal(2, requests.UpdateCalls);

        requests.MissingNextDetails();
        await edit.ClickAsync();
        await Expect(dialog.GetByText("This agent is no longer available.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(save).ToBeDisabledAsync();
        await Expect(code).ToHaveCountAsync(0);
        await cancel.ClickAsync();
        await edit.ClickAsync();
        await Expect(code).ToHaveValueAsync("UPDT");
        await code.FillAsync("SAFE");
        await code.PressAsync("Tab");
        requests.LoseNextSaveResponse();
        await save.ClickAsync();
        await Expect(dialog.GetByText("The save could not be confirmed.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(save).ToBeDisabledAsync();
        await Expect(code).ToHaveValueAsync("SAFE");
        await Expect(code).ToBeDisabledAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await refresh.ClickAsync();
        await Expect(refresh).ToBeEnabledAsync();
        await search.FillAsync("SAFE");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows).ToContainTextAsync("Test Agent");
        var savedAgent = await db.Agents.AsNoTracking().Include(agent => agent.User)
            .Include(agent => agent.Wallets).SingleAsync(agent => agent.Id == scenario.Agent.Id);
        Assert.Equal("SAFE", savedAgent.Code);
        Assert.NotNull(savedAgent.User);
        Assert.Equal(savedAgent.BranchId, savedAgent.User.BranchId);
        Assert.Equal(1_100_000m, savedAgent.Wallets.Sum(wallet => wallet.Balance));

        await page.SetViewportSizeAsync(390, 844);
        await SettleLayoutAsync(page);
        await add.ClickAsync();
        await Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Add Agent", Exact = true })).ToBeVisibleAsync();
        await Expect(save).ToBeEnabledAsync();
        await save.ClickAsync();
        await Expect(dialog.GetByText("Select a user before saving.", new() { Exact = true })).ToBeVisibleAsync();
        await SelectAsync(page, "Select User",
            $"{scenario.Admin.FirstName} {scenario.Admin.LastName} ({scenario.Admin.Email})");
        await Expect(dialog.GetByText("Select a user before saving.", new() { Exact = true })).ToHaveCountAsync(0);
        await SelectAsync(page, "Branch", branchName);
        await SettleLayoutAsync(page);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(artifacts, "agents-after-create-mobile.png"), FullPage = true
        });
        await save.ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(page.GetByText("Agent created successfully.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(add).ToBeEnabledAsync();
        await add.ClickAsync();
        await Expect(dialog.GetByText("All registered users are already linked to agents.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(save).ToBeDisabledAsync();
        await cancel.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(dialog).ToHaveCountAsync(0);

        var actions = page.GetByRole(AriaRole.Button, new() { Name = "Actions for Test Agent", Exact = true });
        await actions.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Locator(".mud-popover-open").GetByText("View", new() { Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(root + $"/agents/{scenario.Agent.Id}");
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        Assert.Empty(pageErrors);
    }

    private static async Task SelectAsync(IPage page, string label, string option)
    {
        var select = page.Locator(".mud-input-control.mud-select")
            .Filter(new() { Has = page.GetByText(label, new() { Exact = true }) });
        await select.Locator(".mud-input").ClickAsync();
        await page.Locator(".mud-popover-open").GetByText(option, new() { Exact = true }).ClickAsync();
    }

    private static async Task SettleLayoutAsync(IPage page)
    {
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        await page.WaitForFunctionAsync("document.getAnimations().every(animation => animation.playState !== 'running')");
    }

    private sealed class AgentRequests
    {
        private int _listCalls;
        private int _updateCalls;
        private int _failList;
        private int _failDetails;
        private int _missingDetails;
        private int _loseSaveResponse;
        private RequestGate? _nextDetails;
        private RequestGate? _nextUpdate;
        public int ListCalls => Volatile.Read(ref _listCalls);
        public int UpdateCalls => Volatile.Read(ref _updateCalls);

        public void FailNextList() => Interlocked.Exchange(ref _failList, 1);
        public void FailNextDetails() => Interlocked.Exchange(ref _failDetails, 1);
        public void MissingNextDetails() => Interlocked.Exchange(ref _missingDetails, 1);
        public void LoseNextSaveResponse() => Interlocked.Exchange(ref _loseSaveResponse, 1);
        public bool TakeMissingDetails() => Interlocked.Exchange(ref _missingDetails, 0) == 1;
        public bool TakeLostSaveResponse() => Interlocked.Exchange(ref _loseSaveResponse, 0) == 1;

        public RequestGate HoldNextDetails() => _nextDetails = new();
        public RequestGate HoldNextUpdate() => _nextUpdate = new();

        public void RecordList()
        {
            Interlocked.Increment(ref _listCalls);
            if (Interlocked.Exchange(ref _failList, 0) == 1)
                throw new InvalidOperationException("Synthetic list failure.");
        }

        public async Task BeforeDetailsAsync()
        {
            if (Interlocked.Exchange(ref _nextDetails, null) is { } gate)
                await gate.WaitAsync();
            if (Interlocked.Exchange(ref _failDetails, 0) == 1)
                throw new InvalidOperationException("Synthetic detail failure.");
        }

        public async Task BeforeUpdateAsync()
        {
            Interlocked.Increment(ref _updateCalls);
            if (Interlocked.Exchange(ref _nextUpdate, null) is { } gate)
                await gate.WaitAsync();
        }
    }

    private sealed class RequestGate
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync()
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private sealed class ObservedAgentService(ApplicationDbContext db, AgentRequests requests)
        : AgentService(db), IAgentService
    {
        Task<List<AgentListItemDto>> IAgentService.GetAgentsAsync()
        {
            requests.RecordList();
            return base.GetAgentsAsync();
        }

        async Task<AgentDetailDto?> IAgentService.GetAgentAsync(long id)
        {
            await requests.BeforeDetailsAsync();
            return requests.TakeMissingDetails() ? null : await base.GetAgentAsync(id);
        }

        async Task<SaveResult> IAgentService.UpdateAgentAsync(AgentFormModel model)
        {
            await requests.BeforeUpdateAsync();
            var result = await base.UpdateAgentAsync(model);
            if (result.Success && requests.TakeLostSaveResponse())
                throw new IOException("Synthetic response loss after the confirmed database save.");
            return result;
        }
    }
}
