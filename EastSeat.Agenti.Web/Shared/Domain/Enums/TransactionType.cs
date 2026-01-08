namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Represents the type of transaction in the system.
/// </summary>
public enum TransactionType
{
    Deposit = 1,
    Withdrawal = 2,
    Transfer = 3,
    Adjustment = 4,
    Reversal = 5
}
