namespace EastSeat.Agenti.Web.Features.Dashboard;

/// <summary>
/// Represents an agent's total wallet balance for the dashboard summary table.
/// </summary>
public record AgentWalletSummaryDto
{
    public long AgentId { get; init; }
    public string AgentCode { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public decimal TotalBalance { get; init; }
    public string Currency { get; init; } = "UGX";
}
