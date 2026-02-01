using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating CashCountDetail test data.
/// </summary>
public class CashCountDetailBuilder
{
    private long _id = 1;
    private long _cashCountId = 1;
    private long _walletId = 1;
    private decimal _amount = 0m;
    private string? _denominations;
    private CashCount? _cashCount;
    private Wallet? _wallet;

    public CashCountDetailBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public CashCountDetailBuilder WithCashCountId(long cashCountId)
    {
        _cashCountId = cashCountId;
        return this;
    }

    public CashCountDetailBuilder WithWalletId(long walletId)
    {
        _walletId = walletId;
        return this;
    }

    public CashCountDetailBuilder WithAmount(decimal amount)
    {
        _amount = amount;
        return this;
    }

    public CashCountDetailBuilder WithDenominations(string? denominations)
    {
        _denominations = denominations;
        return this;
    }

    public CashCountDetailBuilder WithCashCount(CashCount cashCount)
    {
        _cashCount = cashCount;
        _cashCountId = cashCount.Id;
        return this;
    }

    public CashCountDetailBuilder WithWallet(Wallet wallet)
    {
        _wallet = wallet;
        _walletId = wallet.Id;
        return this;
    }

    public CashCountDetail Build()
    {
        return new CashCountDetail
        {
            Id = _id,
            CashCountId = _cashCountId,
            WalletId = _walletId,
            Amount = _amount,
            Denominations = _denominations,
            CashCount = _cashCount,
            Wallet = _wallet
        };
    }

    public static CashCountDetailBuilder Default() => new();
}
