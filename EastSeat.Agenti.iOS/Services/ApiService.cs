using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EastSeat.Agenti.iOS.Models;

namespace EastSeat.Agenti.iOS.Services;

/// <summary>
/// HTTP client service for communicating with the Agenti REST API.
/// </summary>
public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetAuthorizationToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearAuthorizationToken()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    // ─── Auth ─────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<LoginResponse>?> LoginAsync(string email, string password)
    {
        var request = new LoginRequest { Email = email, Password = password };
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", request, JsonOptions);
        return await ReadResponseAsync<LoginResponse>(response);
    }

    // ─── Dashboard ────────────────────────────────────────────────────────────

    public async Task<ApiResponse<DashboardViewModel>?> GetDashboardAsync()
    {
        var response = await _httpClient.GetAsync("api/dashboard");
        return await ReadResponseAsync<DashboardViewModel>(response);
    }

    // ─── Agents ───────────────────────────────────────────────────────────────

    public async Task<ApiResponse<List<AgentListItem>>?> GetAgentsAsync()
    {
        var response = await _httpClient.GetAsync("api/agents");
        return await ReadResponseAsync<List<AgentListItem>>(response);
    }

    public async Task<ApiResponse<AgentDetail>?> GetAgentAsync(long agentId)
    {
        var response = await _httpClient.GetAsync($"api/agents/{agentId}");
        return await ReadResponseAsync<AgentDetail>(response);
    }

    // ─── Cash Sessions ────────────────────────────────────────────────────────

    public async Task<ApiResponse<List<CashSessionListItem>>?> GetCashSessionsAsync()
    {
        var response = await _httpClient.GetAsync("api/cash-sessions");
        return await ReadResponseAsync<List<CashSessionListItem>>(response);
    }

    // ─── Cash Counts ──────────────────────────────────────────────────────────

    public async Task<ApiResponse<CurrentSessionInfo>?> GetCurrentSessionAsync()
    {
        var response = await _httpClient.GetAsync("api/cash-counts/current");
        return await ReadResponseAsync<CurrentSessionInfo>(response);
    }

    public async Task<ApiResponse<CashCountForm>?> InitializeCashCountAsync(bool isOpening)
    {
        var response = await _httpClient.GetAsync($"api/cash-counts/initialize?isOpening={isOpening}");
        return await ReadResponseAsync<CashCountForm>(response);
    }

    public async Task<ApiResponse<CashCountResult>?> SaveCashCountAsync(CashCountForm form)
    {
        var response = await _httpClient.PostAsJsonAsync("api/cash-counts", form, JsonOptions);
        return await ReadResponseAsync<CashCountResult>(response);
    }

    public async Task<ApiResponse<CashCountResult>?> SubmitCashCountAsync(long cashCountId)
    {
        var response = await _httpClient.PostAsync($"api/cash-counts/{cashCountId}/submit",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));
        return await ReadResponseAsync<CashCountResult>(response);
    }

    // ─── Vault ────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<VaultInfo>?> GetVaultAsync(long branchId)
    {
        var response = await _httpClient.GetAsync($"api/vault/{branchId}");
        return await ReadResponseAsync<VaultInfo>(response);
    }

    public async Task<ApiResponse<List<VaultTransactionItem>>?> GetVaultTransactionsAsync(long branchId)
    {
        var response = await _httpClient.GetAsync($"api/vault/{branchId}/transactions");
        return await ReadResponseAsync<List<VaultTransactionItem>>(response);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<ApiResponse<T>?> ReadResponseAsync<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return new ApiResponse<T> { Success = false, Error = "Session expired or unauthorized. Please log in again." };

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new ApiResponse<T> { Success = false, Error = "You do not have permission to perform this action." };

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiResponse<T>>(json, JsonOptions);
    }
}
