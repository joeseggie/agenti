using EastSeat.Agenti.Web.Features.CashCounts;

namespace EastSeat.Agenti.Web.Features.Api;

public static class CashCountApiContract
{
    public const int Version = 2;
    public const string UpgradeMessage = "Update the Android app before using cash counts.";

    public static bool Supports(int? version) => version == Version;

    public static IResult Form(CashCountFormModel form, int? version)
    {
        if (!Supports(version) && form.WalletEntries.Any(w => w.UsesEnteredClosingAmount))
            return UpgradeRequired();

        if (Supports(version))
            form.ApiContractVersion = Version;
        return Results.Ok(ApiResponse<CashCountFormModel>.Ok(form));
    }

    public static IResult UpgradeRequired() => Results.Json(
        ApiResponse<object>.Fail(UpgradeMessage), statusCode: StatusCodes.Status426UpgradeRequired);
}
