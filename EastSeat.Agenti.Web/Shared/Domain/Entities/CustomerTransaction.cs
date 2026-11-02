using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class CustomerTransaction : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public long AgentId { get; set; }
    public long OriginatingSessionId { get; set; }
    public long CashWalletId { get; set; }
    public CustomerTransactionKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";
    public string CustomerIdentifier { get; set; } = string.Empty;
    public Guid CommandId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string RecordedByUserId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
    public Agent? Agent { get; set; }
    public CashSession? OriginatingSession { get; set; }
    public Wallet? CashWallet { get; set; }
    public ICollection<CustomerMovement> Movements { get; set; } = [];
}
