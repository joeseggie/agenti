namespace EastSeat.Agenti.Shared.Services;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public sealed record PageRequest
{
    public int Page { get; }
    public int PageSize { get; }
    public int Offset => Page * PageSize;

    public PageRequest(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, int.MaxValue / pageSize);
        Page = page;
        PageSize = pageSize;
    }
}
