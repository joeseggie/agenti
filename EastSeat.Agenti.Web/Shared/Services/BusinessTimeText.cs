using System.Globalization;

namespace EastSeat.Agenti.Shared.Services;

public static class BusinessTimeText
{
    public static string Format(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out DateTimeOffset instant)
    {
        var parsed = DateTimeOffset.TryParseExact((value?.Trim() ?? string.Empty) + " +03:00",
            "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
        instant = instant.ToUniversalTime();
        return parsed;
    }

    public static DateTimeOffset Parse(string value) => TryParse(value, out var instant)
        ? instant : throw new BankingRuleException("Enter date and time as yyyy-MM-dd HH:mm:ss in Uganda time.");
}
