namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Resolution status of a pending transaction.
/// </summary>
public enum PendingTransactionStatus
{
    /// <summary>Recorded but not yet reported to the bank's customer service centre.</summary>
    Open = 1,

    /// <summary>Reported to the bank; a ticket number has been assigned.</summary>
    ReportedToBank = 2,

    /// <summary>The bank has resolved the issue (funds credited or confirmed).</summary>
    Resolved = 3,

    /// <summary>Closed without resolution (e.g. duplicate entry, operator error).</summary>
    Cancelled = 4
}
