namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Represents the approval status of a cash count.
/// </summary>
public enum CashCountStatus
{
    Draft = 0,
    PendingApproval = 1,
    Submitted = PendingApproval,
    Approved = 2,
    Rejected = 3
}
