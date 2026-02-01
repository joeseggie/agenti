using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EastSeat.Agenti.IntegrationTests.Infrastructure;

/// <summary>
/// Database fixture for integration tests using Testcontainers.
/// Provides a real PostgreSQL instance for testing.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Credentials for ephemeral test container - not persisted
        var testPassword = Environment.GetEnvironmentVariable("TEST_DB_PASSWORD") ?? "test_container_pwd_" + Guid.NewGuid().ToString("N")[..8];
        
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithDatabase("agenti_test")
            .WithUsername("testuser")
            .WithPassword(testPassword)
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Run migrations
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
