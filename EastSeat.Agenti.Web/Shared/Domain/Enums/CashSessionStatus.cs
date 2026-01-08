namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Represents the status of a daily cash session.
/// </summary>
public enum CashSessionStatus
{
    Closed = 0,
    Open = 1,
    Pending = 2,
    DiscrepancyUnderReview = 3,
    Completed = 4,
    Blocked = 5
}
