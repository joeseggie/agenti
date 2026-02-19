namespace EastSeat.Agenti.Android.Features.CashCounts;

/// <summary>
/// DTO for a wallet count entry in a cash count form.
/// </summary>
public class WalletCountEntryDto
{
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public bool SupportsDenominations { get; set; }
    public decimal ExpectedBalance { get; set; }
    public decimal CountedAmount { get; set; }

    /// <summary>
    /// Calculates the variance between expected and counted amounts.
    /// </summary>
    public decimal Variance => CountedAmount - ExpectedBalance;

    /// <summary>
    /// Indicates if there's a discrepancy.
    /// </summary>
    public bool HasDiscrepancy => Variance != 0;
}

/// <summary>
/// Form model for cash count capture on Android.
/// </summary>
public class CashCountFormModel
{
    public long? CashCountId { get; set; }
    public long? CashSessionId { get; set; }
    public bool IsOpening { get; set; }
    public List<WalletCountEntryDto> WalletEntries { get; set; } = [];

    public decimal TotalAmount => WalletEntries.Sum(w => w.CountedAmount);
    public decimal TotalExpected => WalletEntries.Sum(w => w.ExpectedBalance);
    public decimal TotalVariance => TotalAmount - TotalExpected;
}

/// <summary>
/// DTO for the current session state for a cash count.
/// </summary>
public class CurrentSessionDto
{
    public long? SessionId { get; set; }
    public DateOnly? SessionDate { get; set; }
    public string StatusText { get; set; } = "No Active Session";
    public string StatusColor { get; set; } = "#FF9800";
    public bool CanPerformOpeningCount { get; set; }
    public bool CanPerformClosingCount { get; set; }
    public bool HasOpeningCount { get; set; }
    public bool HasClosingCount { get; set; }
}

/// <summary>
/// Result of saving a cash count.
/// </summary>
public class CashCountSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? CashCountId { get; set; }
    public long? CashSessionId { get; set; }

    public static CashCountSaveResult Ok(long cashCountId, long cashSessionId) => new()
    {
        Success = true,
        CashCountId = cashCountId,
        CashSessionId = cashSessionId
    };

    public static CashCountSaveResult Error(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}
