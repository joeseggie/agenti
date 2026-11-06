using System.Security.Claims;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CashSessions;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for cash session management.
/// </summary>
public static class CashSessionEndpoints
{
    public static RouteGroupBuilder MapCashSessionsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (long? branchId, ClaimsPrincipal principal,
            IBankingAccessService access, ICashSessionService cashSessionService) =>
        {
            var actor = await ApiAccess.ActorAsync(principal, access);
            ApiAccess.RequireBranch(actor, branchId ?? actor.BranchId);
            var sessions = await cashSessionService.GetCashSessionsAsync(actor.BranchId, ApiAccess.AgentFilter(actor));
            return Results.Ok(ApiResponse<List<CashSessionListItemDto>>.Ok(sessions));
        })
        .RequireAuthorization()
        .WithName("GetCashSessions")
        .WithSummary("Get cash sessions in the user's branch, limited to own counts for agents");

        group.MapGet("/{sessionId:long}", async (
            long sessionId,
            ClaimsPrincipal principal,
            IBankingAccessService access,
            ApplicationDbContext db,
            ICashSessionService cashSessionService) =>
        {
            var actor = await ApiAccess.ActorAsync(principal, access);
            await ApiAccess.RequireSessionAsync(actor, db, sessionId);
            var session = await cashSessionService.GetCashSessionDetailAsync(sessionId, ApiAccess.AgentFilter(actor));
            return session is null
                ? Results.NotFound(ApiResponse<CashSessionDetailDto>.Fail("Cash session not found."))
                : Results.Ok(ApiResponse<CashSessionDetailDto>.Ok(session));
        })
        .RequireAuthorization()
        .WithName("GetCashSession")
        .WithSummary("Get cash session details by ID");

        group.MapPost("/{sessionId:long}/close", async (
            long sessionId,
            ClaimsPrincipal principal,
            IBankingAccessService access,
            ApplicationDbContext db,
            ICashSessionService cashSessionService) =>
        {
            await ApiAccess.RequireSessionAsync(await ApiAccess.ActorAsync(principal, access), db, sessionId);
            var (success, error) = await cashSessionService.CloseSessionAsync(sessionId);
            return success
                ? Results.Ok(ApiResponse<string>.Ok("Session closed successfully."))
                : Results.BadRequest(ApiResponse<string>.Fail(error ?? "Failed to close session."));
        })
        .RequireAuthorization("CashCountApproveApi")
        .WithName("CloseCashSession")
        .WithSummary("Close a cash session (requires all closing counts approved)");

        group.MapPost("/{sessionId:long}/close-agent/{agentId:long}", async (
            long sessionId,
            long agentId,
            ClaimsPrincipal principal,
            IBankingAccessService access,
            ApplicationDbContext db,
            ICashCountService cashCountService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var actor = await ApiAccess.ActorAsync(principal, access);
            await ApiAccess.RequireSessionAsync(actor, db, sessionId);
            await ApiAccess.RequireAgentAsync(actor, db, agentId);
            var result = await cashCountService.AdminCloseAgentSessionAsync(userId, sessionId, agentId);
            return result.Success
                ? Results.Ok(ApiResponse<CashCountSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<CashCountSaveResult>.Fail(result.ErrorMessage ?? "Failed to close agent session."));
        })
        .RequireAuthorization("VaultApproveApi")
        .WithName("CloseAgentSession")
        .WithSummary("Admin closes session for a specific agent (rule 22)");

        return group;
    }
}
