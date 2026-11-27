using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Security;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.McpAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using Npgsql;

namespace EastSeat.Agenti.IntegrationTests;

public sealed class McpHttpTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly string[] Metrics =
        ["daily_totals", "agent_performance", "wallet_type_volume", "discrepancy_rate", "vault_flow"];

    [Fact]
    public async Task OfficialSdkClient_InitializesListsAndCallsOverHttp()
    {
        var scope = await SeedAsync();
        await using var app = new McpApplicationFactory(database.ConnectionString);
        using var http = Client(app, (await IssueAsync(scope.Owner, app.Clock)).Secret);
        await using var transport = new HttpClientTransport(new() { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http);
        await using var client = await McpClient.CreateAsync(transport);
        Assert.Equal(6, (await client.ListToolsAsync()).Count);
        var result = await client.CallToolAsync("query_agent_wallets");
        Assert.False(result.IsError == true);
        Assert.Contains(scope.Owner.FirstName, JsonSerializer.Serialize(result));
        Assert.DoesNotContain(scope.OtherOwner.FirstName, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task RestrictedLogin_ExecutesProtocolAndEveryTool_WithoutCrossBranchAccessOrWrites()
    {
        var scope = await SeedAsync();
        var connection = await ReadOnlyConnectionAsync();
        await using var app = new McpApplicationFactory(connection);
        using var admin = Client(app, (await IssueAsync(scope.Admin, app.Clock)).Secret);
        using var supervisor = Client(app, (await IssueAsync(scope.Owner, app.Clock)).Secret);
        await using var before = database.CreateContext();
        var audits = await before.UserAuditLogs.CountAsync();
        var tokens = await before.McpAccessTokens.CountAsync();

        var initialized = await RpcAsync(supervisor, "initialize", new
        {
            protocolVersion = "2025-11-25", capabilities = new { },
            clientInfo = new { name = "agenti-tests", version = "1.0" }
        });
        Assert.Equal("agenti-mcp", initialized.GetProperty("serverInfo").GetProperty("name").GetString());
        using (var notification = await supervisor.PostAsJsonAsync("/mcp",
            new { jsonrpc = "2.0", method = "notifications/initialized" }))
            Assert.Equal(HttpStatusCode.Accepted, notification.StatusCode);

        var listed = await RpcAsync(supervisor, "tools/list", new { });
        var tools = listed.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(6, tools.Length);
        foreach (var tool in tools)
        {
            var properties = tool.GetProperty("inputSchema").GetProperty("properties");
            foreach (var hidden in new[] { "db", "config", "caller", "cancellationToken" })
                Assert.False(properties.TryGetProperty(hidden, out _), $"Injected parameter {hidden} must not be advertised.");
            var name = tool.GetProperty("name").GetString()!;
            foreach (var metric in name == "query_trends" ? Metrics : new[] { "" })
            {
                var args = Arguments(name, metric);
                var own = await CallAsync(supervisor, name, args);
                Assert.False(own.TryGetProperty("isError", out var error) && error.GetBoolean(), own.ToString());
                Assert.DoesNotContain(scope.OtherOwner.FirstName, Text(own));
                args["branchId"] = scope.Owner.BranchId;
                Assert.Equal(Text(own), Text(await CallAsync(admin, name, args)));
                Assert.Equal(Text(own), Text(await CallAsync(supervisor, name, args)));
                args["branchId"] = scope.OtherOwner.BranchId;
                var denied = await CallAsync(supervisor, name, args);
                Assert.True(denied.GetProperty("isError").GetBoolean());
                Assert.DoesNotContain(scope.OtherOwner.FirstName, Text(denied));
                args.Remove("branchId");
                Assert.NotEqual(Text(own), Text(await CallAsync(admin, name, args)));
            }
        }
        using (var ready = await admin.GetAsync("/health/ready"))
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(audits, await before.UserAuditLogs.CountAsync());
        Assert.Equal(tokens, await before.McpAccessTokens.CountAsync());

        await using var sql = new NpgsqlConnection(connection);
        await sql.OpenAsync();
        foreach (var command in new[]
        {
            "SELECT \"PasswordHash\" FROM \"AspNetUsers\"",
            "SELECT * FROM \"AspNetUserTokens\"",
            "SELECT * FROM \"DailyBankStatements\"",
            "UPDATE \"McpAccessTokens\" SET \"Name\" = \"Name\" WHERE FALSE",
            "DELETE FROM \"Transactions\" WHERE FALSE",
            "CREATE TABLE public.mcp_forbidden (id int)"
        })
        {
            await using var denied = new NpgsqlCommand(command, sql);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }
        await using var revoke = new NpgsqlConnection(database.ConnectionString);
        await revoke.OpenAsync();
        var role = new NpgsqlConnectionStringBuilder(connection).Username;
        await using (var command = new NpgsqlCommand($"REVOKE SELECT ON \"Transactions\" FROM \"{role}\"", revoke))
            await command.ExecuteNonQueryAsync();
        using var unavailable = await admin.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Empty(await unavailable.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("inactive", HttpStatusCode.Unauthorized)]
    [InlineData("deleted", HttpStatusCode.Unauthorized)]
    [InlineData("locked", HttpStatusCode.Unauthorized)]
    [InlineData("stamp", HttpStatusCode.Unauthorized)]
    [InlineData("agent", HttpStatusCode.Forbidden)]
    [InlineData("branch", HttpStatusCode.Forbidden)]
    public async Task SameClient_UsesCurrentAccountState(string change, HttpStatusCode expected)
    {
        var scope = await SeedAsync();
        await using var app = new McpApplicationFactory(database.ConnectionString);
        using var client = Client(app, (await IssueAsync(scope.Owner, app.Clock)).Secret);
        await RpcAsync(client, "tools/list", new { });
        await using var db = database.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Id == scope.Owner.Id);
        switch (change)
        {
            case "inactive": user.IsActive = false; break;
            case "deleted": user.IsDeleted = true; break;
            case "locked": user.LockoutEnabled = true; user.LockoutEnd = app.Clock.Now.AddHours(1); break;
            case "stamp": user.SecurityStamp = Guid.NewGuid().ToString(); break;
            case "agent": user.Role = UserRole.Agent; break;
            case "branch": user.BranchId = null; break;
        }
        await db.SaveChangesAsync();
        using var response = await SendRpcAsync(client, "tools/list", new { });
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SameClient_SeesBranchChangesAndPermanentRevocation()
    {
        var scope = await SeedAsync();
        await using var app = new McpApplicationFactory(database.ConnectionString);
        var token = await IssueAsync(scope.Owner, app.Clock);
        using var client = Client(app, token.Secret);
        var first = Text(await CallAsync(client, "query_agent_wallets", new()));
        Assert.Contains(scope.Owner.FirstName, first);
        await using (var db = database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == scope.Owner.Id);
            user.BranchId = scope.OtherOwner.BranchId;
            await db.SaveChangesAsync();
        }
        var moved = Text(await CallAsync(client, "query_agent_wallets", new()));
        Assert.Contains(scope.OtherOwner.FirstName, moved);
        Assert.DoesNotContain(scope.Owner.FirstName, moved);
        await using (var db = database.CreateContext())
            await Service(db, app.Clock).RevokeAsync(Principal(scope.Admin), token.Id);
        using var revoked = await SendRpcAsync(client, "tools/list", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        await using (var db = database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == scope.Owner.Id);
            user.Role = UserRole.Admin;
            user.IsActive = true;
            await db.SaveChangesAsync();
        }
        using var stillRevoked = await SendRpcAsync(client, "tools/list", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, stillRevoked.StatusCode);
    }

    [Fact]
    public async Task Expiration_IsEnforcedImmediatelyAtBoundary()
    {
        var scope = await SeedAsync();
        await using var app = new McpApplicationFactory(database.ConnectionString);
        var token = await IssueAsync(scope.Admin, app.Clock);
        using var client = Client(app, token.Secret);
        app.Clock.Now = token.ExpiresAt.AddTicks(-1);
        await RpcAsync(client, "tools/list", new { });
        foreach (var instant in new[] { token.ExpiresAt, token.ExpiresAt.AddTicks(1) })
        {
            app.Clock.Now = instant;
            using var response = await SendRpcAsync(client, "tools/list", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task InvalidCredentialsAndOrigins_AreRejected_HealthIsAnonymous()
    {
        var scope = await SeedAsync();
        await using var app = new McpApplicationFactory(database.ConnectionString);
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        foreach (var header in new[] { "", "Basic dGVzdDp0ZXN0", "Bearer eyJ.invalid.jwt", $"Bearer {McpTokenSecret.Create()}" })
        {
            client.DefaultRequestHeaders.Remove("Authorization");
            if (header.Length > 0) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header);
            using var response = await SendRpcAsync(client, "tools/list", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Identity.Application=not-an-mcp-credential");
        using (var cookie = await SendRpcAsync(client, "tools/list", new { }))
            Assert.Equal(HttpStatusCode.Unauthorized, cookie.StatusCode);
        using (var health = await client.GetAsync("/health/live"))
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await IssueAsync(scope.Admin, app.Clock)).Secret);
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        using var origin = await SendRpcAsync(client, "tools/list", new { });
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
    }

    [Fact]
    public async Task TwoTokensForOneUser_ShareThirtyRequestAllowance()
    {
        var scope = await SeedAsync();
        await using var app = new McpApplicationFactory(database.ConnectionString, requestsPerMinute: 30);
        using var first = Client(app, (await IssueAsync(scope.Admin, app.Clock)).Secret);
        using var second = Client(app, (await IssueAsync(scope.Admin, app.Clock)).Secret);
        for (var i = 0; i < 30; i++)
            await RpcAsync(i % 2 == 0 ? first : second, "tools/list", new { });
        using var limited = await SendRpcAsync(second, "tools/list", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var independent = Client(app, (await IssueAsync(scope.Owner, app.Clock)).Secret);
        await RpcAsync(independent, "tools/list", new { });
    }

    [Fact]
    public async Task ToolConcurrency_IsBoundedAndCancelledRequestsReleaseTheirContext()
    {
        var scope = await SeedAsync();
        var gate = new QueryGate();
        await using var app = new McpApplicationFactory(database.ConnectionString, configureServices: services =>
        {
            services.RemoveAll<ReadOnlyDbContext>();
            services.AddScoped(_ => new ReadOnlyDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(database.ConnectionString).AddInterceptors(gate).Options));
        });
        using var first = Client(app, (await IssueAsync(scope.Owner, app.Clock)).Secret);
        using var second = Client(app, (await IssueAsync(scope.OtherOwner, app.Clock)).Secret);
        using var anonymous = app.CreateClient();
        using var cancellation = new CancellationTokenSource();
        var firstCall = SendRpcAsync(first, "tools/call", new { name = "query_agent_wallets", arguments = new { } }, cancellation.Token);
        var secondCall = SendRpcAsync(second, "tools/call", new { name = "query_agent_wallets", arguments = new { } });
        var probe = anonymous.GetAsync("/health/ready");
        try
        {
            await gate.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await gate.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(2, gate.Contexts.Count);
            using (var rejected = await SendRpcAsync(anonymous, "tools/list", new { }))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
                Assert.NotNull(rejected.Headers.RetryAfter);
            }
            using (var overlap = await anonymous.GetAsync("/health/ready"))
                Assert.Equal(HttpStatusCode.TooManyRequests, overlap.StatusCode);
            using (var live = await anonymous.GetAsync("/health/live"))
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstCall);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        using var ready = await probe;
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var completed = await secondCall;
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var result = await ReadResultAsync(completed);
        Assert.Contains(scope.OtherOwner.FirstName, Text(result));
        Assert.DoesNotContain(scope.Owner.FirstName, Text(result));
        await RpcAsync(first, "tools/list", new { });
    }

    [Fact]
    public async Task RowCap_AppliesToMatchingWalletsAndParticipatingAgents()
    {
        var scope = await SeedAsync();
        await using var db = database.CreateContext();
        var first = await db.Agents.SingleAsync(a => a.UserId == scope.Owner.Id);
        var branchId = Assert.IsType<long>(first.BranchId);
        var session = await db.CashSessions.SingleAsync(s => s.BranchId == branchId);
        var bank = new Wallet { AgentId = first.Id, WalletTypeId = 4, Name = "Z-last bank", IsActive = true };
        var second = new Agent { User = User(UserRole.Agent, branchId), BranchId = branchId,
            Code = $"B{branchId:D3}", IsActive = true };
        db.AddRange(bank, new CashCount { Agent = second, CashSession = session, IsOpening = true,
            CountDate = session.SessionDate, Status = CashCountStatus.Approved });
        await db.SaveChangesAsync();

        await using var app = new McpApplicationFactory(database.ConnectionString, maxRows: 1);
        using var client = Client(app, (await IssueAsync(scope.Owner, app.Clock)).Secret);
        var wallets = Text(await CallAsync(client, "query_agent_wallets", new() { ["agentCode"] = first.Code, ["walletType"] = "bank" }));
        Assert.Contains(bank.Name, wallets);
        var args = Arguments("query_trends", "agent_performance");
        args["agentCode"] = second.Code.ToLowerInvariant();
        var performance = Text(await CallAsync(client, "query_trends", args));
        Assert.Contains(second.Code, performance);
        Assert.DoesNotContain(first.Code, performance);
    }

    [Fact]
    public async Task ConfiguredRowCap_ReportsTruncation_AndReadOnlyContextRejectsSaving()
    {
        var scope = await SeedAsync();
        await using var app = new McpApplicationFactory(database.ConnectionString, maxRows: 1);
        using var client = Client(app, (await IssueAsync(scope.Admin, app.Clock)).Secret);
        Assert.Contains("Results limited to 1 agents", Text(await CallAsync(client, "query_agent_wallets", new())));
        Assert.Contains("Results limited to 1 vaults", Text(await CallAsync(client, "query_vault_balance", new())));
        using var services = app.Services.CreateScope();
        var db = services.ServiceProvider.GetRequiredService<ReadOnlyDbContext>();
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(false));
    }

    private async Task<(ApplicationUser Admin, ApplicationUser Owner, ApplicationUser OtherOwner)> SeedAsync()
    {
        await using var db = database.CreateContext();
        var now = new BankingTestClock().Now;
        async Task<ApplicationUser> BranchAsync()
        {
            var branch = new Branch { Name = "MCP branch " + Guid.NewGuid().ToString("N"), CreatedAt = now };
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            var user = User(UserRole.Supervisor, branch.Id);
            db.Users.Add(user);
            var agent = new Agent { User = user, BranchId = branch.Id, Code = $"M{branch.Id:D3}", IsActive = true, CreatedAt = now };
            var wallet = new Wallet { Agent = agent, WalletTypeId = 1, Name = user.FirstName, Balance = 100, IsActive = true, CreatedAt = now };
            var session = new CashSession { BranchId = branch.Id, SessionDate = DateOnly.FromDateTime(now.Date), OpenedAt = now, Status = CashSessionStatus.Open };
            var count = new CashCount { Agent = agent, CashSession = session, IsOpening = true, TotalAmount = 100, Status = CashCountStatus.Approved, CountDate = session.SessionDate, CreatedAt = now };
            db.AddRange(wallet, count);
            await db.SaveChangesAsync();
            db.Transactions.Add(new Transaction { CashSessionId = session.Id, FromWalletId = wallet.Id, ToWalletId = wallet.Id, Type = TransactionType.Deposit, Amount = 100, CreatedAt = now });
            db.Discrepancies.Add(new Discrepancy { CashSessionId = session.Id, CashCountId = count.Id, ExpectedAmount = 100, ActualAmount = 99, Variance = -1, CreatedAt = now });
            db.VaultTransactions.Add(new VaultTransaction
            {
                VaultId = await db.Vaults.Where(v => v.BranchId == branch.Id).Select(v => v.Id).SingleAsync(),
                Amount = 100, Type = VaultTransactionType.Closing, Status = VaultTransactionStatus.Completed,
                CreatedByUserId = user.Id, CreatedAt = now, Notes = user.FirstName
            });
            await db.SaveChangesAsync();
            return user;
        }
        var owner = await BranchAsync();
        var other = await BranchAsync();
        var admin = User(UserRole.Admin, owner.BranchId!.Value);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return (admin, owner, other);
    }

    private static ApplicationUser User(UserRole role, long branchId)
    {
        var id = Guid.NewGuid().ToString("N");
        return new ApplicationUser { Id = id, UserName = id, FirstName = "Synthetic-" + id, LastName = "MCP",
            Role = role, BranchId = branchId, IsActive = true, SecurityStamp = Guid.NewGuid().ToString() };
    }

    private async Task<CreatedMcpToken> IssueAsync(ApplicationUser user, TimeProvider clock)
    {
        await using var db = database.CreateContext();
        return await Service(db, clock).CreateAsync(Principal(user), new() { Name = "Synthetic MCP client" });
    }

    private static McpAccessService Service(ApplicationDbContext db, TimeProvider clock) =>
        new(db, clock, Options.Create(new IdentityOptions()));

    private static ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(
        [new(ClaimTypes.NameIdentifier, user.Id), new("AspNet.Identity.SecurityStamp", user.SecurityStamp!)],
        IdentityConstants.ApplicationScheme));

    private async Task<string> ReadOnlyConnectionAsync()
    {
        var role = "mcp_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var sql = new NpgsqlConnection(database.ConnectionString);
        await sql.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT", sql))
            await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = role, Password = password };
        var grants = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "mcp-readonly-grants.sql")))
            .Replace(":\"database_name\"", $"\"{connection.Database}\"", StringComparison.Ordinal)
            .Replace(":\"mcp_role\"", $"\"{role}\"", StringComparison.Ordinal);
        await using var command = new NpgsqlCommand(grants, sql);
        await command.ExecuteNonQueryAsync();
        return connection.ConnectionString;
    }

    private static HttpClient Client(McpApplicationFactory app, string secret)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        return client;
    }

    private static Dictionary<string, object?> Arguments(string name, string metric)
    {
        var args = new Dictionary<string, object?>();
        if (name is not ("query_agent_wallets" or "query_vault_balance"))
        {
            args["dateFrom"] = "2026-11-02";
            args["dateTo"] = "2026-11-02";
        }
        if (name == "query_trends") args["metric"] = metric;
        if (name == "query_vault_balance") args["includeHistory"] = 10;
        return args;
    }

    private static Task<JsonElement> CallAsync(HttpClient client, string name, Dictionary<string, object?> args) =>
        RpcAsync(client, "tools/call", new { name, arguments = args });

    private static Task<HttpResponseMessage> SendRpcAsync(HttpClient client, string method, object parameters, CancellationToken cancellation = default) =>
        client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method, @params = parameters }, cancellation);

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object parameters)
    {
        using var response = await SendRpcAsync(client, method, parameters);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync(response);
    }

    private static async Task<JsonElement> ReadResultAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var json = body.StartsWith("data:", StringComparison.Ordinal) || body.Contains("\ndata:", StringComparison.Ordinal)
            ? body.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim()
            : body;
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("error", out _), json);
        return document.RootElement.GetProperty("result").Clone();
    }

    private static string Text(JsonElement result) => string.Join("\n",
        result.GetProperty("content").EnumerateArray().Select(item => item.GetProperty("text").GetString()));

    private sealed class QueryGate : DbCommandInterceptor
    {
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProbeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<Guid, bool> Contexts { get; } = new();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ProbeStarted.TrySetResult()) await Release.Task.WaitAsync(cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Agents\"", StringComparison.Ordinal))
            {
                Contexts.TryAdd(eventData.Context!.ContextId.InstanceId, true);
                if (Contexts.Count == 2) BothStarted.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
