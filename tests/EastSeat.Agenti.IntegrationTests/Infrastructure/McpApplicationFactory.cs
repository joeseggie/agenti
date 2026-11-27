using EastSeat.Agenti.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EastSeat.Agenti.IntegrationTests.Infrastructure;

public sealed class McpApplicationFactory(
    string connectionString, int requestsPerMinute = 1000, int maxRows = 1000,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<McpProgram>
{
    public BankingTestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Agenti:ConnectionString", connectionString);
        builder.UseSetting("Agenti:RequestsPerMinute", requestsPerMinute.ToString());
        builder.UseSetting("Agenti:MaxRows", maxRows.ToString());
        builder.UseSetting("AllowedHosts", "localhost;127.0.0.1");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            configureServices?.Invoke(services);
        });
    }
}
