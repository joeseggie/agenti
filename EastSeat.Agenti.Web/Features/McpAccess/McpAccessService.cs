using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Security;
using EastSeat.Agenti.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.Web.Features.McpAccess;

public sealed class McpAccessService(
    ApplicationDbContext db, TimeProvider clock, IOptions<IdentityOptions> identityOptions) : IMcpAccessService
{
    public const string ManagementPolicy = "McpTokenManagement";
    public const int PageSize = 25;

    public async Task<CreatedMcpToken> CreateAsync(
        ClaimsPrincipal actor, CreateMcpTokenRequest request, CancellationToken cancellationToken = default)
    {
        var user = await RequireActorAsync(actor, cancellationToken);
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        if (string.IsNullOrWhiteSpace(request.Name) || request.RequestId == Guid.Empty)
            throw new ValidationException("A token name and a valid submission identifier are required.");

        if (await db.McpAccessTokens.AnyAsync(t => t.UserId == user.Id && t.RequestId == request.RequestId, cancellationToken))
            throw new ValidationException("This request was already submitted. Refresh the list and revoke the previous token if its secret was not saved.");

        var secret = McpTokenSecret.Create();
        if (!McpTokenSecret.TryHash(secret, out var hash))
            throw new InvalidOperationException("Could not generate an MCP credential.");
        if (await db.McpAccessTokens.AnyAsync(t => t.TokenHash == hash, cancellationToken))
            throw new InvalidOperationException("Could not generate a unique MCP credential. Please try again.");

        var now = clock.GetUtcNow();
        var token = new McpAccessToken
        {
            UserId = user.Id, RequestId = request.RequestId, Name = request.Name.Trim(),
            TokenHash = hash, SecurityStamp = user.SecurityStamp!,
            CreatedAt = now, ExpiresAt = now.AddDays(request.LifetimeDays)
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        UserAuditLog? audit = null;
        try
        {
            db.McpAccessTokens.Add(token);
            await db.SaveChangesAsync(cancellationToken);
            audit = AddAudit(token, user.Id, UserAuditAction.McpTokenCreated, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            // A failed transaction must not leave pending writes in the Blazor circuit's context.
            db.Entry(token).State = EntityState.Detached;
            if (audit is not null) db.Entry(audit).State = EntityState.Detached;
        }
        return new CreatedMcpToken { Id = token.Id, Secret = secret, ExpiresAt = token.ExpiresAt };
    }

    public async Task<McpTokenPage> ListAsync(
        ClaimsPrincipal actor, bool allUsers = false, int page = 0, CancellationToken cancellationToken = default)
    {
        var user = await RequireActorAsync(actor, cancellationToken);
        if (allUsers && user.Role != UserRole.Admin)
            throw new UnauthorizedAccessException("Only an Admin can view other users' MCP tokens.");
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, int.MaxValue / PageSize);

        var query = db.McpAccessTokens.AsNoTracking().Where(t => allUsers || t.UserId == user.Id);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(t => t.Id).Skip(page * PageSize).Take(PageSize)
            .Select(t => new McpTokenListItem(
                t.Id, t.UserId,
                db.Users.Where(u => u.Id == t.UserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "Deleted user",
                t.Name, t.CreatedAt, t.ExpiresAt, t.RevokedAt))
            .ToListAsync(cancellationToken);
        return new McpTokenPage(items, total);
    }

    public async Task RevokeAsync(ClaimsPrincipal actor, long tokenId, CancellationToken cancellationToken = default)
    {
        var user = await RequireActorAsync(actor, cancellationToken);
        var token = await db.McpAccessTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tokenId && (t.UserId == user.Id || user.Role == UserRole.Admin), cancellationToken)
            ?? throw new UnauthorizedAccessException("This token is unavailable or you cannot revoke it.");
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        UserAuditLog? audit = null;
        try
        {
            var changed = await db.McpAccessTokens.Where(t => t.Id == token.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(t => t.RevokedByUserId, user.Id), cancellationToken);
            if (changed != 0)
            {
                audit = AddAudit(token, user.Id, UserAuditAction.McpTokenRevoked, now);
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (audit is not null) db.Entry(audit).State = EntityState.Detached;
        }
    }

    private async Task<ApplicationUser> RequireActorAsync(ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var claims = identityOptions.Value.ClaimsIdentity;
        var identity = actor.Identities.FirstOrDefault(i =>
            i.IsAuthenticated && i.AuthenticationType == IdentityConstants.ApplicationScheme);
        var userId = identity?.FindFirst(claims.UserIdClaimType)?.Value;
        var stamp = identity?.FindFirst(claims.SecurityStampClaimType)?.Value;
        var user = userId is null ? null : await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || user.IsDeleted ||
            user.Role is not (UserRole.Admin or UserRole.Supervisor) ||
            string.IsNullOrEmpty(stamp) || string.IsNullOrEmpty(user.SecurityStamp) || stamp != user.SecurityStamp ||
            (user.LockoutEnabled && user.LockoutEnd > clock.GetUtcNow()))
            throw new UnauthorizedAccessException("Sign in with an active Admin or Supervisor account to manage MCP tokens.");
        if (user.Role == UserRole.Supervisor &&
            (user.BranchId is null or <= 0 || !await db.Branches.AnyAsync(b => b.Id == user.BranchId, cancellationToken)))
            throw new UnauthorizedAccessException("A Supervisor must have an assigned branch to use MCP.");
        return user;
    }

    private UserAuditLog AddAudit(McpAccessToken token, string actorId, UserAuditAction action, DateTimeOffset now)
    {
        var audit = new UserAuditLog
        {
            UserId = token.UserId, PerformedByUserId = actorId, Action = action, PerformedAt = now,
            NewValue = $"MCP token #{token.Id}: {token.Name}; expires {token.ExpiresAt:O}"
        };
        db.UserAuditLogs.Add(audit);
        return audit;
    }
}
