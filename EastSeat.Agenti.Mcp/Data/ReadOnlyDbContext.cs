using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Mcp.Data;

/// <summary>
/// Read-only database context for the MCP server.
/// Inherits all entity configurations from ApplicationDbContext but blocks all write operations.
/// Uses a static connection string set at startup to avoid internal API usage.
/// </summary>
public class ReadOnlyDbContext : ApplicationDbContext
{
    private static string _connectionString = string.Empty;
    private static int _commandTimeout = 30;
    private static int _maxRetryCount = 2;

    /// <summary>
    /// Configure the connection parameters once at startup (called from Program.cs).
    /// </summary>
    public static void Configure(string connectionString, int commandTimeout = 30, int maxRetryCount = 2)
    {
        _connectionString = connectionString;
        _commandTimeout = commandTimeout;
        _maxRetryCount = maxRetryCount;
    }

    public ReadOnlyDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
    }
    public override int SaveChanges()
    {
        throw new InvalidOperationException(
            "This is a read-only database context. Write operations are not permitted.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw new InvalidOperationException(
            "This is a read-only database context. Write operations are not permitted.");
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "This is a read-only database context. Write operations are not permitted.");
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "This is a read-only database context. Write operations are not permitted.");
    }
}
