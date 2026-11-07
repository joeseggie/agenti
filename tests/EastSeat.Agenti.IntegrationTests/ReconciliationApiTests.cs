using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.IntegrationTests;

public sealed class ReconciliationApiTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task JwtOnlyApi_DoesNotCombineCookieAdminWithBearerAgent()
    {
        using var scenario = Seed();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var cookie = app.Cookie(scenario.Admin);
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));

        using var response = await client.GetAsync("/api/banking/admin/accounts");
        using var ordinary = await client.GetAsync("/api/banking/current");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
    }

    [Fact]
    public async Task CustomerCapture_RequiresCommandIdentityAndPreservesRetry()
    {
        using var scenario = Seed();
        scenario.AddApprovedOpening();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));
        using var invalid = await client.PostAsJsonAsync("/api/banking/transactions", new
        {
            scenario.Account.Id, CashSessionId = scenario.Session.Id, Amount = 50_000m
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var form = ReconciliationPostgresTests.Capture(scenario);
        using var first = await client.PostAsJsonAsync("/api/banking/transactions", form);
        using var second = await client.PostAsJsonAsync("/api/banking/transactions", form);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<ApiResponse<CustomerTransactionDto>>();
        var secondResult = await second.Content.ReadFromJsonAsync<ApiResponse<CustomerTransactionDto>>();
        Assert.Equal(firstResult!.Data!.Id, secondResult!.Data!.Id);
        Assert.Equal(1, await DbContext.CustomerTransactions.CountAsync());
    }

    [Fact]
    public async Task SupervisorCannotReadStatements_AndForeignBranchCannotReadEvidence()
    {
        using var scenario = Seed();
        var supervisor = new ApplicationUser
        {
            Id = "test-supervisor", UserName = "test-supervisor", Role = UserRole.Supervisor,
            BranchId = scenario.Admin.BranchId, IsActive = true
        };
        DbContext.Users.Add(supervisor);
        await DbContext.SaveChangesAsync();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(supervisor));
        using var statementResponse = await client.GetAsync("/api/banking/admin/accounts");
        Assert.Equal(HttpStatusCode.NotFound, statementResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.Admin));
        using var content = new MultipartFormDataContent
        {
            { new StringContent(scenario.Account.Id.ToString()), "bankAccountId" },
            { new StringContent(Guid.NewGuid().ToString()), "uploadKey" },
            { new StringContent("true"), "isStatement" },
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("synthetic private statement")), "file", "test.csv" }
        };
        using var uploaded = await client.PostAsync("/api/banking/evidence", content);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var file = await uploaded.Content.ReadFromJsonAsync<ApiResponse<EastSeat.Agenti.Web.Features.BankStatements.BankEvidenceInfoDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));
        using var statement = await client.GetAsync($"/api/banking/evidence/{file!.Data!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, statement.StatusCode);

        var foreignBranch = new EastSeat.Agenti.Shared.Domain.Entities.Branch { Name = "Other synthetic branch", CreatedAt = BankingScenario.Now };
        DbContext.Branches.Add(foreignBranch);
        await DbContext.SaveChangesAsync();
        var foreignAdmin = new ApplicationUser
        {
            Id = "foreign-test-admin", UserName = "foreign-test-admin", Role = UserRole.Admin,
            BranchId = foreignBranch.Id, IsActive = true
        };
        DbContext.Users.Add(foreignAdmin);
        await DbContext.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(foreignAdmin));
        using var foreignResponse = await client.GetAsync($"/api/banking/evidence/{file.Data.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    }

    private BankingScenario Seed()
    {
        var scenario = new BankingScenario(DbContext);
        var setup = DbContext.AppConfigs.Single(c => c.Key == "SetupComplete");
        setup.Value = "true";
        DbContext.SaveChanges();
        return scenario;
    }
}
