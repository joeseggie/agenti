using System.Security.Claims;
using EastSeat.Agenti.Web.Features.WalletAdjustments;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for wallet adjustment operations.
/// </summary>
public static class WalletAdjustmentEndpoints
{
    public static RouteGroupBuilder MapWalletAdjustmentsApi(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            WalletAdjustmentFormModel form,
            ClaimsPrincipal principal,
            IWalletAdjustmentService walletAdjustmentService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await walletAdjustmentService.RecordAdjustmentAsync(userId, form);
            return result.Success
                ? Results.Ok(ApiResponse<WalletAdjustmentSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<WalletAdjustmentSaveResult>.Fail(result.ErrorMessage ?? "Failed to record adjustment."));
        })
        .RequireAuthorization()
        .WithName("RecordWalletAdjustment")
        .WithSummary("Record a debit-only wallet adjustment during an active session");

        group.MapGet("/current", async (
            ClaimsPrincipal principal,
            IWalletAdjustmentService walletAdjustmentService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var adjustments = await walletAdjustmentService.GetAdjustmentsForAgentAsync(userId);
            return Results.Ok(ApiResponse<List<WalletAdjustmentDto>>.Ok(adjustments));
        })
        .RequireAuthorization()
        .WithName("GetCurrentSessionAdjustments")
        .WithSummary("Get wallet adjustments for the authenticated agent's current session");

        group.MapGet("/session/{sessionId:long}", async (
            long sessionId,
            ClaimsPrincipal principal,
            IWalletAdjustmentService walletAdjustmentService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var adjustments = await walletAdjustmentService.GetAdjustmentsForSessionAsync(sessionId);
            return Results.Ok(ApiResponse<List<WalletAdjustmentDto>>.Ok(adjustments));
        })
        .RequireAuthorization("CashCountApprove")
        .WithName("GetSessionAdjustments")
        .WithSummary("Get all wallet adjustments for a session (admin/supervisor only)");

        group.MapPost("/{adjustmentId:long}/approve", async (
            long adjustmentId,
            ClaimsPrincipal principal,
            IWalletAdjustmentService walletAdjustmentService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await walletAdjustmentService.ApproveAdjustmentAsync(userId, adjustmentId);
            return result.Success
                ? Results.Ok(ApiResponse<WalletAdjustmentSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<WalletAdjustmentSaveResult>.Fail(result.ErrorMessage ?? "Failed to approve adjustment."));
        })
        .RequireAuthorization("CashCountApprove")
        .WithName("ApproveWalletAdjustment")
        .WithSummary("Approve a pending wallet adjustment (admin/supervisor only)");

        group.MapPost("/{adjustmentId:long}/reject", async (
            long adjustmentId,
            RejectAdjustmentRequest request,
            ClaimsPrincipal principal,
            IWalletAdjustmentService walletAdjustmentService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await walletAdjustmentService.RejectAdjustmentAsync(userId, adjustmentId, request.Reason);
            return result.Success
                ? Results.Ok(ApiResponse<WalletAdjustmentSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<WalletAdjustmentSaveResult>.Fail(result.ErrorMessage ?? "Failed to reject adjustment."));
        })
        .RequireAuthorization("CashCountApprove")
        .WithName("RejectWalletAdjustment")
        .WithSummary("Reject a pending wallet adjustment (admin/supervisor only)");

        return group;
    }

    private static string? GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
}
