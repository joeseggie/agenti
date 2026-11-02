using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.UnitTests.Helpers;

internal sealed class BankingTestContext() : BankingScenario(CreateContext(), ownsContext: true)
{
    private static ApplicationDbContext CreateContext()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:").Options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
