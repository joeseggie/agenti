namespace EastSeat.Agenti.Shared.Services;

public interface IBusinessClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
    DateOnly GetBusinessDate(DateTimeOffset instant);
    DateTimeOffset ToBusinessTime(DateTimeOffset instant);
}
