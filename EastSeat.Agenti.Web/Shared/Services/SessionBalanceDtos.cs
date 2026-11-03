using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Services;

public sealed record CountedWalletSnapshot(
    long WalletId,
    string WalletName,
    WalletTypeEnum WalletType,
    string Currency,
    decimal Amount,
    DateTimeOffset ObservedAt,
    long? BankAccountId);

public sealed class SessionWalletPosition
{
    public long WalletId { get; init; }
    public string WalletName { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal OpeningAmount { get; init; }
    public decimal CustomerMovements { get; set; }
    public decimal BankRuns { get; set; }
    public decimal Adjustments { get; set; }
    public decimal ReservedCustomerCash { get; set; }
    public decimal ReservedBankSettlement { get; set; }
    public decimal ExpectedBalance => OpeningAmount + CustomerMovements + BankRuns - Adjustments;
    public decimal AvailableForTransfer => ExpectedBalance - ReservedCustomerCash - ReservedBankSettlement;
}

public static class CountSnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyCollection<CountedWalletSnapshot> wallets) =>
        JsonSerializer.Serialize(wallets, Options);

    public static IReadOnlyList<CountedWalletSnapshot> Deserialize(string json)
    {
        List<CountedWalletSnapshot>? wallets;
        try
        {
            wallets = JsonSerializer.Deserialize<List<CountedWalletSnapshot>>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The recorded cash-count snapshot is invalid.", exception);
        }

        if (wallets == null || wallets.Count == 0 || wallets.Select(w => w.WalletId).Distinct().Count() != wallets.Count)
        {
            throw new InvalidOperationException("The recorded cash-count snapshot has missing or duplicate wallets.");
        }
        return wallets;
    }
}
