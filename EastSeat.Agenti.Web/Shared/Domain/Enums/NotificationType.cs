namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Types of in-app notifications.
/// </summary>
public enum NotificationType
{
    SessionBlocked = 0,
    CountPendingApproval = 1,
    CountApproved = 2,
    CountRejected = 3,
    DiscrepancyPendingReview = 4,
    SessionClosed = 5,
    CountAutoApproved = 6,
    WalletAdjustmentRecorded = 7,
    TransactionFlagged = 8
}
