namespace EastSeat.Agenti.Android.Features.CashCounts;

/// <summary>
/// Service interface for cash count operations on Android.
/// </summary>
public interface ICashCountService
{
    /// <summary>
    /// Gets the current session status for the authenticated agent.
    /// </summary>
    Task<CurrentSessionDto?> GetCurrentSessionAsync();

    /// <summary>
    /// Initialises the cash count form for an opening or closing count.
    /// </summary>
    Task<CashCountFormModel?> InitializeCashCountFormAsync(bool isOpening);

    /// <summary>
    /// Submits a cash count.
    /// </summary>
    Task<CashCountSaveResult> SubmitCashCountAsync(CashCountFormModel form);
}
