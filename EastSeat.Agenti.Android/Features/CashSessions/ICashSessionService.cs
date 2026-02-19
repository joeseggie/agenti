namespace EastSeat.Agenti.Android.Features.CashSessions;

/// <summary>
/// Service interface for cash session operations on Android.
/// </summary>
public interface ICashSessionService
{
    /// <summary>
    /// Gets all cash sessions for the current agent.
    /// </summary>
    Task<List<CashSessionListItemDto>> GetCashSessionsAsync();

    /// <summary>
    /// Gets details for a specific cash session.
    /// </summary>
    Task<CashSessionDetailDto?> GetCashSessionDetailAsync(long sessionId);
}
