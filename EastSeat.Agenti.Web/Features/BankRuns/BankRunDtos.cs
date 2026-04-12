namespace EastSeat.Agenti.Web.Features.BankRuns;

/// <summary>
/// Form model for recording a bank run receipt.
/// </summary>
public class BankRunFormModel
{
    /// <summary>
    /// Wallet type the agent is depositing into (destination). Must not be Cash.
    /// </summary>
    public long ToWalletTypeId { get; set; }

    /// <summary>Total amount deposited at the bank (must match the deposit slip).</summary>
    public decimal Amount { get; set; }

    /// <summary>Bank deposit receipt / reference number.</summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>Binary content of the photographed bank deposit slip.</summary>
    public byte[]? ReceiptImage { get; set; }

    /// <summary>MIME type of the receipt image (e.g. "image/jpeg").</summary>
    public string? ReceiptImageContentType { get; set; }

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
    public bool HasReceiptImage { get; set; }
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

/// <summary>
/// Wallet type option for the bank-run destination selector (excludes Cash).
/// </summary>
public class WalletTypeOptionDto
{
    public long WalletTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
}
