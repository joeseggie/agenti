using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Shared.Services;

internal sealed record RecordedWalletBalance(
    long Id, string Name, WalletTypeEnum Type, string Currency, decimal Amount, long? BankAccountId);

internal static class CashCountWalletReader
{
    internal static async Task<List<RecordedWalletBalance>> ReadAsync(
        ApplicationDbContext db, long countId, CancellationToken cancellationToken)
    {
        var snapshot = await db.CashCountSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.CashCountId == countId, cancellationToken);
        var wallets = snapshot == null
            ? await db.CashCountDetails.AsNoTracking().Where(d => d.CashCountId == countId)
                .Select(d => new RecordedWalletBalance(d.WalletId, d.Wallet!.Name,
                    d.Wallet.WalletType!.Type, d.Wallet.Currency, d.Amount, null))
                .ToListAsync(cancellationToken)
            : CountSnapshotCodec.Deserialize(snapshot.WalletsJson).Select(w =>
                new RecordedWalletBalance(w.WalletId, w.WalletName, w.WalletType, w.Currency, w.Amount, w.BankAccountId)).ToList();
        if (wallets.Count == 0 || wallets.Select(w => w.Id).Distinct().Count() != wallets.Count)
            throw new BankingRuleException("The cash count must contain distinct recorded wallet balances.");
        return wallets.OrderBy(w => w.Id).ToList();
    }
}
