using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Shared.Domain.Entities;

/// <summary>
/// Records a physical bank deposit made by an agent during a cash session.
/// A bank run transfers cash from the agent's cash wallet to a bank wallet.
/// The bank receipt details (denominations, reference number) are captured here.
/// </summary>
public class BankRun
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public long AgentId { get; set; }
    public long FromWalletId { get; set; }
    public long ToWalletId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";

    /// <summary>
    /// Bank deposit receipt / reference number.
    /// </summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>
    /// Binary content of the scanned / photographed bank deposit slip.
    /// </summary>
    public byte[]? ReceiptImage { get; set; }

    /// <summary>
    /// MIME type of the stored receipt image (e.g. "image/jpeg", "image/png").
    /// </summary>
    public string? ReceiptImageContentType { get; set; }

    public string? Notes { get; set; }
    public string RecordedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public CashSession? CashSession { get; set; }
    public Agent? Agent { get; set; }
    public Wallet? FromWallet { get; set; }
    public Wallet? ToWallet { get; set; }
    public ApplicationUser? RecordedByUser { get; set; }
}
