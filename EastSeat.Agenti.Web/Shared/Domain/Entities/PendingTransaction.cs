using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Records a failed / pending transaction that occurred during a cash session.
/// Two scenarios are covered:
///   1. Outbound transfer failed – money was deducted from the agent's wallet but
///      never reflected in the customer's account.
///   2. Self-liquidation failed – a transfer to the agent's own wallet did not
///      complete due to a backend failure.
/// </summary>
public class PendingTransaction
{
    public long Id { get; set; }

    /// <summary>The cash session during which the failure occurred.</summary>
    public long CashSessionId { get; set; }

    /// <summary>The agent who recorded the pending transaction.</summary>
    public long AgentId { get; set; }

    /// <summary>The source wallet that was debited.</summary>
    public long WalletId { get; set; }

    public PendingTransactionType Type { get; set; }
    public PendingTransactionStatus Status { get; set; } = PendingTransactionStatus.Open;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";

    /// <summary>Destination account or phone number (customer or agent's other wallet).</summary>
    public string? CustomerAccountNumber { get; set; }

    /// <summary>Incident ticket number issued by the bank's customer service centre.</summary>
    public string? TicketNumber { get; set; }

    /// <summary>Relative path to the uploaded receipt photo (e.g. uploads/pending-transactions/abc.jpg).</summary>
    public string? ReceiptPhotoPath { get; set; }

    public string Notes { get; set; } = string.Empty;

    /// <summary>Additional notes provided when resolving or cancelling the transaction.</summary>
    public string? ResolutionNotes { get; set; }

    public string RecordedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    // Navigation properties
    public CashSession? CashSession { get; set; }
    public Agent? Agent { get; set; }
    public Wallet? Wallet { get; set; }
    public ApplicationUser? RecordedByUser { get; set; }
}
