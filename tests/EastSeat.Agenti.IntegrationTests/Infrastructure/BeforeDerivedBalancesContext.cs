using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.IntegrationTests.Infrastructure;

// Seeds historical schemas without asking EF to insert columns introduced by the new migration.
internal sealed class BeforeDerivedBalancesContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<BankAccount>().Ignore(a => a.AllowMissingRunningBalances);
        builder.Entity<DailyBankStatement>().Ignore(s => s.BalanceSource);
        var statement = builder.Entity<DailyBankStatement>().Metadata;
        foreach (var relationship in statement.GetForeignKeys()
            .Where(fk => fk.Properties.Any(p => p.Name == nameof(DailyBankStatement.OpeningCountId))).ToList())
            statement.RemoveForeignKey(relationship);
        builder.Entity<DailyBankStatement>().Ignore(s => s.OpeningCountId);
    }
}
