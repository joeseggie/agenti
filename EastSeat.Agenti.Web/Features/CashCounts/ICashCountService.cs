namespace EastSeat.Agenti.Web.Features.CashCounts;

/// <summary>
/// Service interface for cash count operations.
/// </summary>
public interface ICashCountService
{
    /// <summary>
    /// Gets the current session information for an agent.
    /// </summary>
    Task<CurrentSessionDto> GetCurrentSessionAsync(string userId);

    /// <summary>
    /// Initializes a cash count form for opening or closing count.
    /// </summary>
    Task<CashCountFormModel> InitializeCashCountFormAsync(string userId, bool isOpening);

    /// <summary>
    /// Saves a cash count (creates session if needed for opening count).
    /// </summary>
    Task<CashCountSaveResult> SaveCashCountAsync(string userId, CashCountFormModel form);

    /// <summary>
    /// Submits a cash count for approval.
    /// </summary>
    Task<CashCountSaveResult> SubmitCashCountAsync(string userId, long cashCountId);

    /// <summary>
    /// Gets an existing cash count for editing.
    /// </summary>
    Task<CashCountFormModel?> GetCashCountFormAsync(string userId, long cashCountId);

    /// <summary>
    /// Approves a cash count (admin only). Executes vault operations on approval.
    /// </summary>
    Task<CashCountSaveResult> ApproveCashCountAsync(string adminUserId, long cashCountId);

    /// <summary>
    /// Rejects a cash count (admin only). Agent must revise and resubmit.
    /// </summary>
    Task<CashCountSaveResult> RejectCashCountAsync(string adminUserId, long cashCountId, string reason);

    /// <summary>
    /// Gets all cash counts pending admin approval for a branch.
    /// </summary>
    Task<List<PendingApprovalDto>> GetPendingApprovalsAsync(long branchId);

    /// <summary>
    /// Admin closes a session for a specific agent (rule 22).
    /// Creates a closing count matching the opening count if the agent cannot close themselves.
    /// </summary>
    Task<CashCountSaveResult> AdminCloseAgentSessionAsync(string adminUserId, long cashSessionId, long agentId);
}
