namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankStatementOccurrence : IImmutableBankingRecord
{
    public long Id { get; set; }
    public long BankTransactionId { get; set; }
    public long StatementRowId { get; set; }
    public BankTransaction? BankTransaction { get; set; }
    public BankStatementRow? StatementRow { get; set; }
}
