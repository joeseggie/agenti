namespace EastSeat.Agenti.Shared.Domain.Entities;

public class CashCountCorrectionRequest
{
    public long Id { get; set; }
    public long CashCountId { get; set; }
    public long AgentId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string AllowedMovementIdsJson { get; set; } = "[]";
    public bool AllowMissingTransactions { get; set; }
    public bool IsOpen { get; set; } = true;
    public int Version { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResubmittedAt { get; set; }
    public CashCount? CashCount { get; set; }
    public Agent? Agent { get; set; }
}
