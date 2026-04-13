namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Status of an erroneous transaction flag requiring investigation.
/// </summary>
public enum TransactionFlagStatus
{
    /// <summary>Flag submitted by agent, awaiting supervisor/admin review.</summary>
    PendingReview = 1,

    /// <summary>A supervisor/admin has acknowledged and is investigating.</summary>
    UnderInvestigation = 2,

    /// <summary>Investigation complete and the flag has been resolved.</summary>
    Resolved = 3,

    /// <summary>Flag was dismissed as not requiring action.</summary>
    Dismissed = 4
}
