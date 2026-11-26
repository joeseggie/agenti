using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using EastSeat.Agenti.Web.Features.CashCounts;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.IntegrationTests;

public sealed class ReconciliationApiTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("/api/banking/counts")]
    [InlineData("/api/cash-counts")]
    public async Task CashCountContract_BlocksOldClientsAndAdvertisesUpdatedForm(string route)
    {
        using var scenario = Seed();
        scenario.AddApprovedOpening();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));

        using var oldForm = await client.GetAsync($"{route}/initialize?isOpening=false");
        using var formResponse = await client.GetAsync($"{route}/initialize?isOpening=false&countContractVersion=2");
        Assert.Equal(HttpStatusCode.UpgradeRequired, oldForm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, formResponse.StatusCode);
        var form = (await formResponse.Content.ReadFromJsonAsync<ApiResponse<CashCountFormModel>>())!.Data!;
        Assert.Equal(2, form.ApiContractVersion);
        Assert.Contains(form.WalletEntries, w => w.WalletId == scenario.CashWallet.Id && w.UsesEnteredClosingAmount);
        using var oldSave = await client.PostAsJsonAsync(route, form);
        Assert.Equal(HttpStatusCode.UpgradeRequired, oldSave.StatusCode);
        using var oldSubmit = await client.PostAsync($"{route}/123/submit", null);
        Assert.Equal(HttpStatusCode.UpgradeRequired, oldSubmit.StatusCode);

        var closing = scenario.AddSubmittedClosing();
        DbContext.CashCountDetails.Add(new EastSeat.Agenti.Shared.Domain.Entities.CashCountDetail
        {
            CashCountId = closing.Id, WalletId = scenario.CashWallet.Id, Amount = 100_000m
        });
        await DbContext.SaveChangesAsync();
        using var oldRead = await client.GetAsync($"{route}/{closing.Id}");
        using var newRead = await client.GetAsync($"{route}/{closing.Id}?countContractVersion=2");
        Assert.Equal(HttpStatusCode.UpgradeRequired, oldRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newRead.StatusCode);
    }

    [Fact]
    public async Task JwtOnlyApi_DoesNotCombineCookieAdminWithBearerAgent()
    {
        using var scenario = Seed();
        await using var app = new BankingApplicationFactory(Fixture.ConnectionString);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var cookie = app.Cookie(scenario.Admin);
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));

        using var response = await client.PostAsync("/api/banking/wallet-adjustments/1/approve", null);
        using var ordinary = await client.GetAsync("/api/banking/current");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    public async Task SupervisorCanReadStatementEvidence_ButAgentsAndForeignBranchCannot()
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.Admin));
        using var content = new MultipartFormDataContent
        {
            { new StringContent(scenario.Account.Id.ToString()), "bankAccountId" },
            { new StringContent(Guid.NewGuid().ToString()), "uploadKey" },
            { new StringContent("true"), "isStatement" },
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("synthetic private statement")), "file", "test.csv" }
        };
        using var uploaded = await client.PostAsync("/api/banking/evidence", content);
        Assert.Equal(HttpStatusCode.BadRequest, uploaded.StatusCode);
        var csv = BankingScenario.StatementCsv(scenario.Session.SessionDate, 1_000_000m);
        var file = await scenario.UploadStatementAsync(scenario.Account,
            new EastSeat.Agenti.Shared.Services.BusinessClock(app.Clock),
            csv);
        var fileId = file.Id;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(supervisor));
        using var supervisorDownload = await client.GetAsync($"/closing-statement-evidence/{fileId}");
        Assert.Equal(HttpStatusCode.OK, supervisorDownload.StatusCode);
        Assert.Equal("text/csv", supervisorDownload.Content.Headers.ContentType?.MediaType);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(csv), await supervisorDownload.Content.ReadAsByteArrayAsync());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(scenario.User));
        using var statement = await client.GetAsync($"/closing-statement-evidence/{fileId}");
        Assert.Equal(HttpStatusCode.Forbidden, statement.StatusCode);

        var foreignBranch = new EastSeat.Agenti.Shared.Domain.Entities.Branch { Name = "Other synthetic branch", CreatedAt = BankingScenario.Now };
        DbContext.Branches.Add(foreignBranch);
        await DbContext.SaveChangesAsync();
        var foreignSupervisor = new ApplicationUser
        {
            Id = "foreign-test-supervisor", UserName = "foreign-test-supervisor", Role = UserRole.Supervisor,
            BranchId = foreignBranch.Id, IsActive = true
        };
        DbContext.Users.Add(foreignSupervisor);
        await DbContext.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(foreignSupervisor));
        using var foreignResponse = await client.GetAsync($"/closing-statement-evidence/{fileId}");
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
