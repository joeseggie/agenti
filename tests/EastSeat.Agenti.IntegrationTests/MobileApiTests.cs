using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Agents;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CashSessions;
using EastSeat.Agenti.Web.Features.Dashboard;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EastSeat.Agenti.IntegrationTests;

public sealed class MobileApiTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ProtectedMobileEndpoints_RequireBearerEvenWithAdminCookie()
    {
        using var scenario = Seed();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var cookie = app.Cookie(scenario.Admin);
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");
        foreach (var path in new[] { "/api/auth/me", "/api/dashboard", "/api/agents",
            "/api/cash-sessions", "/api/cash-counts/current", $"/api/vault/{scenario.Admin.BranchId}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(UserRole.Agent)]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Admin)]
    public async Task MobileReads_RejectForeignBranchesAndIgnoreClaimedAssignments(UserRole role)
    {
        using var scenario = Seed();
        var foreign = AddAgent(null, "FORE");
        var foreignSession = new CashSession
        {
            BranchId = foreign.Agent.BranchId!.Value, SessionDate = scenario.Session.SessionDate,
            OpenedAt = BankingScenario.Now, Status = CashSessionStatus.Open
        };
        DbContext.CashSessions.Add(foreignSession);
        await DbContext.SaveChangesAsync();
        scenario.User.Role = role;
        await DbContext.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, scenario.User);
        foreach (var path in new[] {
            $"/api/agents/{foreign.Agent.Id}", $"/api/agents/{foreign.Agent.Id}/wallets",
            $"/api/cash-sessions/{foreignSession.Id}", $"/api/cash-sessions?branchId={foreign.Agent.BranchId}",
            $"/api/vault/{foreign.Agent.BranchId}", $"/api/vault/{foreign.Agent.BranchId}/transactions",
            $"/api/cash-counts/pending?branchId={foreign.Agent.BranchId}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var agents = await client.GetFromJsonAsync<ApiResponse<List<AgentListItemDto>>>("/api/agents");
        Assert.DoesNotContain(agents!.Data!, a => a.Id == foreign.Agent.Id);
        var sessions = await client.GetFromJsonAsync<ApiResponse<List<CashSessionListItemDto>>>("/api/cash-sessions");
        Assert.DoesNotContain(sessions!.Data!, s => s.Id == foreignSession.Id);

        var claimed = new ApplicationUser
        {
            Id = scenario.User.Id, Role = role, BranchId = foreign.Agent.BranchId, IsActive = true
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token(claimed));
        var me = await client.GetFromJsonAsync<ApiResponse<SessionUserResponse>>("/api/auth/me");
        Assert.Equal(scenario.User.BranchId, me!.Data!.BranchId);
    }

    [Fact]
    public async Task AgentReads_ContainOnlyOwnWalletsAndCountSummaries()
    {
        using var scenario = Seed();
        scenario.AddApprovedOpening();
        var peer = AddAgent(scenario.User.BranchId, "PEER");
        DbContext.CashCounts.Add(new CashCount
        {
            AgentId = peer.Agent.Id, CashSessionId = scenario.Session.Id, IsOpening = true,
            CountDate = scenario.Session.SessionDate, Status = CashCountStatus.Approved,
            TotalAmount = 777m, CreatedAt = BankingScenario.Now,
            Details = [new() { WalletId = peer.Wallet.Id, Amount = 777m }]
        });
        await DbContext.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, scenario.User);
        var agents = await client.GetFromJsonAsync<ApiResponse<List<AgentListItemDto>>>("/api/agents");
        Assert.Equal(scenario.Agent.Id, Assert.Single(agents!.Data!).Id);
        var dashboard = await client.GetFromJsonAsync<ApiResponse<DashboardViewModel>>("/api/dashboard");
        Assert.DoesNotContain(dashboard!.Data!.Wallets, w => w.WalletId == peer.Wallet.Id);
        Assert.Equal(0, dashboard.Data.VaultBalance);
        var detail = await client.GetFromJsonAsync<ApiResponse<CashSessionDetailDto>>($"/api/cash-sessions/{scenario.Session.Id}");
        Assert.Equal(scenario.Agent.Id, Assert.Single(detail!.Data!.AgentSummaries).AgentId);
        Assert.Equal(1_100_000m, detail.Data.TotalOpeningAmount);
        var list = await client.GetFromJsonAsync<ApiResponse<List<CashSessionListItemDto>>>("/api/cash-sessions");
        Assert.Equal(1, Assert.Single(list!.Data!).AgentCount);
        using var peerResponse = await client.GetAsync($"/api/agents/{peer.Agent.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, peerResponse.StatusCode);
    }

    [Fact]
    public async Task SupervisorReads_IncludeOwnBranchAgentsAndCorrectVault()
    {
        using var scenario = Seed();
        var peer = AddAgent(scenario.User.BranchId, "PEER");
        var foreign = AddAgent(null, "FORE");
        scenario.Admin.Role = UserRole.Supervisor;
        var vault = await DbContext.Vaults.SingleAsync(v => v.BranchId == scenario.User.BranchId);
        vault.CurrentBalance = 654_000m;
        await DbContext.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, scenario.Admin);
        var agents = await client.GetFromJsonAsync<ApiResponse<List<AgentListItemDto>>>("/api/agents");
        Assert.Equal(2, agents!.Data!.Count);
        Assert.Contains(agents.Data, a => a.Id == peer.Agent.Id);
        Assert.DoesNotContain(agents.Data, a => a.Id == foreign.Agent.Id);
        var dashboard = await client.GetFromJsonAsync<ApiResponse<DashboardViewModel>>("/api/dashboard");
        Assert.Equal(654_000m, dashboard!.Data!.VaultBalance);
        Assert.DoesNotContain(dashboard.Data.Wallets, w => w.WalletId == foreign.Wallet.Id);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("role-changed")]
    public async Task ExistingJwt_RejectsChangedAccount(string change)
    {
        using var scenario = Seed();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, scenario.User);
        switch (change)
        {
            case "inactive": scenario.User.IsActive = false; break;
            case "deleted": scenario.User.IsDeleted = true; break;
            case "role-changed": scenario.User.Role = UserRole.Supervisor; break;
        }
        await DbContext.SaveChangesAsync();
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UsesExistingContractAndUnknownAccountReturnsUnauthorized()
    {
        using var scenario = Seed();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var password = $"Test-{Guid.NewGuid():N}!9";
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(scenario.User.Id);
            Assert.True((await users.SetEmailAsync(user!, "mobile-test@example.invalid")).Succeeded);
            Assert.True((await users.AddPasswordAsync(user!, password)).Succeeded);
        }
        using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "mobile-test@example.invalid", Password = password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var result = await login.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        Assert.Equal("Bearer", result!.Data!.TokenType);
        Assert.True(result.Data.ExpiresIn > 0);
        client.DefaultRequestHeaders.Authorization = new("Bearer", result.Data.AccessToken);
        var me = await client.GetFromJsonAsync<ApiResponse<SessionUserResponse>>("/api/auth/me");
        Assert.Equal(scenario.User.Id, me!.Data!.UserId);
        using var unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "missing@example.invalid", Password = password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task CashCounts_RejectForeignAndDuplicateWalletsWithoutWriting()
    {
        using var scenario = Seed();
        var foreign = AddAgent(null, "FORE");
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, scenario.User);
        foreach (var ids in new[] { new[] { foreign.Wallet.Id }, new[] { scenario.CashWallet.Id, scenario.CashWallet.Id } })
        {
            using var response = await client.PostAsJsonAsync("/api/cash-counts", new CashCountFormModel
            {
                IsOpening = true,
                WalletEntries = ids.Select(id => new WalletCountEntryDto { WalletId = id, CountedAmount = 10m }).ToList()
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(0, await DbContext.CashCounts.CountAsync());
        Assert.Equal(0, await DbContext.CashCountDetails.CountAsync());
    }

    [Fact]
    public async Task LegacyCashCount_SaveSubmitAndReadOnlyApproval_PreserveContract()
    {
        using var scenario = Seed();
        scenario.Session.SessionDate = DateOnly.FromDateTime(DateTime.UtcNow);
        scenario.Session.WorkflowVersion = SessionWorkflowVersion.Legacy;
        await DbContext.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, scenario.User);
        var initialized = await client.GetFromJsonAsync<ApiResponse<CashCountFormModel>>("/api/cash-counts/initialize?isOpening=true");
        var form = initialized!.Data!;
        Assert.Equal(2, form.WalletEntries.Count);
        foreach (var wallet in form.WalletEntries) wallet.CountedAmount = 0;
        using var saved = await client.PostAsJsonAsync("/api/cash-counts", form);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var result = await saved.Content.ReadFromJsonAsync<ApiResponse<CashCountSaveResult>>();
        using var submitted = await client.PostAsync($"/api/cash-counts/{result!.Data!.CashCountId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var count = await DbContext.CashCounts.SingleAsync();
        Assert.Equal(CashCountStatus.PendingApproval, count.Status);
        count.Status = CashCountStatus.Approved;
        await DbContext.SaveChangesAsync();
        var readOnly = await client.GetFromJsonAsync<ApiResponse<CashCountFormModel>>($"/api/cash-counts/{count.Id}");
        Assert.True(readOnly!.Data!.IsReadOnly);
    }

    [Fact]
    public async Task AgentCanJoinExistingBranchSession_ButCannotReadPeersBeforeJoining()
    {
        using var scenario = Seed();
        scenario.Account.WorkflowEnabled = true;
        var peer = AddAgent(scenario.User.BranchId, "PEER");
        await DbContext.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, peer.User);
        using var before = await client.GetAsync($"/api/cash-sessions/{scenario.Session.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        var initialized = await client.GetFromJsonAsync<ApiResponse<CashCountFormModel>>("/api/cash-counts/initialize?isOpening=true");
        var form = initialized!.Data!;
        Assert.Equal(scenario.Session.Id, form.CashSessionId);
        foreach (var wallet in form.WalletEntries) wallet.ObservedAt = BankingScenario.Now;
        using var saved = await client.PostAsJsonAsync("/api/cash-counts", form);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(peer.Agent.Id, (await DbContext.CashCounts.SingleAsync()).AgentId);
    }

    [Fact]
    public async Task CrossBranchCommands_CannotMutateCountsSessionsOrVaultRequests()
    {
        using var scenario = Seed();
        scenario.AddApprovedOpening();
        var foreign = AddAgent(null, "FORE");
        foreign.User.Role = UserRole.Admin;
        var transaction = new VaultTransaction
        {
            VaultId = await DbContext.Vaults.Where(v => v.BranchId == scenario.User.BranchId).Select(v => v.Id).SingleAsync(),
            Amount = 100m, Status = VaultTransactionStatus.Pending, Type = VaultTransactionType.ManualDeposit,
            CreatedAt = BankingScenario.Now, ExpiresAt = BankingScenario.Now.AddHours(12),
            CreatedByUserId = scenario.Admin.Id, Notes = "Synthetic pending adjustment"
        };
        DbContext.VaultTransactions.Add(transaction);
        await DbContext.SaveChangesAsync();
        var count = await DbContext.CashCounts.SingleAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, foreign.User);
        foreach (var path in new[] {
            $"/api/cash-counts/{count.Id}/submit", $"/api/cash-counts/{count.Id}/approve",
            $"/api/cash-counts/{count.Id}/reject", $"/api/cash-sessions/{scenario.Session.Id}/close",
            $"/api/cash-sessions/{scenario.Session.Id}/close-agent/{scenario.Agent.Id}",
            $"/api/vault/adjustment/{transaction.Id}/approve", $"/api/vault/adjustment/{transaction.Id}/reject" })
        {
            using var result = await client.PostAsJsonAsync(path, new { rejectionReason = "Synthetic rejection reason" });
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        }
        using var forgedSave = await client.PostAsJsonAsync("/api/cash-counts", new CashCountFormModel
        {
            CashCountId = count.Id, CashSessionId = scenario.Session.Id, IsOpening = true
        });
        Assert.Equal(HttpStatusCode.Forbidden, forgedSave.StatusCode);
        await DbContext.Entry(count).ReloadAsync();
        await DbContext.Entry(transaction).ReloadAsync();
        Assert.Equal(CashCountStatus.Approved, count.Status);
        Assert.Equal(VaultTransactionStatus.Pending, transaction.Status);
    }

    [Fact]
    public async Task VaultCommands_EnforceBranchAndDualApproval()
    {
        using var scenario = Seed();
        var foreign = AddAgent(null, "FORE");
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = Client(app, scenario.Admin);
        var form = new ManualAdjustmentFormModel { Amount = 100m, IsDeposit = true, Notes = "Synthetic mobile deposit" };
        using var blocked = await client.PostAsJsonAsync($"/api/vault/{foreign.Agent.BranchId}/adjustment", form);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(0, await DbContext.VaultTransactions.CountAsync());
        using var created = await client.PostAsJsonAsync($"/api/vault/{scenario.Admin.BranchId}/adjustment", form);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<ApiResponse<VaultOperationResult>>();
        using var selfApproval = await client.PostAsync($"/api/vault/adjustment/{result!.Data!.TransactionId}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, selfApproval.StatusCode);
        Assert.Equal(VaultTransactionStatus.Pending, (await DbContext.VaultTransactions.SingleAsync()).Status);

        scenario.User.Role = UserRole.Admin;
        await DbContext.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token(scenario.User));
        using var approved = await client.PostAsync($"/api/vault/adjustment/{result.Data.TransactionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    private BankingScenario Seed()
    {
        var scenario = new BankingScenario(DbContext);
        DbContext.AppConfigs.Single(c => c.Key == "SetupComplete").Value = "true";
        DbContext.SaveChanges();
        return scenario;
    }

    private static HttpClient Client(BankingApplicationFactory app, ApplicationUser user)
    {
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(user));
        return client;
    }

    private (ApplicationUser User, Agent Agent, Wallet Wallet) AddAgent(long? branchId, string code)
    {
        if (!branchId.HasValue)
        {
            var branch = new Branch { Name = $"Synthetic {code}", CreatedAt = BankingScenario.Now };
            DbContext.Branches.Add(branch);
            DbContext.SaveChanges();
            branchId = branch.Id;
        }
        var user = new ApplicationUser { Id = code, UserName = code, BranchId = branchId, Role = UserRole.Agent, IsActive = true };
        DbContext.Users.Add(user);
        DbContext.SaveChanges();
        var agent = new Agent { UserId = user.Id, BranchId = branchId, Code = code, IsActive = true, CreatedAt = BankingScenario.Now };
        DbContext.Agents.Add(agent);
        DbContext.SaveChanges();
        user.AgentId = agent.Id;
        var wallet = new Wallet { AgentId = agent.Id, WalletTypeId = 1, Name = code, Currency = "UGX", Balance = 777m, IsActive = true, CreatedAt = BankingScenario.Now };
        DbContext.Wallets.Add(wallet);
        DbContext.SaveChanges();
        return (user, agent, wallet);
    }
}
