using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashSessions;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EastSeat.Agenti.UnitTests.Services;

public sealed class HistoryPagingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly HistoryContext _db;
    private readonly BankingScenario _scenario;

    public HistoryPagingTests()
    {
        _connection.Open();
        _db = CreateContext();
        _db.Database.EnsureCreated();
        _scenario = new(_db);
    }

    [Fact]
    public async Task Sessions_PageBeforeMapping_PreservesSummariesAndBranchDateStatusFilters()
    {
        _scenario.AddApprovedOpening();
        for (var index = 1; index <= 12; index++)
            _db.CashSessions.Add(new CashSession
            {
                BranchId = _scenario.Session.BranchId, SessionDate = _scenario.Session.SessionDate.AddDays(-index),
                OpenedAt = BankingScenario.Now.AddDays(-index), Status = CashSessionStatus.Closed
            });
        var other = new Branch { Name = "Other branch", CreatedAt = BankingScenario.Now };
        _db.Branches.Add(other);
        _db.CashSessions.Add(new CashSession
        {
            Branch = other, SessionDate = _scenario.Session.SessionDate,
            OpenedAt = BankingScenario.Now, Status = CashSessionStatus.Open
        });
        await _db.SaveChangesAsync();
        var service = new CashSessionService(_db);

        var first = await service.GetCashSessionsPageAsync(new(0, 10), _scenario.Agent.BranchId);
        var second = await service.GetCashSessionsPageAsync(new(1, 10), _scenario.Agent.BranchId);
        Assert.Equal(13, first.TotalCount);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(s => s.Id).Intersect(second.Items.Select(s => s.Id)));
        var current = first.Items[0];
        Assert.Equal(_scenario.Session.Id, current.Id);
        Assert.Equal(1, current.AgentCount);
        Assert.Equal(1_100_000m, current.TotalOpeningAmount);
        Assert.Null(current.TotalClosingAmount);
        var filtered = await service.GetCashSessionsPageAsync(new(0, 10), _scenario.Agent.BranchId,
            CashSessionStatus.Closed, _scenario.Session.SessionDate.AddDays(-3), _scenario.Session.SessionDate.AddDays(-1));
        Assert.Equal(3, filtered.TotalCount);
        Assert.All(filtered.Items, s => Assert.Equal(CashSessionStatus.Closed, s.Status));
        Assert.Empty((await service.GetCashSessionsPageAsync(new(5, 10), _scenario.Agent.BranchId)).Items);
    }

    [Fact]
    public async Task Sessions_ReversedDateRange_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new CashSessionService(_db)
            .GetCashSessionsPageAsync(new(0, 10), from: new(2026, 11, 3), to: new(2026, 11, 2)));
    }

    [Fact]
    public async Task Vault_FiltersAndPagesHistory_WithoutHidingOlderPendingRequestsOrOtherBranchIsolation()
    {
        var vault = await _db.Vaults.SingleAsync(v => v.BranchId == _scenario.Agent.BranchId);
        var other = new Branch { Name = "Other branch", CreatedAt = BankingScenario.Now };
        _db.Branches.Add(other);
        await _db.SaveChangesAsync();
        var otherVault = await _db.Vaults.SingleAsync(v => v.BranchId == other.Id);
        for (var index = 0; index < 105; index++)
            _db.VaultTransactions.Add(new VaultTransaction
            {
                VaultId = vault.Id, CreatedByUserId = _scenario.Admin.Id,
                CreatedAt = BankingScenario.Now.AddMinutes(-index), Amount = 100m,
                Type = VaultTransactionType.ManualDeposit,
                Status = index == 104 ? VaultTransactionStatus.Pending : VaultTransactionStatus.Completed,
                ExpiresAt = index == 104 ? BankingScenario.Now.AddHours(1) : null
            });
        _db.VaultTransactions.Add(new VaultTransaction
        {
            VaultId = otherVault.Id, CreatedByUserId = _scenario.Admin.Id, CreatedAt = BankingScenario.Now,
            Amount = 500m, Type = VaultTransactionType.ManualDeposit, Status = VaultTransactionStatus.Pending
        });
        await _db.SaveChangesAsync();
        var service = new VaultService(_db);

        var first = await service.GetHistoryPageAsync(_scenario.Session.BranchId, new(0, 10), VaultTransactionStatus.Completed);
        var second = await service.GetHistoryPageAsync(_scenario.Session.BranchId, new(1, 10), VaultTransactionStatus.Completed);
        Assert.Equal(104, first.TotalCount);
        Assert.Equal(10, first.Items.Count);
        Assert.Empty(first.Items.Select(t => t.Id).Intersect(second.Items.Select(t => t.Id)));
        var pending = Assert.Single(await service.GetPendingTransactionsAsync(_scenario.Session.BranchId));
        Assert.Equal(BankingScenario.Now.AddMinutes(-104), pending.CreatedAt);
        var filtered = await service.GetHistoryPageAsync(_scenario.Session.BranchId, new(0, 10),
            VaultTransactionStatus.Pending, VaultTransactionType.ManualDeposit);
        Assert.Equal(pending.Id, Assert.Single(filtered.Items).Id);
        Assert.Empty((await service.GetHistoryPageAsync(_scenario.Session.BranchId, new(0, 10),
            type: VaultTransactionType.ManualWithdrawal)).Items);
    }

    [Fact]
    public async Task Notifications_FilterBeforePaging_StayRecipientScopedAndStableForEqualTimestamps()
    {
        for (var index = 0; index < 25; index++)
            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(), RecipientUserId = _scenario.Admin.Id,
                CreatedAt = BankingScenario.Now, Message = $"Synthetic message {index}",
                Title = index % 2 == 0 ? "Review required" : "Update", IsRead = index % 2 != 0
            });
        _db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = _scenario.User.Id,
            CreatedAt = BankingScenario.Now, Message = "Review required"
        });
        await _db.SaveChangesAsync();
        var service = new NotificationService(new ContextFactory(this));

        var first = await service.GetNotificationsPageAsync(_scenario.Admin.Id, new(0, 10), false, " REVIEW ");
        var second = await service.GetNotificationsPageAsync(_scenario.Admin.Id, new(1, 10), false, "review");
        Assert.Equal(13, first.TotalCount);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(n => n.Id).Intersect(second.Items.Select(n => n.Id)));
        Assert.All(first.Items.Concat(second.Items), n => Assert.False(n.IsRead));
        Assert.Empty((await service.GetNotificationsPageAsync(_scenario.Admin.Id, new(0, 10), search: "no match")).Items);
        Assert.Single((await service.GetNotificationsPageAsync(_scenario.User.Id, new(0, 10))).Items);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    [InlineData(int.MaxValue, 10)]
    public void InvalidPaging_IsRejectedBeforeQuerying(int page, int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(page, size));

    private HistoryContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite(_connection).Options);

    public void Dispose()
    {
        _scenario.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class ContextFactory(HistoryPagingTests owner) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => owner.CreateContext();
    }

    private sealed class HistoryContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // SQLite needs an instant-preserving representation to test timestamp ordering.
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
                .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(
                    value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero)));
        }
    }
}
