using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EastSeat.Agenti.IntegrationTests.Infrastructure;

public sealed class BankingApplicationFactory(
    string connectionString, Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    private readonly string _jwtKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    public BankingTestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseStaticWebAssets();
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Jwt:Key", _jwtKey);
        builder.UseSetting("ApplicationInsights:ConnectionString", "");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "EastSeat.Agenti",
            ["Jwt:Audience"] = "EastSeat.Agenti.Android",
            ["ApplicationInsights:ConnectionString"] = ""
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<IDbContextFactory<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
            services.AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(connectionString), ServiceLifetime.Scoped);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStatementImportProfile, SyntheticStatementProfile>();
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType?.Name is "VaultExpirationService" or "UserAuditCleanupService").ToList())
                services.Remove(descriptor);
            configureServices?.Invoke(services);
        });
    }

    public string Token(ApplicationUser user) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer: "EastSeat.Agenti", audience: "EastSeat.Agenti.Android", claims: Claims(user),
        expires: DateTime.UtcNow.AddMinutes(30),
        signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey)), SecurityAlgorithms.HmacSha256)));

    public (string Name, string Value) Cookie(ApplicationUser user)
    {
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity(Claims(user), IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            new AuthenticationProperties { IssuedUtc = Clock.GetUtcNow(), ExpiresUtc = Clock.GetUtcNow().AddMinutes(30) },
            IdentityConstants.ApplicationScheme);
        return (options.Cookie.Name!, options.TicketDataFormat.Protect(ticket));
    }

    private static IEnumerable<Claim> Claims(ApplicationUser user) =>
    [
        new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.UserName ?? user.Id),
        new(ClaimTypes.Role, user.Role.ToString()), new("BranchId", user.BranchId?.ToString() ?? ""),
        new("AspNet.Identity.SecurityStamp", user.SecurityStamp ?? "")
    ];
}

public sealed class BankingTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class SyntheticStatementProfile : IStatementImportProfile
{
    public const string ProfileKey = "synthetic-test-json-not-bank-format";
    public string Key => ProfileKey;
    public string BankIdentifier => "synthetic-test-bank";
    public string DisplayName => "Synthetic fixture only - not a production bank format";
    public int Version => 1;
    public bool IsVerified => true;
    public BankBalanceBasis BalanceBasis => BankBalanceBasis.Ledger;
    public IReadOnlyList<string> SupportedContentTypes => ["text/csv"];
    public string? GetReliableIdentity(ParsedStatementEntry entry) => entry.TransactionNumber;
    public async Task<ParsedStatement> ParseAsync(Stream source, string contentType, StatementAccountContext account, CancellationToken cancellationToken) =>
        await JsonSerializer.DeserializeAsync<ParsedStatement>(source, cancellationToken: cancellationToken)
        ?? throw new StatementParsingException("The synthetic test fixture is empty.");
}
