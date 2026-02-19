namespace EastSeat.Agenti.Android.Features.Dashboard;

/// <summary>
/// DTO for wallet balance summary on the dashboard.
/// </summary>
public class WalletBalanceSummaryDto
{
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public string WalletTypeIcon { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string Currency { get; set; } = "UGX";
    public bool SupportsDenominations { get; set; }
}

/// <summary>
/// DTO for the current session status on the dashboard.
/// </summary>
public class SessionStatusDto
{
    public long? SessionId { get; set; }
    public DateOnly? SessionDate { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public bool HasActiveSession { get; set; }

    public string StatusDisplay => Status switch
    {
        "Open" => "Open",
        "Closed" => "Closed",
        "Blocked" => "Blocked",
        "Pending" => "Pending",
        "DiscrepancyUnderReview" => "Under Review",
        "Completed" => "Completed",
        _ => "No Session"
    };

    public string StatusColor => Status switch
    {
        "Open" => "#4CAF50",
        "Closed" => "#9E9E9E",
        "Blocked" => "#F44336",
        "Pending" => "#FF9800",
        "DiscrepancyUnderReview" => "#FF9800",
        "Completed" => "#2196F3",
        _ => "#2196F3"
    };
}

/// <summary>
/// View model for the agent dashboard.
/// </summary>
public class DashboardViewModel
{
    public IReadOnlyList<WalletBalanceSummaryDto> Wallets { get; set; } = [];
    public SessionStatusDto SessionStatus { get; set; } = new();
    public decimal TotalBalance => Wallets.Sum(w => w.Balance);
    public string Currency { get; set; } = "UGX";
    public bool HasWallets => Wallets.Count > 0;
}
