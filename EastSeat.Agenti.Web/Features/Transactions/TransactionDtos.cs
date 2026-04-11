using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Transactions;

/// <summary>
/// Summary of a transaction for display in lists.
/// </summary>
public class TransactionListItemDto
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string FromWalletName { get; set; } = string.Empty;
    public string ToWalletName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsReversed { get; set; }

    /// <summary>Whether this transaction has an active (non-dismissed) flag.</summary>
    public bool IsFlagged { get; set; }

    /// <summary>Status of the flag if one exists.</summary>
    public TransactionFlagStatus? FlagStatus { get; set; }

    public string TypeDisplay => Type switch
    {
        TransactionType.Deposit => "Deposit",
        TransactionType.Withdrawal => "Withdrawal",
        TransactionType.Transfer => "Transfer",
        TransactionType.Adjustment => "Adjustment",
        TransactionType.Reversal => "Reversal",
        _ => Type.ToString()
    };
}

/// <summary>
/// Form model submitted by an agent when flagging a transaction as erroneous.
/// </summary>
public class FlagTransactionFormModel
{
    public long TransactionId { get; set; }

    /// <summary>Agent's explanation of the error (minimum 10 characters).</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Display DTO for a transaction flag used in admin review.
/// </summary>
public class TransactionFlagDto
{
    public long Id { get; set; }
    public long TransactionId { get; set; }
    public TransactionType TransactionType { get; set; }
    public decimal TransactionAmount { get; set; }
    public string TransactionCurrency { get; set; } = "UGX";
    public string? TransactionReference { get; set; }
    public string FromWalletName { get; set; } = string.Empty;
    public string ToWalletName { get; set; } = string.Empty;
    public DateTimeOffset TransactionCreatedAt { get; set; }
    public string FlaggedByAgentName { get; set; } = string.Empty;
    public string FlaggedByAgentCode { get; set; } = string.Empty;
    public DateTimeOffset FlaggedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public TransactionFlagStatus Status { get; set; }
    public string? InvestigationNotes { get; set; }
    public string? ResolvedByName { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public string StatusDisplay => Status switch
    {
        TransactionFlagStatus.PendingReview => "Pending Review",
        TransactionFlagStatus.UnderInvestigation => "Under Investigation",
        TransactionFlagStatus.Resolved => "Resolved",
        TransactionFlagStatus.Dismissed => "Dismissed",
        _ => Status.ToString()
    };
}

/// <summary>
/// Result of a flag save/update operation.
/// </summary>
public class TransactionFlagResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? FlagId { get; set; }

    public static TransactionFlagResult Ok(long flagId) => new() { Success = true, FlagId = flagId };
    public static TransactionFlagResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Request body for resolving or dismissing a transaction flag.
/// </summary>
public class ResolveTransactionFlagRequest
{
    /// <summary>Notes explaining the resolution or dismissal (minimum 10 characters).</summary>
    public string Notes { get; set; } = string.Empty;
}
