using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Dashboard;

/// <summary>
/// Represents the current session status for the dashboard.
/// </summary>
public record SessionStatusDto
{
    public bool UsesBankReconciliation { get; init; }
    public DateTimeOffset? OperationalClosedAt { get; init; }
    public int PendingClosingApprovalCount { get; init; }
    public string BalanceBasis => UsesBankReconciliation
        ? "Stored internal allocations; customer tallies are shown in the register and cash-count form."
        : "Stored wallet balances";
    public long? SessionId { get; init; }
    public DateOnly? SessionDate { get; init; }
    public CashSessionStatus? Status { get; init; }
    public DateTimeOffset? OpenedAt { get; init; }
    public bool HasActiveSession { get; init; }

    public string StatusDisplay => UsesBankReconciliation && OperationalClosedAt.HasValue
        ? PendingClosingApprovalCount > 0 ? "Trading ended; bank review pending" : "Trading ended; approval recorded"
        : Status switch
    {
        CashSessionStatus.Open => "Open",
        CashSessionStatus.Closed => "Closed",
        CashSessionStatus.Blocked => "Blocked",
        CashSessionStatus.Pending => "Pending",
        CashSessionStatus.DiscrepancyUnderReview => "Under Review",
        CashSessionStatus.Completed => "Completed",
        _ => "No Session"
    };

    public string StatusColor => Status switch
    {
        CashSessionStatus.Open => "success",
        CashSessionStatus.Closed => "default",
        CashSessionStatus.Blocked => "error",
        CashSessionStatus.Pending => "warning",
        CashSessionStatus.DiscrepancyUnderReview => "warning",
        CashSessionStatus.Completed => "info",
        _ => "info"
    };
}
