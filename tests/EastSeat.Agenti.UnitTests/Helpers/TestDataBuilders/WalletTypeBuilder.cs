using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating WalletType test data.
/// </summary>
public class WalletTypeBuilder
{
    private long _id = 1;
    private string _name = "Cash";
    private string _description = "Cash wallet";
    private WalletTypeEnum _type = WalletTypeEnum.Cash;
    private bool _isSystem = false;
    private bool _isActive = true;
    private bool _supportsDenominations = false;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _updatedAt = null;
    private List<Wallet> _wallets = new();

    public WalletTypeBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public WalletTypeBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public WalletTypeBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public WalletTypeBuilder WithType(WalletTypeEnum type)
    {
        _type = type;
        return this;
    }

    public WalletTypeBuilder IsSystem()
    {
        _isSystem = true;
        return this;
    }

    public WalletTypeBuilder IsInactive()
    {
        _isActive = false;
        return this;
    }

    public WalletTypeBuilder WithSupportsDenominations(bool supportsDenominations)
    {
        _supportsDenominations = supportsDenominations;
        return this;
    }

    public WalletTypeBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public WalletTypeBuilder WithUpdatedAt(DateTimeOffset? updatedAt)
    {
        _updatedAt = updatedAt;
        return this;
    }

    public WalletTypeBuilder WithWallets(params Wallet[] wallets)
    {
        _wallets = wallets.ToList();
        return this;
    }

    public WalletType Build()
    {
        return new WalletType
        {
            Id = _id,
            Name = _name,
            Description = _description,
            Type = _type,
            IsSystem = _isSystem,
            IsActive = _isActive,
            SupportsDenominations = _supportsDenominations,
            CreatedAt = _createdAt,
            UpdatedAt = _updatedAt,
            Wallets = _wallets
        };
    }

    public static WalletTypeBuilder Default() => new();
}
