using System.Security.Claims;
using EastSeat.Agenti.Web.Features.Dashboard;
using EastSeat.Agenti.Shared.Services;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for the agent dashboard.
/// </summary>
public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            ClaimsPrincipal principal,
            IBankingAccessService access,
            IDashboardService dashboardService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var actor = await ApiAccess.ActorAsync(principal, access);
            var dashboard = await dashboardService.GetDashboardAsync(userId, actor.BranchId, ApiAccess.AgentFilter(actor));
            return Results.Ok(ApiResponse<DashboardViewModel>.Ok(dashboard));
        })
        .RequireAuthorization()
        .WithName("GetDashboard")
        .WithSummary("Get agent dashboard data including wallet balances and session status");

        return group;
    }
}
