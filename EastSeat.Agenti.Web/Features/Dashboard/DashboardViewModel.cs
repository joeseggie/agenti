namespace EastSeat.Agenti.Web.Features.Dashboard;

/// <summary>
/// View model for the agent dashboard.
/// </summary>
public record DashboardViewModel
{
    public IReadOnlyList<WalletBalanceSummaryDto> Wallets { get; init; } = [];
    public SessionStatusDto SessionStatus { get; init; } = new();
    public decimal TotalBalance => Wallets.Sum(w => w.Balance);
    public string Currency { get; init; } = "UGX";
    public bool HasWallets => Wallets.Count > 0;
}
