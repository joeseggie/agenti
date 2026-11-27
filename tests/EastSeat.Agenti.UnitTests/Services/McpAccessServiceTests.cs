using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Security;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.McpAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.UnitTests.Services;

public sealed class McpAccessServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FixedClock _clock = new();
    private readonly AuditFailure _auditFailure = new();
    private ApplicationDbContext _db = null!;
    private McpAccessService _service = null!;
    private ApplicationUser _admin = null!;
    private ApplicationUser _supervisor = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection).AddInterceptors(_auditFailure).Options);
        await _db.Database.EnsureCreatedAsync();
        var branch = new Branch { Name = "Synthetic branch" };
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();
        _admin = User("admin", UserRole.Admin, branch.Id);
        _supervisor = User("supervisor", UserRole.Supervisor, branch.Id);
        _db.Users.AddRange(_admin, _supervisor);
        await _db.SaveChangesAsync();
        _service = new McpAccessService(_db, _clock, Options.Create(new IdentityOptions()));
    }

    [Fact]
    public async Task Create_BindsOwner_StoresOnlyHash_AndAudits()
    {
        var created = await _service.CreateAsync(Principal(_admin), new() { Name = "Reporting client" });
        var saved = await _db.McpAccessTokens.SingleAsync();
        Assert.True(McpTokenSecret.TryHash(created.Secret, out var hash));
        Assert.Equal(hash, saved.TokenHash);
        Assert.False(created.Secret == saved.TokenHash);
        Assert.Equal(_admin.Id, saved.UserId);
        Assert.Equal(_admin.SecurityStamp, saved.SecurityStamp);
        Assert.Equal(_clock.GetUtcNow().AddDays(7), created.ExpiresAt);
        var audit = await _db.UserAuditLogs.SingleAsync();
        Assert.Equal(UserAuditAction.McpTokenCreated, audit.Action);
        Assert.False(audit.NewValue?.Contains(created.Secret, StringComparison.Ordinal) == true);
        var page = await _service.ListAsync(Principal(_admin));
        Assert.Single(page.Items);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(page).Contains(created.Secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Create_RejectsInvalidLifetime(int days)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateAsync(Principal(_admin), new() { Name = "Client", LifetimeDays = days }));
        Assert.Empty(await _db.McpAccessTokens.ToListAsync());
    }

    [Fact]
    public async Task Create_AllowsThirtyDays_AndRejectsReplayedSubmission()
    {
        var request = new CreateMcpTokenRequest { Name = "Background", LifetimeDays = 30 };
        var created = await _service.CreateAsync(Principal(_supervisor), request);
        Assert.Equal(_clock.GetUtcNow().AddDays(30), created.ExpiresAt);
        await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(Principal(_supervisor), request));
        Assert.Single(await _db.McpAccessTokens.ToListAsync());
    }

    [Fact]
    public async Task Management_RejectsBearerAndStaleCookie()
    {
        var cookie = Principal(_admin);
        var jwt = new ClaimsPrincipal(new ClaimsIdentity(cookie.Claims, "Bearer"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListAsync(jwt));
        _admin.SecurityStamp = Guid.NewGuid().ToString();
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListAsync(cookie));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("agent")]
    [InlineData("locked")]
    [InlineData("no-branch")]
    public async Task Management_RechecksDatabaseEligibility(string change)
    {
        var cookie = Principal(_supervisor);
        if (change == "inactive") _supervisor.IsActive = false;
        if (change == "deleted") _supervisor.IsDeleted = true;
        if (change == "agent") _supervisor.Role = UserRole.Agent;
        if (change == "locked")
        {
            _supervisor.LockoutEnabled = true;
            _supervisor.LockoutEnd = _clock.GetUtcNow().AddDays(1);
        }
        if (change == "no-branch") _supervisor.BranchId = null;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListAsync(cookie));
    }

    [Fact]
    public async Task Revoke_EnforcesOwnership_AdminCanRevokeOthers_AndIsIdempotent()
    {
        var adminToken = await _service.CreateAsync(Principal(_admin), new() { Name = "Admin client" });
        var supervisorToken = await _service.CreateAsync(Principal(_supervisor), new() { Name = "Supervisor client" });
        Assert.Single((await _service.ListAsync(Principal(_supervisor))).Items);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListAsync(Principal(_supervisor), allUsers: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RevokeAsync(Principal(_supervisor), adminToken.Id));
        Assert.Equal(2, (await _service.ListAsync(Principal(_admin), allUsers: true)).TotalCount);
        await _service.RevokeAsync(Principal(_admin), supervisorToken.Id);
        await _service.RevokeAsync(Principal(_admin), supervisorToken.Id);
        var revoked = await _db.McpAccessTokens.AsNoTracking().SingleAsync(t => t.Id == supervisorToken.Id);
        Assert.Equal(_clock.GetUtcNow(), revoked.RevokedAt);
        Assert.Equal(_admin.Id, revoked.RevokedByUserId);
        Assert.Single(await _db.UserAuditLogs.Where(a => a.Action == UserAuditAction.McpTokenRevoked).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditFailure_RollsBackLifecycleChange_AndAllowsCleanRecovery(bool revoke)
    {
        var actor = Principal(_admin);
        var request = new CreateMcpTokenRequest { Name = "Recoverable" };
        var token = revoke ? await _service.CreateAsync(actor, request) : null;
        _auditFailure.FailNext = true;
        if (revoke)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RevokeAsync(actor, token!.Id));
            Assert.Null((await _db.McpAccessTokens.AsNoTracking().SingleAsync()).RevokedAt);
            await _service.RevokeAsync(actor, token!.Id);
            Assert.Single(await _db.UserAuditLogs.Where(a => a.Action == UserAuditAction.McpTokenRevoked).ToListAsync());
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(actor, request));
            Assert.Empty(await _db.McpAccessTokens.ToListAsync());
            await _service.CreateAsync(actor, request);
        }
        Assert.Single(await _db.UserAuditLogs.Where(a => a.Action == UserAuditAction.McpTokenCreated).ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ApplicationUser User(string id, UserRole role, long branchId) => new()
    {
        Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FirstName = "Synthetic", LastName = id,
        Role = role, BranchId = branchId, SecurityStamp = Guid.NewGuid().ToString()
    };

    private static ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(
        [new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Role, user.Role.ToString()),
            new("AspNet.Identity.SecurityStamp", user.SecurityStamp!)], IdentityConstants.ApplicationScheme));

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class AuditFailure : SaveChangesInterceptor
    {
        public bool FailNext { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailNext && eventData.Context!.ChangeTracker.Entries<UserAuditLog>().Any(e => e.State == EntityState.Added))
            {
                FailNext = false;
                throw new InvalidOperationException("Synthetic audit failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
