using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.CashSessions;

/// <summary>
/// DTO for displaying cash session in a list.
/// </summary>
public class CashSessionListItemDto
{
    public long Id { get; set; }
    public DateOnly SessionDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public CashSessionStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal OpeningTotal { get; set; }
    public decimal? ClosingTotal { get; set; }
    public decimal? Variance => ClosingTotal.HasValue ? ClosingTotal.Value - OpeningTotal : null;
}

/// <summary>
/// DTO for displaying cash session details with opening and closing counts.
/// </summary>
public class CashSessionDetailDto
{
    public long Id { get; set; }
    public DateOnly SessionDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public CashSessionStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal OpeningTotal { get; set; }
    public decimal? ClosingTotal { get; set; }
    public decimal? Variance => ClosingTotal.HasValue ? ClosingTotal.Value - OpeningTotal : null;
    public CashCountDetailDto? OpeningCount { get; set; }
    public CashCountDetailDto? ClosingCount { get; set; }
}

/// <summary>
/// DTO for displaying cash count details within a session.
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
/// DTO for displaying wallet count summary.
/// </summary>
public class WalletCountSummaryDto
{
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
