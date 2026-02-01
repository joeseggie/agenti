using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating Wallet test data.
/// </summary>
public class WalletBuilder
{
    private long _id = 1;
    private long _agentId = 1;
    private long _walletTypeId = 1;
    private string _name = "Test Wallet";
    private string _currency = "UGX";
    private decimal _balance = 0m;
    private bool _isActive = true;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private WalletType? _walletType;

    public WalletBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public WalletBuilder WithAgentId(long agentId)
    {
        _agentId = agentId;
        return this;
    }

    public WalletBuilder WithWalletTypeId(long walletTypeId)
    {
        _walletTypeId = walletTypeId;
        return this;
    }

    public WalletBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public WalletBuilder WithCurrency(string currency)
    {
        _currency = currency;
        return this;
    }

    public WalletBuilder WithBalance(decimal balance)
    {
        _balance = balance;
        return this;
    }

    public WalletBuilder WithWalletType(WalletType walletType)
    {
        _walletType = walletType;
        _walletTypeId = walletType.Id;
        return this;
    }

    public WalletBuilder IsInactive()
    {
        _isActive = false;
        return this;
    }

    public WalletBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public Wallet Build()
    {
        return new Wallet
        {
            Id = _id,
            AgentId = _agentId,
            WalletTypeId = _walletTypeId,
            Name = _name,
            Currency = _currency,
            Balance = _balance,
            IsActive = _isActive,
            CreatedAt = _createdAt,
            WalletType = _walletType
        };
    }

    public static WalletBuilder Default() => new();
}
