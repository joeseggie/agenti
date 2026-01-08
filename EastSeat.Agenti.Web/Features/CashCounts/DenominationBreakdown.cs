using System.Text.Json;
using System.Text.Json.Serialization;

namespace EastSeat.Agenti.Web.Features.CashCounts;

/// <summary>
/// Represents a denomination breakdown for UGX currency.
/// Notes: 50000, 20000, 10000, 5000, 2000, 1000
/// Coins: 1000, 500, 200, 100
/// </summary>
public record DenominationBreakdown
{
    /// <summary>
    /// Note denominations and their quantities.
    /// Keys: "50000", "20000", "10000", "5000", "2000", "1000"
    /// </summary>
    [JsonPropertyName("notes")]
    public Dictionary<string, int> Notes { get; init; } = new()
    {
        ["50000"] = 0,
        ["20000"] = 0,
        ["10000"] = 0,
        ["5000"] = 0,
        ["2000"] = 0,
        ["1000"] = 0
    };

    /// <summary>
    /// Coin denominations and their quantities.
    /// Keys: "1000", "500", "200", "100"
    /// </summary>
    [JsonPropertyName("coins")]
    public Dictionary<string, int> Coins { get; init; } = new()
    {
        ["1000"] = 0,
        ["500"] = 0,
        ["200"] = 0,
        ["100"] = 0
    };

    /// <summary>
    /// Calculates the total value of all notes.
    /// </summary>
    public decimal NotesTotal => Notes.Sum(n => int.Parse(n.Key) * n.Value);

    /// <summary>
    /// Calculates the total value of all coins.
    /// </summary>
    public decimal CoinsTotal => Coins.Sum(c => int.Parse(c.Key) * c.Value);

    /// <summary>
    /// Calculates the grand total of notes and coins.
    /// </summary>
    public decimal Total => NotesTotal + CoinsTotal;

    /// <summary>
    /// Serializes the breakdown to JSON string.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Deserializes a JSON string to DenominationBreakdown.
    /// </summary>
    public static DenominationBreakdown? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DenominationBreakdown>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new empty breakdown with all denominations set to zero.
    /// </summary>
    public static DenominationBreakdown Empty => new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

/// <summary>
/// Static helper for UGX denomination values.
/// </summary>
public static class UgxDenominations
{
    /// <summary>
    /// Note denominations in descending order.
    /// </summary>
    public static readonly int[] Notes = [50000, 20000, 10000, 5000, 2000, 1000];

    /// <summary>
    /// Coin denominations in descending order.
    /// </summary>
    public static readonly int[] Coins = [1000, 500, 200, 100];

    /// <summary>
    /// All denominations (notes + coins) in descending order.
    /// </summary>
    public static readonly int[] All = [50000, 20000, 10000, 5000, 2000, 1000, 500, 200, 100];
}
