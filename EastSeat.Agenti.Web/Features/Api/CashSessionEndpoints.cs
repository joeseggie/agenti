using EastSeat.Agenti.Web.Features.CashSessions;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for cash session management.
/// </summary>
public static class CashSessionEndpoints
{
    public static RouteGroupBuilder MapCashSessionsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (ICashSessionService cashSessionService) =>
        {
            var sessions = await cashSessionService.GetCashSessionsAsync();
            return Results.Ok(ApiResponse<List<CashSessionListItemDto>>.Ok(sessions));
        })
        .RequireAuthorization()
        .WithName("GetCashSessions")
        .WithSummary("Get all cash sessions");

        group.MapGet("/{sessionId:long}", async (
            long sessionId,
            ICashSessionService cashSessionService) =>
        {
            var session = await cashSessionService.GetCashSessionDetailAsync(sessionId);
            return session is null
                ? Results.NotFound(ApiResponse<CashSessionDetailDto>.Fail("Cash session not found."))
                : Results.Ok(ApiResponse<CashSessionDetailDto>.Ok(session));
        })
        .RequireAuthorization()
        .WithName("GetCashSession")
        .WithSummary("Get cash session details by ID");

        group.MapPost("/{sessionId:long}/close", async (
            long sessionId,
            ICashSessionService cashSessionService) =>
        {
            var (success, error) = await cashSessionService.CloseSessionAsync(sessionId);
            return success
                ? Results.Ok(ApiResponse<string>.Ok("Session closed successfully."))
                : Results.BadRequest(ApiResponse<string>.Fail(error ?? "Failed to close session."));
        })
        .RequireAuthorization()
        .WithName("CloseCashSession")
        .WithSummary("Close a cash session");

        return group;
    }
}
