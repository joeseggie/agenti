using System.Net.Http.Json;

namespace EastSeat.Agenti.Android.Features.CashCounts;

/// <summary>
/// Implementation of the cash count service for Android.
/// Communicates with the Agenti web API.
/// </summary>
public class CashCountService : ICashCountService
{
    private readonly HttpClient _httpClient;

    public CashCountService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<CurrentSessionDto?> GetCurrentSessionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/mobile/cash-counts/current-session");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CurrentSessionDto>();
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CashCountFormModel?> InitializeCashCountFormAsync(bool isOpening)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"api/mobile/cash-counts/form?isOpening={isOpening}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CashCountFormModel>();
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> SubmitCashCountAsync(CashCountFormModel form)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/mobile/cash-counts", form);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return CashCountSaveResult.Error(string.IsNullOrWhiteSpace(error)
                    ? "Failed to submit cash count."
                    : error);
            }

            var result = await response.Content.ReadFromJsonAsync<CashCountSaveResult>();
            return result ?? CashCountSaveResult.Error("Invalid response from server.");
        }
        catch (HttpRequestException ex)
        {
            return CashCountSaveResult.Error($"Unable to connect to server: {ex.Message}");
        }
        catch (Exception ex)
        {
            return CashCountSaveResult.Error($"An unexpected error occurred: {ex.Message}");
        }
    }
}
