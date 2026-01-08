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
}
