using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating CashCount test data.
/// </summary>
public class CashCountBuilder
{
    private long _id = 1;
    private long _cashSessionId = 1;
    private bool _isOpening = true;
    private decimal _totalAmount = 0m;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _submittedAt;
    private DateTimeOffset? _approvedAt;
    private CashSession? _cashSession;
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

    public CashCountBuilder WithIsOpening(bool isOpening)
    {
        _isOpening = isOpening;
        return this;
    }

    public CashCountBuilder WithTotalAmount(decimal totalAmount)
    {
        _totalAmount = totalAmount;
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
        return this;
    }

    public CashCountBuilder AsApproved()
    {
        _approvedAt = DateTimeOffset.UtcNow;
        return this;
    }

    public CashCount Build()
    {
        var cashCount = new CashCount
        {
            Id = _id,
            CashSessionId = _cashSessionId,
            IsOpening = _isOpening,
            TotalAmount = _totalAmount,
            CreatedAt = _createdAt,
            SubmittedAt = _submittedAt,
            ApprovedAt = _approvedAt,
            CashSession = _cashSession
        };

        // Set details collection using reflection if needed
        if (_details.Any())
        {
            var detailsProperty = typeof(CashCount).GetProperty(nameof(CashCount.Details));
            detailsProperty?.SetValue(cashCount, _details);
        }

        return cashCount;
    }

    public static CashCountBuilder Default() => new();
}
