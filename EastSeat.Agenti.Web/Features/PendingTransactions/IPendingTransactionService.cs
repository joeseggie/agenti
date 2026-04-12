namespace EastSeat.Agenti.Web.Features.PendingTransactions;

/// <summary>
/// Service for recording and tracking pending (failed) transactions.
/// </summary>
public interface IPendingTransactionService
{
    /// <summary>
    /// Records a new pending transaction for the agent identified by <paramref name="userId"/>.
    /// </summary>
    Task<PendingTransactionSaveResult> RecordPendingTransactionAsync(string userId, PendingTransactionFormModel form);

    /// <summary>
    /// Updates a pending transaction with a ticket number, receipt photo, status change, or resolution notes.
    /// </summary>
    Task<PendingTransactionSaveResult> UpdatePendingTransactionAsync(string userId, long pendingTransactionId, PendingTransactionUpdateModel update);

    /// <summary>
    /// Gets all pending transactions for the agent's active session today.
    /// </summary>
    Task<List<PendingTransactionDto>> GetPendingTransactionsForAgentAsync(string userId);

    /// <summary>
    /// Gets pending transactions for a branch (all agents, admin/supervisor view).
    /// Only returns Open and ReportedToBank records.
    /// The implementation must validate that the caller identified by <paramref name="userId"/>
    /// is authorized to view data for <paramref name="branchId"/>.
    /// </summary>
    Task<List<PendingTransactionDto>> GetOpenPendingTransactionsForBranchAsync(string userId, long branchId);

    /// <summary>
    /// Gets all pending transactions (any status) for a branch, optionally filtered by agent.
    /// The implementation must validate that the caller identified by <paramref name="userId"/>
    /// is authorized to view data for <paramref name="branchId"/>.
    /// </summary>
    Task<List<PendingTransactionDto>> GetAllPendingTransactionsForBranchAsync(string userId, long branchId, long? agentId = null);

    /// <summary>
    /// Gets the active wallets for the agent identified by <paramref name="userId"/>.
    /// Used to populate wallet selection in forms.
    /// </summary>
    Task<List<PendingTransactionWalletDto>> GetAgentWalletsForUserAsync(string userId);
}
