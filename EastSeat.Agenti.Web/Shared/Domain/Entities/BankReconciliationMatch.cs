namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankReconciliationMatch
{
    public long Id { get; set; }
    public long BankTransactionId { get; set; }
    public long? CustomerMovementId { get; set; }
    public long? BankRunId { get; set; }
    public long? WalletAdjustmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
    public string MatchedByUserId { get; set; } = string.Empty;
    public DateTimeOffset MatchedAt { get; set; }
    public BankTransaction? BankTransaction { get; set; }
    public CustomerMovement? CustomerMovement { get; set; }
    public BankRun? BankRun { get; set; }
    public WalletAdjustment? WalletAdjustment { get; set; }
}
