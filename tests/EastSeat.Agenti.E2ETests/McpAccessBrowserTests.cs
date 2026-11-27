using System.Security.Claims;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Security;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.McpAccess;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EastSeat.Agenti.E2ETests;

public sealed class McpAccessBrowserTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Tokens_CreateCopyDismissRevoke_AndAdminMetadata_AreUsableAcrossViewports()
    {
        var (admin, supervisor, agent) = await SeedAsync();
        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(0);
        var root = Root(app);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright);
        await using var context = await ContextAsync(browser, app, admin, root);
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var page = await context.NewPageAsync();
        await OpenAsync(page, root);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create token", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "The Name field is required." })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Expires in days", new() { Exact = true })).ToHaveValueAsync("7");
        await page.GetByLabel("Client name", new() { Exact = true }).FillAsync("Reporting client");
        var create = page.GetByRole(AriaRole.Button, new() { Name = "Create token", Exact = true });
        await create.FocusAsync();
        await Expect(create).ToBeFocusedAsync();
        await create.EvaluateAsync("button => { button.click(); button.click(); }");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Save your new token" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Copy token", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Token copied.", new() { Exact = false })).ToBeVisibleAsync();
        var secret = await page.EvaluateAsync<string>("navigator.clipboard.readText()");
        Assert.True(McpTokenSecret.TryHash(secret, out var hash));
        await using (var db = database.CreateContext())
        {
            var token = await db.McpAccessTokens.SingleAsync(t => t.UserId == admin.Id);
            Assert.True(hash == token.TokenHash);
        }
        secret = "";
        await page.EvaluateAsync("navigator.clipboard.writeText('')");
        await page.GetByRole(AriaRole.Button, new() { Name = "Dismiss secret" }).ClickAsync();
        await page.ReloadAsync();
        await Expect(create).ToBeEnabledAsync();
        await Expect(page.GetByLabel("New MCP token", new() { Exact = true })).ToHaveCountAsync(0);
        foreach (var width in new[] { 1440, 768, 390 })
        {
            await page.SetViewportSizeAsync(width, 900);
            if (width < 960)
            {
                var drawerScrim = page.Locator(".mud-overlay-drawer .mud-overlay-scrim");
                if (await drawerScrim.IsVisibleAsync()) await drawerScrim.ClickAsync();
                await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Create token", Exact = true })).ToBeVisibleAsync();
            }
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            await Expect(create).ToBeVisibleAsync();
            var artifacts = Environment.GetEnvironmentVariable("AGENTI_MCP_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"mcp-access-{width}.png"), FullPage = true });
            }
        }
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke Reporting client", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Active", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke Reporting client", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke token", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Revoked", new() { Exact = true })).ToBeVisibleAsync();

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMcpAccessService>()
                .CreateAsync(Principal(supervisor), new() { Name = "Supervisor automation" });
        await page.GetByLabel("Show all users' tokens", new() { Exact = true }).CheckAsync();
        await Expect(page.GetByText("Supervisor automation", new() { Exact = false })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke Supervisor automation", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke token", Exact = true }).ClickAsync();
        var supervisorRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Supervisor automation" });
        await Expect(supervisorRow.GetByText("Revoked", new() { Exact = true })).ToBeVisibleAsync();
        await using (var db = database.CreateContext())
        {
            var revoked = await db.McpAccessTokens.SingleAsync(t => t.UserId == supervisor.Id);
            Assert.NotNull(revoked.RevokedAt);
            Assert.Equal(admin.Id, revoked.RevokedByUserId);
        }
        await using var deniedContext = await ContextAsync(browser, app, agent, root);
        var deniedPage = await deniedContext.NewPageAsync();
        await deniedPage.GotoAsync(root + "/mcp-access");
        await Expect(deniedPage.GetByRole(AriaRole.Button, new() { Name = "Create token", Exact = true })).ToHaveCountAsync(0);

        await using (var db = database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == admin.Id);
            user.SecurityStamp = Guid.NewGuid().ToString();
            await db.SaveChangesAsync();
        }
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh list", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Sign in with an active Admin or Supervisor account to manage MCP tokens.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("New MCP token", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task LostCreationResponse_PreservesInputAndPreventsDuplicateIssuance()
    {
        var (admin, _, _) = await SeedAsync();
        await using var app = new BankingApplicationFactory(database.ConnectionString, services =>
            services.AddScoped<IMcpAccessService>(provider =>
                new LostResponseService(ActivatorUtilities.CreateInstance<McpAccessService>(provider))));
        app.UseKestrel(0);
        var root = Root(app);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright);
        await using var context = await ContextAsync(browser, app, admin, root);
        var page = await context.NewPageAsync();
        await OpenAsync(page, root);
        await page.GetByLabel("Client name", new() { Exact = true }).FillAsync("Recoverable client");
        var create = page.GetByRole(AriaRole.Button, new() { Name = "Create token", Exact = true });
        await create.ClickAsync();
        await Expect(page.GetByText("The operation could not be completed.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Client name", new() { Exact = true })).ToHaveValueAsync("Recoverable client");
        await create.ClickAsync();
        await Expect(page.GetByText("This request was already submitted.", new() { Exact = false })).ToBeVisibleAsync();
        await using (var db = database.CreateContext())
            Assert.Equal(1, await db.McpAccessTokens.CountAsync(t => t.UserId == admin.Id));
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh list", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Revoke Recoverable client", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("New MCP token", new() { Exact = true })).ToHaveCountAsync(0);
    }

    private async Task<(ApplicationUser Admin, ApplicationUser Supervisor, ApplicationUser Agent)> SeedAsync()
    {
        await using var db = database.CreateContext();
        var branch = new Branch { Name = "MCP browser " + Guid.NewGuid().ToString("N"), CreatedAt = new BankingTestClock().Now };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        ApplicationUser User(UserRole role)
        {
            var id = Guid.NewGuid().ToString("N");
            return new() { Id = id, UserName = id, FirstName = "Synthetic", LastName = role.ToString(),
                Role = role, BranchId = branch.Id, IsActive = true, SecurityStamp = Guid.NewGuid().ToString() };
        }
        var admin = User(UserRole.Admin);
        var supervisor = User(UserRole.Supervisor);
        var agent = User(UserRole.Agent);
        db.Users.AddRange(admin, supervisor, agent);
        (await db.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        await db.SaveChangesAsync();
        return (admin, supervisor, agent);
    }

    private static ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(
        [new(ClaimTypes.NameIdentifier, user.Id), new("AspNet.Identity.SecurityStamp", user.SecurityStamp!)],
        IdentityConstants.ApplicationScheme));

    private static string Root(BankingApplicationFactory app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');

    private static Task<IBrowser> LaunchAsync(IPlaywright playwright) => playwright.Chromium.LaunchAsync(new()
    {
        Headless = true, Channel = Environment.GetEnvironmentVariable("AGENTI_TEST_BROWSER_CHANNEL")
    });

    private static async Task<IBrowserContext> ContextAsync(IBrowser browser, BankingApplicationFactory app, ApplicationUser user, string root)
    {
        var context = await browser.NewContextAsync();
        var cookie = app.Cookie(user);
        await context.AddCookiesAsync([new Microsoft.Playwright.Cookie { Name = cookie.Name, Value = cookie.Value, Url = root, HttpOnly = true }]);
        return context;
    }

    private static async Task OpenAsync(IPage page, string root)
    {
        await page.GotoAsync(root + "/mcp-access");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "MCP Access", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Create token", Exact = true })).ToBeEnabledAsync(new() { Timeout = 30_000 });
    }

    private sealed class LostResponseService(IMcpAccessService inner) : IMcpAccessService
    {
        public async Task<CreatedMcpToken> CreateAsync(ClaimsPrincipal actor, CreateMcpTokenRequest request, CancellationToken cancellationToken = default)
        {
            await inner.CreateAsync(actor, request, cancellationToken);
            throw new InvalidOperationException("Synthetic failure after commit");
        }

        public Task<McpTokenPage> ListAsync(ClaimsPrincipal actor, bool allUsers = false, int page = 0, CancellationToken cancellationToken = default) =>
            inner.ListAsync(actor, allUsers, page, cancellationToken);

        public Task RevokeAsync(ClaimsPrincipal actor, long tokenId, CancellationToken cancellationToken = default) =>
            inner.RevokeAsync(actor, tokenId, cancellationToken);
    }
}
