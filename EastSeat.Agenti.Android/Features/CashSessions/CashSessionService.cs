using System.Net.Http.Json;

namespace EastSeat.Agenti.Android.Features.CashSessions;

/// <summary>
/// Implementation of the cash session service for Android.
/// Communicates with the Agenti web API.
/// </summary>
public class CashSessionService : ICashSessionService
{
    private readonly HttpClient _httpClient;

    public CashSessionService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<List<CashSessionListItemDto>> GetCashSessionsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/mobile/cash-sessions");

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            return await response.Content.ReadFromJsonAsync<List<CashSessionListItemDto>>() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<CashSessionDetailDto?> GetCashSessionDetailAsync(long sessionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/mobile/cash-sessions/{sessionId}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CashSessionDetailDto>();
        }
        catch
        {
            return null;
        }
    }
}
