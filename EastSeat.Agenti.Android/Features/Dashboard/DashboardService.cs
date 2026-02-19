using System.Net.Http.Json;

namespace EastSeat.Agenti.Android.Features.Dashboard;

/// <summary>
/// Implementation of the dashboard service for Android.
/// Communicates with the Agenti web API.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly HttpClient _httpClient;

    public DashboardService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<DashboardViewModel?> GetDashboardAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/mobile/dashboard");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<DashboardViewModel>();
        }
        catch
        {
            return null;
        }
    }
}
