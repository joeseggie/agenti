using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.CashCounts;

/// <summary>
/// DTO for a wallet's cash count entry.
/// </summary>
public class WalletCountEntryDto
{
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public bool SupportsDenominations { get; set; }
    public decimal ExpectedBalance { get; set; }
    public decimal CountedAmount { get; set; }
    public DenominationBreakdown? Denominations { get; set; }

    public decimal Variance => CountedAmount - ExpectedBalance;
    public bool HasDiscrepancy => Variance != 0;
}

/// <summary>
/// Form model for cash count capture.
/// </summary>
public class CashCountFormModel
{
    public long? CashCountId { get; set; }
    public long? CashSessionId { get; set; }
    public bool IsOpening { get; set; }
    public DateOnly? CountDate { get; set; }
    public string? Explanation { get; set; }
    public List<WalletCountEntryDto> WalletEntries { get; set; } = [];

    public decimal TotalAmount => WalletEntries.Sum(w => w.CountedAmount);
    public decimal TotalExpected => WalletEntries.Sum(w => w.ExpectedBalance);
    public decimal TotalVariance => TotalAmount - TotalExpected;
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

/// <summary>
/// DTO for displaying current session information for an agent.
/// </summary>
public class CurrentSessionDto
{
    public long? SessionId { get; set; }
    public DateOnly? SessionDate { get; set; }
    public string StatusText { get; set; } = "No Active Session";
    public string StatusColor { get; set; } = "warning";
    public bool CanPerformOpeningCount { get; set; }
    public bool CanPerformClosingCount { get; set; }
    public bool HasOpeningCount { get; set; }
    public bool HasClosingCount { get; set; }
    public CashCountStatus? OpeningCountStatus { get; set; }
    public CashCountStatus? ClosingCountStatus { get; set; }
    public bool HasPendingApproval { get; set; }
    public string? BlockReason { get; set; }
}

/// <summary>
/// DTO for pending approval list items (admin view).
/// </summary>
public class PendingApprovalDto
{
    public long CashCountId { get; set; }
    public long CashSessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public DateOnly CountDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public bool IsOpening { get; set; }
    public CashCountStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? OpeningTotal { get; set; }
    public decimal? Variance { get; set; }
    public string? Explanation { get; set; }
    public bool HasDiscrepancy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}

/// <summary>
/// Model for admin approval/rejection action.
/// </summary>
public class CashCountApprovalModel
{
    public long CashCountId { get; set; }
    public bool Approve { get; set; }
    public string? RejectionReason { get; set; }
}
