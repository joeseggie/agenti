namespace EastSeat.Agenti.Web.Features.BankRuns;

/// <summary>
/// Form model for recording a bank run receipt.
/// </summary>
public class BankRunFormModel
{
    /// <summary>Bank wallet to deposit into (destination).</summary>
    public long ToWalletId { get; set; }

    /// <summary>Total amount deposited at the bank.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// JSON-serialised denomination breakdown matching the bank receipt
    /// (e.g. {"50000":4,"20000":3}).
    /// </summary>
    public string? Denominations { get; set; }

    /// <summary>Bank deposit receipt / reference number.</summary>
    public string? ReceiptNumber { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// DTO for displaying a recorded bank run.
/// </summary>
public class BankRunDto
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public string FromWalletName { get; set; } = string.Empty;
    public string ToWalletName { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";
    public string? Denominations { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Result of a bank-run save operation.
/// </summary>
public class BankRunSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? BankRunId { get; set; }

    public static BankRunSaveResult Ok(long bankRunId) => new() { Success = true, BankRunId = bankRunId };
    public static BankRunSaveResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Lightweight wallet summary for UI dropdowns.
/// </summary>
public class AgentWalletDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}
