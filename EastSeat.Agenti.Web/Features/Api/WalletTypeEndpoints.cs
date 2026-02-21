using EastSeat.Agenti.Web.Features.WalletTypes;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for wallet type management.
/// </summary>
public static class WalletTypeEndpoints
{
    public static RouteGroupBuilder MapWalletTypesApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IWalletTypeService walletTypeService) =>
        {
            var walletTypes = await walletTypeService.GetWalletTypesAsync();
            return Results.Ok(ApiResponse<List<WalletTypeListItemDto>>.Ok(walletTypes));
        })
        .RequireAuthorization()
        .WithName("GetWalletTypes")
        .WithSummary("Get all wallet types");

        return group;
    }
}
