using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating CashCount test data.
/// </summary>
public class CashCountBuilder
{
    private long _id = 1;
    private long _cashSessionId = 1;
    private long _agentId = 1;
    private bool _isOpening = true;
    private CashCountStatus _status = CashCountStatus.Draft;
    private DateOnly _countDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private decimal _totalAmount = 0m;
    private string? _explanation;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _submittedAt;
    private DateTimeOffset? _approvedAt;
    private string? _approvedByUserId;
    private CashSession? _cashSession;
    private Agent? _agent;
    private List<CashCountDetail> _details = new();

    public CashCountBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public CashCountBuilder WithCashSessionId(long cashSessionId)
    {
        _cashSessionId = cashSessionId;
        return this;
    }

    public CashCountBuilder WithAgentId(long agentId)
    {
        _agentId = agentId;
        return this;
    }

    public CashCountBuilder WithIsOpening(bool isOpening)
    {
        _isOpening = isOpening;
        return this;
    }

    public CashCountBuilder WithStatus(CashCountStatus status)
    {
        _status = status;
        return this;
    }

    public CashCountBuilder WithCountDate(DateOnly countDate)
    {
        _countDate = countDate;
        return this;
    }

    public CashCountBuilder WithTotalAmount(decimal totalAmount)
    {
        _totalAmount = totalAmount;
        return this;
    }

    public CashCountBuilder WithExplanation(string? explanation)
    {
        _explanation = explanation;
        return this;
    }

    public CashCountBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public CashCountBuilder WithSubmittedAt(DateTimeOffset? submittedAt)
    {
        _submittedAt = submittedAt;
        return this;
    }

    public CashCountBuilder WithApprovedAt(DateTimeOffset? approvedAt)
    {
        _approvedAt = approvedAt;
        return this;
    }

    public CashCountBuilder WithCashSession(CashSession cashSession)
    {
        _cashSession = cashSession;
        _cashSessionId = cashSession.Id;
        return this;
    }

    public CashCountBuilder WithAgent(Agent agent)
    {
        _agent = agent;
        _agentId = agent.Id;
        return this;
    }

    public CashCountBuilder WithDetails(params CashCountDetail[] details)
    {
        _details.AddRange(details);
        return this;
    }

    public CashCountBuilder AsOpening()
    {
        _isOpening = true;
        return this;
    }

    public CashCountBuilder AsClosing()
    {
        _isOpening = false;
        return this;
    }

    public CashCountBuilder AsSubmitted()
    {
        _submittedAt = DateTimeOffset.UtcNow;
        _status = CashCountStatus.PendingApproval;
        return this;
    }

    public CashCountBuilder AsApproved()
    {
        _approvedAt = DateTimeOffset.UtcNow;
        _submittedAt ??= DateTimeOffset.UtcNow;
        _status = CashCountStatus.Approved;
        return this;
    }

    public CashCount Build()
    {
        var cashCount = new CashCount
        {
            Id = _id,
            CashSessionId = _cashSessionId,
            AgentId = _agentId,
            IsOpening = _isOpening,
            Status = _status,
            CountDate = _countDate,
            TotalAmount = _totalAmount,
            Explanation = _explanation,
            CreatedAt = _createdAt,
            SubmittedAt = _submittedAt,
            ApprovedAt = _approvedAt,
            ApprovedByUserId = _approvedByUserId,
            CashSession = _cashSession,
            Agent = _agent
        };

        if (_details.Any())
        {
            var detailsProperty = typeof(CashCount).GetProperty(nameof(CashCount.Details));
            detailsProperty?.SetValue(cashCount, _details);
        }

        return cashCount;
    }

    public static CashCountBuilder Default() => new();
}
