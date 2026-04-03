using System.Security.Claims;
using EastSeat.Agenti.Web.Features.CashCounts;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for cash count operations.
/// </summary>
public static class CashCountEndpoints
{
    public static RouteGroupBuilder MapCashCountsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/current", async (
            ClaimsPrincipal principal,
            ICashCountService cashCountService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var session = await cashCountService.GetCurrentSessionAsync(userId);
            return Results.Ok(ApiResponse<CurrentSessionDto>.Ok(session));
        })
        .RequireAuthorization()
        .WithName("GetCurrentSession")
        .WithSummary("Get the current session status for the authenticated agent");

        group.MapGet("/initialize", async (
            bool isOpening,
            ClaimsPrincipal principal,
            ICashCountService cashCountService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var form = await cashCountService.InitializeCashCountFormAsync(userId, isOpening);
            return Results.Ok(ApiResponse<CashCountFormModel>.Ok(form));
        })
        .RequireAuthorization()
        .WithName("InitializeCashCount")
        .WithSummary("Initialize a cash count form for opening or closing count");

        group.MapPost("/", async (
            CashCountFormModel form,
            ClaimsPrincipal principal,
            ICashCountService cashCountService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await cashCountService.SaveCashCountAsync(userId, form);
            return result.Success
                ? Results.Ok(ApiResponse<CashCountSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<CashCountSaveResult>.Fail(result.ErrorMessage ?? "Failed to save cash count."));
        })
        .RequireAuthorization()
        .WithName("SaveCashCount")
        .WithSummary("Save a cash count (creates session if needed for opening count)");

        group.MapPost("/{cashCountId:long}/submit", async (
            long cashCountId,
            ClaimsPrincipal principal,
            ICashCountService cashCountService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await cashCountService.SubmitCashCountAsync(userId, cashCountId);
            return result.Success
                ? Results.Ok(ApiResponse<CashCountSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<CashCountSaveResult>.Fail(result.ErrorMessage ?? "Failed to submit cash count."));
        })
        .RequireAuthorization()
        .WithName("SubmitCashCount")
        .WithSummary("Submit a cash count for approval");

        group.MapGet("/{cashCountId:long}", async (
            long cashCountId,
            ClaimsPrincipal principal,
            ICashCountService cashCountService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var form = await cashCountService.GetCashCountFormAsync(userId, cashCountId);
            return form is null
                ? Results.NotFound(ApiResponse<CashCountFormModel>.Fail("Cash count not found."))
                : Results.Ok(ApiResponse<CashCountFormModel>.Ok(form));
        })
        .RequireAuthorization()
        .WithName("GetCashCount")
        .WithSummary("Get an existing cash count by ID");

        group.MapGet("/pending", async (
            long branchId,
            ICashCountService cashCountService) =>
        {
            var pending = await cashCountService.GetPendingApprovalsAsync(branchId);
            return Results.Ok(ApiResponse<List<PendingApprovalDto>>.Ok(pending));
        })
        .RequireAuthorization("CashCountApprove")
        .WithName("GetPendingApprovals")
        .WithSummary("Get all pending cash count approvals for a branch");

        group.MapPost("/{cashCountId:long}/approve", async (
            long cashCountId,
            ClaimsPrincipal principal,
            ICashCountService cashCountService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await cashCountService.ApproveCashCountAsync(userId, cashCountId);
            return result.Success
                ? Results.Ok(ApiResponse<CashCountSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<CashCountSaveResult>.Fail(result.ErrorMessage ?? "Failed to approve."));
        })
        .RequireAuthorization("CashCountApprove")
        .WithName("ApproveCashCount")
        .WithSummary("Approve a pending cash count (admin/supervisor only)");

        group.MapPost("/{cashCountId:long}/reject", async (
            long cashCountId,
            CashCountApprovalModel model,
            ClaimsPrincipal principal,
            ICashCountService cashCountService) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await cashCountService.RejectCashCountAsync(userId, cashCountId, model.RejectionReason ?? "");
            return result.Success
                ? Results.Ok(ApiResponse<CashCountSaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<CashCountSaveResult>.Fail(result.ErrorMessage ?? "Failed to reject."));
        })
        .RequireAuthorization("CashCountApprove")
        .WithName("RejectCashCount")
        .WithSummary("Reject a pending cash count (admin/supervisor only)");

        return group;
    }

    private static string? GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
}
