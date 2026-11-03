using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankReconciliationReview
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public BankReconciliationStatus Status { get; set; }
    public int Version { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
}
