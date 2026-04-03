namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Represents the approval status of a cash count.
/// </summary>
public enum CashCountStatus
{
    Draft = 0,
    Submitted = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4
}
