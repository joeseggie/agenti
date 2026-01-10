namespace EastSeat.Agenti.Web.Features.CashSessions;

/// <summary>
/// Service interface for cash session operations.
/// </summary>
public interface ICashSessionService
{
    /// <summary>
    /// Gets all cash sessions with summary information.
    /// </summary>
    Task<List<CashSessionListItemDto>> GetCashSessionsAsync();

    /// <summary>
    /// Gets cash session details by ID including opening and closing counts.
    /// </summary>
    Task<CashSessionDetailDto?> GetCashSessionDetailAsync(long sessionId);

    /// <summary>
    /// Closes a cash session by ID.
    /// </summary>
    /// <param name="sessionId">The session ID to close.</param>
    /// <returns>True if closed successfully, false otherwise.</returns>
    Task<(bool Success, string? ErrorMessage)> CloseSessionAsync(long sessionId);
}
