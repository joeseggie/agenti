using EastSeat.Agenti.Web.Features.CashCounts;

namespace EastSeat.Agenti.Android.Models;

public static class CashCountDraftMerger
{
    public static void Refresh(BankingDraft<CashCountFormModel> draft, CashCountFormModel server)
    {
        var old = draft.Form;
        var sameCount = old.IsOpening == server.IsOpening && old.CountDate == server.CountDate &&
            old.CashCountId == server.CashCountId && old.CashSessionId == server.CashSessionId;
        var oldEntries = sameCount && old.WalletEntries.Select(w => w.WalletId).Distinct().Count() == old.WalletEntries.Count
            ? old.WalletEntries.ToDictionary(w => w.WalletId)
            : new Dictionary<long, WalletCountEntryDto>();

        foreach (var wallet in server.WalletEntries)
        {
            if (!oldEntries.TryGetValue(wallet.WalletId, out var saved)) continue;
            wallet.CountedAmount = saved.CountedAmount;
            wallet.ObservedAt = saved.ObservedAt;
            wallet.Denominations = saved.Denominations;
        }

        if (sameCount) server.Explanation = old.Explanation;
        foreach (var key in draft.Text.Keys.ToList())
        {
            if (!sameCount || ((key.StartsWith("amount-") || key.StartsWith("observed-")) &&
                (!long.TryParse(key[(key.IndexOf('-') + 1)..], out var id) ||
                 !oldEntries.ContainsKey(id) || !server.WalletEntries.Any(w => w.WalletId == id))))
                draft.Text.Remove(key);
        }
        draft.Form = server;
    }
}
