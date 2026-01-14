namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Status of a vault transaction.
/// </summary>
public enum VaultTransactionStatus
{
    Pending = 1,
    Completed = 2,
    Rejected = 3,
    Expired = 4
}
