using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Security.Claims;
using System.Threading.RateLimiting;
using EastSeat.Agenti.Mcp.Authentication;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Mcp;

public sealed class McpProgram
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args, ContentRootPath = AppContext.BaseDirectory
        });
        var config = builder.Configuration.GetSection(McpServerConfig.SectionName).Get<McpServerConfig>() ?? new();
        Validator.ValidateObject(config, new ValidationContext(config), validateAllProperties: true);
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
            throw new InvalidOperationException("Set AGENTI__ConnectionString to the dedicated read-only database connection.");
        var allowedHosts = builder.Configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Contains('*'))
            throw new InvalidOperationException("Configure AllowedHosts with the MCP server's explicit hostnames.");

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(_ => new ReadOnlyDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(config.ConnectionString,
                npgsql => npgsql.CommandTimeout(config.CommandTimeoutSeconds)).Options));
        builder.Services.AddScoped<McpTokenValidator>();
        builder.Services.AddScoped(provider =>
            provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[typeof(McpCaller)] as McpCaller
                ?? throw new UnauthorizedAccessException("An authenticated MCP caller is required."));
        builder.Services.AddAuthentication(McpTokenAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, McpTokenAuthenticationHandler>(McpTokenAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("McpAccess", policy =>
            policy.RequireAuthenticatedUser().RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString())
                .RequireAssertion(context => context.Resource is HttpContext http &&
                    http.Items[typeof(McpCaller)] is McpCaller caller &&
                    (caller.Role == UserRole.Admin || caller.BranchId is > 0))));
        builder.Services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.Request.Path.StartsWithSegments("/mcp")
                    ? RateLimitPartition.GetConcurrencyLimiter("mcp", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = config.MaxConcurrentRequests, QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter("health"));
            options.AddPolicy("mcp-user", http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException(),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
                    ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "1";
                return ValueTask.CompletedTask;
            };
        });
        builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "agenti-mcp", Version = "2.0.0" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithToolsFromAssembly(typeof(McpProgram).Assembly);

        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.Path.StartsWithSegments("/mcp") && http.Request.Headers.TryGetValue("Origin", out var origins) &&
                (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) ||
                 !origin.Authority.Equals(http.Request.Host.Value, StringComparison.OrdinalIgnoreCase) ||
                 (origin.Scheme != Uri.UriSchemeHttps && !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            try { await next(); }
            catch (DbException ex) when (!http.Response.HasStarted)
            {
                app.Logger.LogError(ex, "MCP database is unavailable");
                http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcp("/mcp").RequireAuthorization("McpAccess").RequireRateLimiting("mcp-user");
        app.MapGet("/health/live", () => Results.Ok()).AllowAnonymous().DisableRateLimiting();
        app.MapGet("/health/ready", async (ReadOnlyDbContext db, CancellationToken cancellationToken) =>
        {
            // Validate schema and SELECT grants without returning operational or credential data.
            await db.Database.ExecuteSqlRawAsync("""
                SELECT t.*, b.*, a.*, w.*, wt.*, s.*, c.*, d.*, x.*, v.*, vt.*,
                       u."Id", u."FirstName", u."LastName", u."SecurityStamp", u."IsActive",
                       u."IsDeleted", u."Role", u."BranchId", u."LockoutEnabled", u."LockoutEnd"
                FROM "McpAccessTokens" t, "Branches" b, "Agents" a, "Wallets" w, "WalletTypes" wt,
                     "CashSessions" s, "CashCounts" c, "Discrepancies" d, "Transactions" x,
                     "Vaults" v, "VaultTransactions" vt, "AspNetUsers" u
                WHERE FALSE
                """, cancellationToken);
            return Results.Ok();
        }).AllowAnonymous().DisableRateLimiting();
        await app.RunAsync();
    }
}
