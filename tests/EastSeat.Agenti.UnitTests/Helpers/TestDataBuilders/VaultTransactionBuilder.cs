using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating VaultTransaction test data.
/// </summary>
public class VaultTransactionBuilder
{
    private long _id = 1;
    private long _vaultId = 1;
    private long? _cashSessionId;
    private decimal _amount = 100m;
    private VaultTransactionType _type = VaultTransactionType.ManualDeposit;
    private VaultTransactionStatus _status = VaultTransactionStatus.Pending;
    private decimal? _balanceAfter;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private string _createdByUserId = "user-123";
    private string? _approvedByUserId;
    private DateTimeOffset? _approvedAt;
    private DateTimeOffset? _expiresAt;
    private string? _notes = "Test transaction";
    private Vault? _vault;
    private CashSession? _cashSession;
    private ApplicationUser? _createdByUser;
    private ApplicationUser? _approvedByUser;

    public VaultTransactionBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public VaultTransactionBuilder WithVaultId(long vaultId)
    {
        _vaultId = vaultId;
        return this;
    }

    public VaultTransactionBuilder WithCashSessionId(long? cashSessionId)
    {
        _cashSessionId = cashSessionId;
        return this;
    }

    public VaultTransactionBuilder WithAmount(decimal amount)
    {
        _amount = amount;
        return this;
    }

    public VaultTransactionBuilder WithType(VaultTransactionType type)
    {
        _type = type;
        return this;
    }

    public VaultTransactionBuilder WithStatus(VaultTransactionStatus status)
    {
        _status = status;
        return this;
    }

    public VaultTransactionBuilder WithBalanceAfter(decimal? balanceAfter)
    {
        _balanceAfter = balanceAfter;
        return this;
    }

    public VaultTransactionBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public VaultTransactionBuilder WithCreatedByUserId(string userId)
    {
        _createdByUserId = userId;
        return this;
    }

    public VaultTransactionBuilder WithApprovedByUserId(string? userId)
    {
        _approvedByUserId = userId;
        return this;
    }

    public VaultTransactionBuilder WithApprovedAt(DateTimeOffset? approvedAt)
    {
        _approvedAt = approvedAt;
        return this;
    }

    public VaultTransactionBuilder WithExpiresAt(DateTimeOffset? expiresAt)
    {
        _expiresAt = expiresAt;
        return this;
    }

    public VaultTransactionBuilder WithNotes(string? notes)
    {
        _notes = notes;
        return this;
    }

    public VaultTransactionBuilder WithVault(Vault vault)
    {
        _vault = vault;
        _vaultId = vault.Id;
        return this;
    }

    public VaultTransactionBuilder WithCashSession(CashSession cashSession)
    {
        _cashSession = cashSession;
        _cashSessionId = cashSession.Id;
        return this;
    }

    public VaultTransactionBuilder WithCreatedByUser(ApplicationUser user)
    {
        _createdByUser = user;
        _createdByUserId = user.Id;
        return this;
    }

    public VaultTransactionBuilder WithApprovedByUser(ApplicationUser user)
    {
        _approvedByUser = user;
        _approvedByUserId = user.Id;
        return this;
    }

    public VaultTransactionBuilder AsPending()
    {
        _status = VaultTransactionStatus.Pending;
        _expiresAt = DateTimeOffset.UtcNow.AddHours(12);
        return this;
    }

    public VaultTransactionBuilder AsCompleted()
    {
        _status = VaultTransactionStatus.Completed;
        _approvedAt = DateTimeOffset.UtcNow;
        return this;
    }

    public VaultTransactionBuilder AsExpired()
    {
        _status = VaultTransactionStatus.Expired;
        _expiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        return this;
    }

    public VaultTransactionBuilder AsRejected()
    {
        _status = VaultTransactionStatus.Rejected;
        _approvedAt = DateTimeOffset.UtcNow;
        return this;
    }

    public VaultTransaction Build()
    {
        return new VaultTransaction
        {
            Id = _id,
            VaultId = _vaultId,
            CashSessionId = _cashSessionId,
            Amount = _amount,
            Type = _type,
            Status = _status,
            BalanceAfter = _balanceAfter,
            CreatedAt = _createdAt,
            CreatedByUserId = _createdByUserId,
            ApprovedByUserId = _approvedByUserId,
            ApprovedAt = _approvedAt,
            ExpiresAt = _expiresAt,
            Notes = _notes,
            Vault = _vault,
            CashSession = _cashSession,
            CreatedByUser = _createdByUser,
            ApprovedByUser = _approvedByUser
        };
    }

    public static VaultTransactionBuilder Default() => new();
}
