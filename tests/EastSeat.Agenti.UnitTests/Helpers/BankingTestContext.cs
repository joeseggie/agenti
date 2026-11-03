using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.Helpers;

internal sealed class BankingTestContext() : BankingScenario(CreateContext(), ownsContext: true)
{
    private static ApplicationDbContext CreateContext()
    {
        var db = new SqliteBankingDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:").Options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class SqliteBankingDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // SQLite cannot compare DateTimeOffset values; preserve instant ordering for reservation cutoffs.
            builder.Entity<CustomerMovement>().Property(m => m.OccurredAt)
                .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        }
    }
}
