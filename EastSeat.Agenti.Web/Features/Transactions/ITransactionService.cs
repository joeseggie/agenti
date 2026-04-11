namespace EastSeat.Agenti.Web.Features.Transactions;

/// <summary>
/// Service for querying agent transactions and managing erroneous transaction flags.
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Gets transactions for the agent's current active session.
    /// </summary>
    Task<List<TransactionListItemDto>> GetTransactionsForAgentAsync(string userId);

    /// <summary>
    /// Gets transactions for a specific cash session.
    /// </summary>
    Task<List<TransactionListItemDto>> GetTransactionsForSessionAsync(long cashSessionId);

    /// <summary>
    /// Flags a transaction as having been made in error.
    /// Only the agent who belongs to the session containing the transaction may flag it.
    /// A transaction can only have one active flag at a time.
    /// </summary>
    Task<TransactionFlagResult> FlagTransactionAsync(string userId, FlagTransactionFormModel form);

    /// <summary>
    /// Marks a flag as under investigation. Only admins/supervisors can perform this action.
    /// </summary>
    Task<TransactionFlagResult> StartInvestigationAsync(string adminUserId, long flagId);

    /// <summary>
    /// Resolves a flagged transaction. Only admins/supervisors can resolve.
    /// </summary>
    Task<TransactionFlagResult> ResolveFlagAsync(string adminUserId, long flagId, string notes);

    /// <summary>
    /// Dismisses a flagged transaction without further action. Only admins/supervisors can dismiss.
    /// </summary>
    Task<TransactionFlagResult> DismissFlagAsync(string adminUserId, long flagId, string notes);

    /// <summary>
    /// Gets all active (non-dismissed, non-resolved) flagged transactions for a branch.
    /// For use by admins/supervisors.
    /// </summary>
    Task<List<TransactionFlagDto>> GetActiveFlagsForBranchAsync(long branchId);

    /// <summary>
    /// Gets all flags (any status) for a branch, optionally filtered by status.
    /// For use by admins/supervisors.
    /// </summary>
    Task<List<TransactionFlagDto>> GetAllFlagsForBranchAsync(long branchId, string? statusFilter = null);
}
