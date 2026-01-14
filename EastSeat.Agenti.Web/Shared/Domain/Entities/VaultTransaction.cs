using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Represents a vault movement (opening/closing session or manual adjustment).
/// </summary>
public class VaultTransaction
{
    public long Id { get; set; }
    public long VaultId { get; set; }
    public long? CashSessionId { get; set; }
    public decimal Amount { get; set; }
    public VaultTransactionType Type { get; set; }
    public VaultTransactionStatus Status { get; set; }
    public decimal? BalanceAfter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Vault? Vault { get; set; }
    public CashSession? CashSession { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
}
