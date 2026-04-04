namespace EastSeat.Agenti.Web.Features.CashSessions;

/// <summary>
/// Service interface for cash session operations.
/// </summary>
public interface ICashSessionService
{
    /// <summary>
    /// Gets all cash sessions with summary information (branch-level).
    /// </summary>
    Task<List<CashSessionListItemDto>> GetCashSessionsAsync(long? branchId = null);

    /// <summary>
    /// Gets cash session details by ID including all agents' opening and closing counts.
    /// </summary>
    Task<CashSessionDetailDto?> GetCashSessionDetailAsync(long sessionId);

    /// <summary>
    /// Closes a cash session. Requires all agents' closing counts to be approved.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> CloseSessionAsync(long sessionId);

    /// <summary>
    /// Gets open sessions that have not been closed (for admin dashboards).
    /// </summary>
    Task<List<CashSessionListItemDto>> GetOpenSessionsAsync(long branchId);
}
