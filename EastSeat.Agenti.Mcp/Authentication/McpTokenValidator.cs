using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Mcp.Authentication;

public sealed class McpTokenValidator(ReadOnlyDbContext db, TimeProvider clock)
{
    public async Task<McpCaller?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (!McpTokenSecret.TryHash(token, out var hash))
            return null;
        var credential = await (
            from t in db.McpAccessTokens
            join u in db.Users on t.UserId equals u.Id
            where t.TokenHash == hash
            select new
            {
                t.Id, t.UserId, t.CreatedAt, t.ExpiresAt, t.RevokedAt, t.SecurityStamp,
                UserStamp = u.SecurityStamp, u.IsActive, u.IsDeleted, u.Role, u.BranchId,
                u.LockoutEnabled, u.LockoutEnd,
                HasBranch = db.Branches.Any(b => b.Id == u.BranchId)
            }).SingleOrDefaultAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (credential is null || credential.RevokedAt.HasValue || credential.ExpiresAt <= now ||
            credential.ExpiresAt <= credential.CreatedAt ||
            credential.ExpiresAt - credential.CreatedAt > TimeSpan.FromDays(McpTokenSecret.MaxLifetimeDays) ||
            !credential.IsActive || credential.IsDeleted ||
            string.IsNullOrEmpty(credential.UserStamp) || credential.SecurityStamp != credential.UserStamp ||
            (credential.LockoutEnabled && credential.LockoutEnd > now))
            return null;
        return new McpCaller(credential.UserId, credential.Role,
            credential.HasBranch ? credential.BranchId : null, credential.Id);
    }
}
