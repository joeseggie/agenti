using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Features.CustomerTransactions;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class AndroidBankingClientTests
{
    [Fact]
    public async Task SeparateClients_UseCurrentTokenAndRemoveStaleDefaultOnLogout()
    {
        var tokens = new Tokens { Value = "first-synthetic-token" };
        var headers = new List<string?>();
        HttpClient Client() => new(new AuthenticatedApiHandler(tokens)
        {
            InnerHandler = new Responder(request =>
            {
                headers.Add(request.Headers.Authorization?.Parameter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        }) { BaseAddress = new Uri("https://banking.invalid/") };
        using var first = Client();
        using var second = Client();
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "stale");
        using var one = await first.GetAsync("api/banking/current");
        tokens.Value = "replacement-synthetic-token";
        using var two = await second.GetAsync("api/banking/current");
        tokens.Value = null;
        using var three = await first.GetAsync("api/banking/current");

        Assert.Equal(new string?[] { "first-synthetic-token", "replacement-synthetic-token", null }, headers);
    }

    [Fact]
    public async Task Login_DoesNotAttachAnotherUsersBearerToken()
    {
        var tokens = new Tokens { Value = "previous-user-synthetic-token" };
        using var client = new HttpClient(new AuthenticatedApiHandler(tokens)
        {
            InnerHandler = new Responder(request =>
            {
                Assert.Null(request.Headers.Authorization);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        });
        using var response = await client.PostAsync("https://banking.invalid/api/auth/login", null);
    }

    [Fact]
    public async Task FailedSaveAndRetry_PreserveCommandAndFinancialContract()
    {
        var commands = new List<string>();
        var attempts = 0;
        using var http = new HttpClient(new Responder(async request =>
        {
            var json = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            commands.Add(document.RootElement.GetProperty("commandId").GetString()!);
            Assert.Equal(123.45m, document.RootElement.GetProperty("amount").GetDecimal());
            Assert.Equal("0012345", document.RootElement.GetProperty("customerIdentifier").GetString());
            Assert.Equal((int)CustomerTransactionKind.Deposit, document.RootElement.GetProperty("kind").GetInt32());
            Assert.Equal("/api/banking/transactions", request.RequestUri!.AbsolutePath);
            return ++attempts == 1
                ? Json(HttpStatusCode.Conflict, """{"success":false,"error":"Refresh stale evidence."}""")
                : Json(HttpStatusCode.OK, """{"success":true,"data":{"id":42}}""");
        })) { BaseAddress = new Uri("https://banking.invalid/") };
        var api = new BankingApiClient(http);
        var form = new CustomerTransactionForm
        {
            CommandId = Guid.NewGuid(), Amount = 123.45m, CustomerIdentifier = "0012345",
            Kind = CustomerTransactionKind.Deposit, OccurredAt = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)
        };
        var failure = await Assert.ThrowsAsync<BankingApiException>(() =>
            api.PostAsync<CustomerTransactionForm, CustomerTransactionDto>("transactions", form));
        Assert.Equal(409, failure.StatusCode);

        var result = await api.PostAsync<CustomerTransactionForm, CustomerTransactionDto>("transactions", form);

        Assert.Equal(42, result.Id);
        Assert.Equal(commands[0], commands[1]);
        Assert.Equal(form.CommandId.ToString(), commands[1]);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task ErrorResponses_AreNeverReturnedAsSuccessfulEmptyData(int status)
    {
        using var http = new HttpClient(new Responder(_ => Task.FromResult(Json((HttpStatusCode)status, "<html>gateway error</html>"))))
        { BaseAddress = new Uri("https://banking.invalid/") };
        var api = new BankingApiClient(http);
        var exception = await Assert.ThrowsAsync<BankingApiException>(() => api.GetAsync<CustomerTransactionDto>("transactions/42"));
        Assert.Equal(status, exception.StatusCode);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Tokens : IAccessTokenProvider
    {
        public string? Value { get; set; }
        public Task<string?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
    }
    private sealed class Responder(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
