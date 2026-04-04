using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.CashSessions;

/// <summary>
/// DTO for displaying cash session in a list (branch-level, multi-agent).
/// </summary>
public class CashSessionListItemDto
{
    public long Id { get; set; }
    public DateOnly SessionDate { get; set; }
    public CashSessionStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int AgentCount { get; set; }
    public int ApprovedClosingCount { get; set; }
    public decimal TotalOpeningAmount { get; set; }
    public decimal? TotalClosingAmount { get; set; }
    public bool AllClosingCountsApproved { get; set; }
    public int PendingApprovalCount { get; set; }
}

/// <summary>
/// DTO for displaying cash session details with all agents' counts.
/// </summary>
public class CashSessionDetailDto
{
    public long Id { get; set; }
    public DateOnly SessionDate { get; set; }
    public CashSessionStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal TotalOpeningAmount { get; set; }
    public decimal? TotalClosingAmount { get; set; }
    public List<AgentSessionSummaryDto> AgentSummaries { get; set; } = [];
}

/// <summary>
/// DTO for a single agent's participation in a session.
/// </summary>
public class AgentSessionSummaryDto
{
    public long AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public CashCountSummaryDto? OpeningCount { get; set; }
    public CashCountSummaryDto? ClosingCount { get; set; }
    public decimal? Variance => ClosingCount != null && OpeningCount != null
        ? ClosingCount.TotalAmount - OpeningCount.TotalAmount
        : null;
    public bool HasDiscrepancy => Variance.HasValue && Variance.Value != 0;
}

/// <summary>
/// DTO for displaying a cash count within a session detail.
/// </summary>
public class CashCountSummaryDto
{
    public long Id { get; set; }
    public CashCountStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? Explanation { get; set; }
    public string? RejectionReason { get; set; }
    public List<WalletCountSummaryDto> WalletEntries { get; set; } = [];
}

/// <summary>
/// DTO for displaying wallet count summary.
/// </summary>
public class WalletCountSummaryDto
{
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
