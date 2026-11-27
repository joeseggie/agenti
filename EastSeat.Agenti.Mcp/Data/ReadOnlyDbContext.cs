using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Mcp.Data;

/// <summary>
/// Read-only database context for the MCP server.
/// Inherits all entity configurations from ApplicationDbContext but blocks all write operations.
/// </summary>
public class ReadOnlyDbContext : ApplicationDbContext
{
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
