using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating Agent test data.
/// </summary>
public class AgentBuilder
{
    private long _id = 1;
    private string _userId = "user-123";
    private string _code = "AGNT";
    private long _branchId = 1;
    private bool _isActive = true;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private ApplicationUser? _user;
    private List<Wallet> _wallets = new();

    public AgentBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public AgentBuilder WithUserId(string userId)
    {
        _userId = userId;
        return this;
    }

    public AgentBuilder WithCode(string code)
    {
        _code = code;
        return this;
    }

    public AgentBuilder WithBranchId(long branchId)
    {
        _branchId = branchId;
        return this;
    }

    public AgentBuilder WithUser(ApplicationUser user)
    {
        _user = user;
        _userId = user.Id;
        return this;
    }

    public AgentBuilder WithWallets(params Wallet[] wallets)
    {
        _wallets.AddRange(wallets);
        return this;
    }

    public AgentBuilder IsInactive()
    {
        _isActive = false;
        return this;
    }

    public AgentBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public Agent Build()
    {
        var agent = new Agent
        {
            Id = _id,
            UserId = _userId,
            Code = _code,
            BranchId = _branchId,
            IsActive = _isActive,
            CreatedAt = _createdAt,
            User = _user
        };

        // Set wallets collection using reflection if needed
        if (_wallets.Any())
        {
            var walletsProperty = typeof(Agent).GetProperty(nameof(Agent.Wallets));
            walletsProperty?.SetValue(agent, _wallets);
        }

        return agent;
    }

    public static AgentBuilder Default() => new();
}
