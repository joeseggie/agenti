namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// The type of failed / pending transaction captured.
/// </summary>
public enum PendingTransactionType
{
    /// <summary>
    /// Money was deducted from the agent's wallet but was not reflected on the customer's account.
    /// </summary>
    OutboundTransferFailed = 1,

    /// <summary>
    /// Self-liquidation failed: the destination account is the agent's own other wallet,
    /// but the transaction did not complete due to a backend failure.
    /// </summary>
    SelfLiquidationFailed = 2
}
