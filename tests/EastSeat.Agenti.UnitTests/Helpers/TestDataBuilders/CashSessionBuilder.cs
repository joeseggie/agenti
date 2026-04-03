using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating CashSession test data.
/// </summary>
public class CashSessionBuilder
{
    private long _id = 1;
    private long _agentId = 1;
    private long? _branchId = 1;
    private DateOnly _sessionDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private CashSessionStatus _status = CashSessionStatus.Open;
    private DateTimeOffset _openedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _closedAt;
    private DateTimeOffset? _blockedAt;
    private DateTimeOffset? _unblockedAt;
    private Agent? _agent;

    public CashSessionBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public CashSessionBuilder WithAgentId(long agentId)
    {
        _agentId = agentId;
        return this;
    }

    public CashSessionBuilder WithBranchId(long? branchId)
    {
        _branchId = branchId;
        return this;
    }

    public CashSessionBuilder WithSessionDate(DateOnly sessionDate)
    {
        _sessionDate = sessionDate;
        return this;
    }

    public CashSessionBuilder WithStatus(CashSessionStatus status)
    {
        _status = status;
        return this;
    }

    public CashSessionBuilder WithOpenedAt(DateTimeOffset openedAt)
    {
        _openedAt = openedAt;
        return this;
    }

    public CashSessionBuilder WithClosedAt(DateTimeOffset? closedAt)
    {
        _closedAt = closedAt;
        return this;
    }

    public CashSessionBuilder WithBlockedAt(DateTimeOffset? blockedAt)
    {
        _blockedAt = blockedAt;
        return this;
    }

    public CashSessionBuilder WithUnblockedAt(DateTimeOffset? unblockedAt)
    {
        _unblockedAt = unblockedAt;
        return this;
    }

    public CashSessionBuilder WithAgent(Agent agent)
    {
        _agent = agent;
        _agentId = agent.Id;
        return this;
    }

    public CashSessionBuilder AsOpen()
    {
        _status = CashSessionStatus.Open;
        _closedAt = null;
        return this;
    }

    public CashSessionBuilder AsClosed()
    {
        _status = CashSessionStatus.Closed;
        _closedAt = DateTimeOffset.UtcNow;
        return this;
    }

    public CashSession Build()
    {
        return new CashSession
        {
            Id = _id,
            AgentId = _agentId,
            BranchId = _branchId,
            SessionDate = _sessionDate,
            Status = _status,
            OpenedAt = _openedAt,
            ClosedAt = _closedAt,
            BlockedAt = _blockedAt,
            UnblockedAt = _unblockedAt,
            Agent = _agent
        };
    }

    public static CashSessionBuilder Default() => new();
}
