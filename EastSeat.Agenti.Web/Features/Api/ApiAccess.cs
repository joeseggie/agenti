using System.Security.Claims;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Api;

internal static class ApiAccess
{
    public static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? throw new UnauthorizedAccessException("An authenticated user is required.");

    public static Task<BankingActor> ActorAsync(ClaimsPrincipal principal, IBankingAccessService access) =>
        access.GetActorAsync(UserId(principal));

    public static long OwnAgentId(BankingActor actor) =>
        actor.AgentId ?? throw new UnauthorizedAccessException("An active agent assignment is required.");

    public static long? AgentFilter(BankingActor actor) =>
        actor.Role == UserRole.Agent ? OwnAgentId(actor) : null;

    public static void RequireBranch(BankingActor actor, long branchId)
    {
        if (actor.BranchId != branchId)
            throw new UnauthorizedAccessException("You do not have access to this branch.");
    }

    public static async Task RequireAgentAsync(BankingActor actor, ApplicationDbContext db, long agentId)
    {
        var ownAgentId = AgentFilter(actor);
        if (!await db.Agents.AnyAsync(a => a.Id == agentId && a.BranchId == actor.BranchId &&
                (!ownAgentId.HasValue || a.Id == ownAgentId)))
            throw new UnauthorizedAccessException("You do not have access to this agent.");
    }

    public static async Task RequireSessionAsync(
        BankingActor actor, ApplicationDbContext db, long sessionId, bool requireParticipation = true)
    {
        var ownAgentId = requireParticipation ? AgentFilter(actor) : null;
        if (!await db.CashSessions.AnyAsync(s => s.Id == sessionId && s.BranchId == actor.BranchId &&
                (!ownAgentId.HasValue || s.CashCounts.Any(c => c.AgentId == ownAgentId))))
            throw new UnauthorizedAccessException("You do not have access to this session.");
    }

    public static async Task RequireCountAsync(
        BankingActor actor, ApplicationDbContext db, long cashCountId, bool ownCount = false)
    {
        var agentId = ownCount ? OwnAgentId(actor) : AgentFilter(actor);
        if (!await db.CashCounts.AnyAsync(c => c.Id == cashCountId &&
                c.CashSession!.BranchId == actor.BranchId && (!agentId.HasValue || c.AgentId == agentId)))
            throw new UnauthorizedAccessException("You do not have access to this cash count.");
    }

    public static async Task RequireVaultTransactionAsync(
        BankingActor actor, ApplicationDbContext db, long transactionId)
    {
        if (!await db.VaultTransactions.AnyAsync(t => t.Id == transactionId &&
                t.Vault!.BranchId == actor.BranchId))
            throw new UnauthorizedAccessException("You do not have access to this vault transaction.");
    }
}
