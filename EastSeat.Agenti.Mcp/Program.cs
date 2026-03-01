using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration from appsettings.json and environment variables (AGENTI__*)
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Send logs to stderr so stdout stays clean for MCP protocol messages
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Bind configuration
var config = new McpServerConfig();
builder.Configuration.GetSection(McpServerConfig.SectionName).Bind(config);
builder.Services.AddSingleton(config);

// Validate configuration
if (string.IsNullOrWhiteSpace(config.ConnectionString))
{
    Console.Error.WriteLine("ERROR: Agenti connection string is not configured.");
    Console.Error.WriteLine("Set AGENTI__ConnectionString environment variable or configure in appsettings.json.");
    Environment.Exit(1);
}

// Enforce branch isolation safety model:
// When the server is not allowed to query all branches, a valid BranchId must be configured.
if (!config.CanQueryAllBranches && (config.BranchId == null || config.BranchId <= 0))
{
    Console.Error.WriteLine("ERROR: Invalid branch configuration for Agenti MCP server.");
    Console.Error.WriteLine("When CanQueryAllBranches is false, a valid BranchId (> 0) must be configured.");
    Console.Error.WriteLine("Set AGENTI__McpServer__BranchId (or the appropriate config key) to a positive integer.");
    Environment.Exit(1);
}
// Configure read-only database context with connection string and Npgsql settings
ReadOnlyDbContext.Configure(config.ConnectionString, config.CommandTimeoutSeconds, maxRetryCount: 2);

// Register read-only database context
builder.Services.AddDbContext<ReadOnlyDbContext>(options =>
{
    options.UseNpgsql(config.ConnectionString, npgsql =>
    {
        npgsql.CommandTimeout(config.CommandTimeoutSeconds);
        npgsql.EnableRetryOnFailure(maxRetryCount: 2);
    });
});

// Register MCP server with stdio transport
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "agenti-mcp",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
