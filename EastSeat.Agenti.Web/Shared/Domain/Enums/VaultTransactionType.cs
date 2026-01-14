namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Types of vault movements.
/// </summary>
public enum VaultTransactionType
{
    Opening = 1,
    Closing = 2,
    ManualDeposit = 3,
    ManualWithdrawal = 4,
    Adjustment = 5
}
