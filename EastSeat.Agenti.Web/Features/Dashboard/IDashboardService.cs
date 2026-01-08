namespace EastSeat.Agenti.Web.Features.Dashboard;

/// <summary>
/// Service interface for dashboard operations.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets the dashboard view model for a specific agent.
    /// </summary>
    /// <param name="userId">The authenticated user's ID.</param>
    /// <returns>Dashboard view model with wallet balances and session status.</returns>
    Task<DashboardViewModel> GetDashboardAsync(string userId);
}
