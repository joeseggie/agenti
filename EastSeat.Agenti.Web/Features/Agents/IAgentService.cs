namespace EastSeat.Agenti.Web.Features.Agents;

/// <summary>
/// Service interface for agent management operations.
/// </summary>
public interface IAgentService
{
    /// <summary>
    /// Gets all agents with summary information.
    /// </summary>
    Task<List<AgentListItemDto>> GetAgentsAsync();

    /// <summary>
    /// Gets agent details by ID including wallets.
    /// </summary>
    Task<AgentDetailDto?> GetAgentAsync(long agentId);

    /// <summary>
    /// Gets ApplicationUsers that are available to become agents (no AgentId assigned).
    /// </summary>
    Task<List<AvailableUserDto>> GetAvailableUsersAsync();

    /// <summary>
    /// Creates a new agent linked to an ApplicationUser.
    /// Also updates the ApplicationUser with the new AgentId.
    /// </summary>
    Task<SaveResult> CreateAgentAsync(AgentFormModel model);

    /// <summary>
    /// Updates an existing agent.
    /// </summary>
    Task<SaveResult> UpdateAgentAsync(AgentFormModel model);

    /// <summary>
    /// Toggles agent active status.
    /// </summary>
    Task<SaveResult> ToggleAgentStatusAsync(long agentId);

    /// <summary>
    /// Gets all available wallet types.
    /// </summary>
    Task<List<WalletTypeDto>> GetWalletTypesAsync();

    /// <summary>
    /// Adds a wallet to an agent.
    /// </summary>
    Task<SaveResult> AddWalletAsync(WalletFormModel model);

    /// <summary>
    /// Updates an existing wallet.
    /// </summary>
    Task<SaveResult> UpdateWalletAsync(WalletFormModel model);

    /// <summary>
    /// Toggles wallet active status.
    /// </summary>
    Task<SaveResult> ToggleWalletStatusAsync(long walletId);

    /// <summary>
    /// Deletes a wallet (only if balance is zero and no transactions).
    /// </summary>
    Task<SaveResult> DeleteWalletAsync(long walletId);
}
