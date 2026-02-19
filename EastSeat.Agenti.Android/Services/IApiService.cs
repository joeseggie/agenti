using EastSeat.Agenti.Android.Models;

namespace EastSeat.Agenti.Android.Services;

/// <summary>
/// Interface for the Agenti REST API service.
/// </summary>
public interface IApiService
{
    // Auth
    Task<ApiResponse<LoginResponse>?> LoginAsync(string email, string password);

    // Dashboard
    Task<ApiResponse<DashboardViewModel>?> GetDashboardAsync();

    // Agents
    Task<ApiResponse<List<AgentListItem>>?> GetAgentsAsync();
    Task<ApiResponse<AgentDetail>?> GetAgentAsync(long agentId);

    // Cash Sessions
    Task<ApiResponse<List<CashSessionListItem>>?> GetCashSessionsAsync();

    // Cash Counts
    Task<ApiResponse<CurrentSessionInfo>?> GetCurrentSessionAsync();
    Task<ApiResponse<CashCountForm>?> InitializeCashCountAsync(bool isOpening);
    Task<ApiResponse<CashCountResult>?> SaveCashCountAsync(CashCountForm form);
    Task<ApiResponse<CashCountResult>?> SubmitCashCountAsync(long cashCountId);

    // Vault
    Task<ApiResponse<VaultInfo>?> GetVaultAsync(long branchId);
    Task<ApiResponse<List<VaultTransactionItem>>?> GetVaultTransactionsAsync(long branchId);
}
