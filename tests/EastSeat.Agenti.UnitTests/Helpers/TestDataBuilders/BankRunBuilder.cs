using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating BankRun test data.
/// </summary>
public class BankRunBuilder
{
    private long _id = 1;
    private long _cashSessionId = 1;
    private long _agentId = 1;
    private long _fromWalletId = 1;
    private long _toWalletId = 2;
    private decimal _amount = 100_000m;
    private string _currency = "UGX";
    private string? _denominations;
    private string? _receiptNumber;
    private string? _notes;
    private string _recordedByUserId = "user1";
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    public BankRunBuilder WithId(long id) { _id = id; return this; }
    public BankRunBuilder WithCashSessionId(long cashSessionId) { _cashSessionId = cashSessionId; return this; }
    public BankRunBuilder WithAgentId(long agentId) { _agentId = agentId; return this; }
    public BankRunBuilder WithFromWalletId(long fromWalletId) { _fromWalletId = fromWalletId; return this; }
    public BankRunBuilder WithToWalletId(long toWalletId) { _toWalletId = toWalletId; return this; }
    public BankRunBuilder WithAmount(decimal amount) { _amount = amount; return this; }
    public BankRunBuilder WithReceiptNumber(string? receiptNumber) { _receiptNumber = receiptNumber; return this; }
    public BankRunBuilder WithNotes(string? notes) { _notes = notes; return this; }
    public BankRunBuilder WithRecordedByUserId(string userId) { _recordedByUserId = userId; return this; }
    public BankRunBuilder WithDenominations(string? denominations) { _denominations = denominations; return this; }

    public BankRun Build() => new()
    {
        Id = _id,
        CashSessionId = _cashSessionId,
        AgentId = _agentId,
        FromWalletId = _fromWalletId,
        ToWalletId = _toWalletId,
        Amount = _amount,
        Currency = _currency,
        Denominations = _denominations,
        ReceiptNumber = _receiptNumber,
        Notes = _notes,
        RecordedByUserId = _recordedByUserId,
        CreatedAt = _createdAt
    };

    public static BankRunBuilder Default() => new();
}
