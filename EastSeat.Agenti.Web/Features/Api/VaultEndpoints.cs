using System.Security.Claims;
using EastSeat.Agenti.Web.Features.Vaults;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for vault management.
/// </summary>
public static class VaultEndpoints
{
    public static RouteGroupBuilder MapVaultApi(this RouteGroupBuilder group)
    {
        group.MapGet("/{branchId:long}", async (
            long branchId,
            IVaultService vaultService,
            CancellationToken cancellationToken) =>
        {
            var vault = await vaultService.GetVaultAsync(branchId, cancellationToken);
            return vault is null
                ? Results.NotFound(ApiResponse<VaultDto>.Fail("Vault not found."))
                : Results.Ok(ApiResponse<VaultDto>.Ok(vault));
        })
        .RequireAuthorization("VaultView")
        .WithName("GetVault")
        .WithSummary("Get vault information for a branch");

        group.MapGet("/{branchId:long}/transactions", async (
            long branchId,
            IVaultService vaultService,
            CancellationToken cancellationToken,
            int take = 50,
            bool includeExpired = false) =>
        {
            var transactions = await vaultService.GetRecentTransactionsAsync(
                branchId, take, includeExpired, cancellationToken);
            return Results.Ok(ApiResponse<List<VaultTransactionListItemDto>>.Ok(transactions));
        })
        .RequireAuthorization("VaultView")
        .WithName("GetVaultTransactions")
        .WithSummary("Get recent vault transactions for a branch");

        group.MapPost("/{branchId:long}/adjustment", async (
            long branchId,
            ManualAdjustmentFormModel model,
            ClaimsPrincipal principal,
            IVaultService vaultService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await vaultService.RequestManualAdjustmentAsync(
                branchId, model.Amount, model.IsDeposit, model.Notes, userId, cancellationToken);
            return result.Success
                ? Results.Ok(ApiResponse<VaultOperationResult>.Ok(result))
                : Results.BadRequest(ApiResponse<VaultOperationResult>.Fail(result.ErrorMessage ?? "Failed to request adjustment."));
        })
        .RequireAuthorization("VaultAdjust")
        .WithName("RequestVaultAdjustment")
        .WithSummary("Request a manual vault adjustment");

        group.MapPost("/adjustment/{transactionId:long}/approve", async (
            long transactionId,
            ClaimsPrincipal principal,
            IVaultService vaultService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await vaultService.ApproveManualAdjustmentAsync(transactionId, userId, cancellationToken);
            return result.Success
                ? Results.Ok(ApiResponse<VaultOperationResult>.Ok(result))
                : Results.BadRequest(ApiResponse<VaultOperationResult>.Fail(result.ErrorMessage ?? "Failed to approve adjustment."));
        })
        .RequireAuthorization("VaultApprove")
        .WithName("ApproveVaultAdjustment")
        .WithSummary("Approve a pending vault adjustment");

        group.MapPost("/adjustment/{transactionId:long}/reject", async (
            long transactionId,
            ClaimsPrincipal principal,
            IVaultService vaultService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(principal);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await vaultService.RejectManualAdjustmentAsync(transactionId, userId, cancellationToken);
            return result.Success
                ? Results.Ok(ApiResponse<VaultOperationResult>.Ok(result))
                : Results.BadRequest(ApiResponse<VaultOperationResult>.Fail(result.ErrorMessage ?? "Failed to reject adjustment."));
        })
        .RequireAuthorization("VaultApprove")
        .WithName("RejectVaultAdjustment")
        .WithSummary("Reject a pending vault adjustment");

        return group;
    }

    private static string? GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
}
