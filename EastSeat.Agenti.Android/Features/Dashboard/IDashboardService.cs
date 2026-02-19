namespace EastSeat.Agenti.Android.Features.Dashboard;

/// <summary>
/// Service interface for dashboard operations on Android.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets the dashboard view model for the currently authenticated agent.
    /// </summary>
    Task<DashboardViewModel?> GetDashboardAsync();
}
