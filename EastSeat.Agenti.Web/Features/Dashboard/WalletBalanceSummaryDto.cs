namespace EastSeat.Agenti.Web.Features.Dashboard;

/// <summary>
/// Represents a wallet balance summary for the dashboard.
/// </summary>
public record WalletBalanceSummaryDto
{
    public long WalletId { get; init; }
    public string WalletName { get; init; } = string.Empty;
    public string WalletTypeName { get; init; } = string.Empty;
    public string WalletTypeIcon { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Currency { get; init; } = "UGX";
    public bool SupportsDenominations { get; init; }
}
