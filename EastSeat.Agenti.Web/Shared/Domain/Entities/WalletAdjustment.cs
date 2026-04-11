using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Records a debit-only withdrawal from an agent's wallet during a cash session
/// where there is no corresponding credit to another wallet.
/// </summary>
public class WalletAdjustment
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public long WalletId { get; set; }
    public long AgentId { get; set; }
    public WalletAdjustmentReason Reason { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";
    public string? Notes { get; set; }
    public string RecordedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public CashSession? CashSession { get; set; }
    public Wallet? Wallet { get; set; }
    public Agent? Agent { get; set; }
    public ApplicationUser? RecordedByUser { get; set; }
}
