using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankTransaction
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public long AcceptedRowId { get; set; }
    public string? ReliableIdentity { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public BankTransactionClassification Classification { get; set; }
    public string? ClassificationReason { get; set; }
    public long? RelatedCustomerTransactionId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public BankAccount? BankAccount { get; set; }
    public BankStatementRow? AcceptedRow { get; set; }
    public CustomerTransaction? RelatedCustomerTransaction { get; set; }
}
