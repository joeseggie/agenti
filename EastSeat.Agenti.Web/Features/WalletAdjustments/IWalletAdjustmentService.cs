namespace EastSeat.Agenti.Web.Features.WalletAdjustments;

/// <summary>
/// Service for recording and querying wallet adjustments (debit-only withdrawals).
/// </summary>
public interface IWalletAdjustmentService
{
    /// <summary>
    /// Records a new wallet adjustment for the current session.
    /// </summary>
    Task<WalletAdjustmentSaveResult> RecordAdjustmentAsync(string userId, WalletAdjustmentFormModel form);

    /// <summary>
    /// Gets all adjustments for a session, optionally filtered by agent.
    /// </summary>
    Task<List<WalletAdjustmentDto>> GetAdjustmentsForSessionAsync(long cashSessionId, long? agentId = null);

    /// <summary>
    /// Gets total adjustment amounts per wallet for an agent in a session.
    /// Used to compute adjusted expected balances for closing counts.
    /// </summary>
    Task<Dictionary<long, decimal>> GetWalletAdjustmentTotalsAsync(long cashSessionId, long agentId);

    /// <summary>
    /// Gets adjustments for the currently logged-in agent's active session.
    /// </summary>
    Task<List<WalletAdjustmentDto>> GetAdjustmentsForAgentAsync(string userId);
}
