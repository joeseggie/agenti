namespace EastSeat.Agenti.Android.Features.CashSessions;

/// <summary>
/// DTO for displaying a cash session in a list.
/// </summary>
public class CashSessionListItemDto
{
    public long Id { get; set; }
    public DateOnly SessionDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal OpeningTotal { get; set; }
    public decimal? ClosingTotal { get; set; }
    public decimal? Variance => ClosingTotal.HasValue ? ClosingTotal.Value - OpeningTotal : null;

    public string StatusColor => Status switch
    {
        "Open" => "#4CAF50",
        "Closed" => "#9E9E9E",
        "Blocked" => "#F44336",
        "Pending" => "#FF9800",
        "DiscrepancyUnderReview" => "#FF9800",
        "Completed" => "#2196F3",
        _ => "#9E9E9E"
    };
}

/// <summary>
/// DTO for detailed wallet count within a session.
/// </summary>
public class WalletCountSummaryDto
{
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>
/// DTO for displaying a cash count detail within a session.
/// </summary>
public class CashCountDetailDto
{
    public long Id { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public List<WalletCountSummaryDto> WalletEntries { get; set; } = [];
}

/// <summary>
/// DTO for detailed cash session information.
/// </summary>
public class CashSessionDetailDto
{
    public long Id { get; set; }
    public DateOnly SessionDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal OpeningTotal { get; set; }
    public decimal? ClosingTotal { get; set; }
    public decimal? Variance => ClosingTotal.HasValue ? ClosingTotal.Value - OpeningTotal : null;
    public CashCountDetailDto? OpeningCount { get; set; }
    public CashCountDetailDto? ClosingCount { get; set; }
}
