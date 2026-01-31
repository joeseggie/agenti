using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Respawn;

namespace EastSeat.Agenti.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for integration tests with database cleanup.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    protected readonly DatabaseFixture Fixture;
    protected ApplicationDbContext DbContext = null!;
    private Respawner _respawner = null!;

    protected IntegrationTestBase(DatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        DbContext = Fixture.CreateContext();

        // Initialize Respawner for database cleanup
        await using var connection = DbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public" },
            TablesToIgnore = new Respawn.Graph.Table[]
            {
                new("__EFMigrationsHistory"),
                new("AspNetRoles") // Preserve seed data
            }
        });
    }

    public async Task DisposeAsync()
    {
        // Clean database after each test
        await using var connection = DbContext.Database.GetDbConnection();
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);

        await DbContext.DisposeAsync();
    }
}
