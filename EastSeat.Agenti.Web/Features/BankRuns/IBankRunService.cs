using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankRuns;

/// <summary>
/// Service for recording and querying bank runs (physical bank deposits).
/// </summary>
public interface IBankRunService
{
    /// <summary>
    /// Records a bank run receipt. Immediately transfers <paramref name="model"/>.Amount from
    /// the agent's cash wallet to the target bank wallet and updates both balances.
    /// </summary>
    Task<BankRunSaveResult> RecordBankRunAsync(string userId, BankRunFormModel model);

    /// <summary>
    /// Returns all bank runs recorded for a cash session, optionally filtered by agent.
    /// </summary>
    Task<List<BankRunDto>> GetBankRunsForSessionAsync(long cashSessionId, long? agentId = null);

    /// <summary>
    /// Returns all bank runs recorded by the currently logged-in agent during their active session.
    /// </summary>
    Task<List<BankRunDto>> GetBankRunsForAgentAsync(string userId);

    /// <summary>
    /// Returns the total amount banked per cash-wallet ID for an agent in a given session.
    /// Used when calculating the expected closing balance for the cash wallet.
    /// </summary>
    Task<Dictionary<long, decimal>> GetBankRunTotalsAsync(long cashSessionId, long agentId);

    /// <summary>
    /// Returns the active wallets belonging to the agent for the specified wallet type.
    /// Used to populate dropdowns in the UI.
    /// </summary>
    Task<List<AgentWalletDto>> GetAgentWalletsAsync(string userId, EastSeat.Agenti.Shared.Domain.Enums.WalletTypeEnum walletType);
}
