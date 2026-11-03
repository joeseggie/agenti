namespace EastSeat.Agenti.Shared.Services;

public sealed class BusinessClock(TimeProvider timeProvider) : IBusinessClock
{
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Kampala");

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    public DateOnly Today => GetBusinessDate(UtcNow);
    public DateOnly GetBusinessDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(ToBusinessTime(instant).DateTime);
    public DateTimeOffset ToBusinessTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, BusinessTimeZone);
}
