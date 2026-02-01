using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating Vault test data.
/// </summary>
public class VaultBuilder
{
    private long _id = 1;
    private long _branchId = 1;
    private decimal _currentBalance = 0m;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _updatedAt;
    private Branch? _branch;

    public VaultBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public VaultBuilder WithBranchId(long branchId)
    {
        _branchId = branchId;
        return this;
    }

    public VaultBuilder WithCurrentBalance(decimal balance)
    {
        _currentBalance = balance;
        return this;
    }

    public VaultBuilder WithBranch(Branch branch)
    {
        _branch = branch;
        _branchId = branch.Id;
        return this;
    }

    public VaultBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public VaultBuilder WithUpdatedAt(DateTimeOffset updatedAt)
    {
        _updatedAt = updatedAt;
        return this;
    }

    public Vault Build()
    {
        return new Vault
        {
            Id = _id,
            BranchId = _branchId,
            CurrentBalance = _currentBalance,
            CreatedAt = _createdAt,
            UpdatedAt = _updatedAt,
            Branch = _branch
        };
    }

    public static VaultBuilder Default() => new();
}
