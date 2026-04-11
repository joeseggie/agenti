namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Reasons for debit-only wallet adjustments during a cash session.
/// </summary>
public enum WalletAdjustmentReason
{
    /// <summary>Bank counted less cash than the stated deposit amount.</summary>
    BankShortage = 1,

    /// <summary>Bank confiscated fake notes during a deposit run.</summary>
    FakeNotes = 2,

    /// <summary>Owner requested a payment from the agent's wallet.</summary>
    OwnerPayment = 3,

    /// <summary>Customer has not paid before closing cash count.</summary>
    UnpaidCustomer = 4,

    /// <summary>Other reason (notes required).</summary>
    Other = 99
}
