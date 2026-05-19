namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Represents an action recorded against a cash count for audit history.
/// </summary>
public enum CashCountAuditAction
{
    Created = 0,
    Saved = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Unapproved = 5,
    AutoApproved = 6
}
