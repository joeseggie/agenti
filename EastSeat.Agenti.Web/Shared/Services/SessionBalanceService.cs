using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Shared.Services;

public sealed class SessionBalanceService(ApplicationDbContext db, IBusinessClock clock) : ISessionBalanceService
{
    public Task<List<SessionWalletPosition>> GetPositionsAsync(
        long sessionId, long agentId, bool includePendingAdjustments = false, CancellationToken cancellationToken = default) =>
        CalculateAsync(sessionId, agentId, null, null, includePendingAdjustments, cancellationToken);

    public async Task<List<SessionWalletPosition>> GetPositionsAtSnapshotAsync(
        long snapshotId, bool includePendingAdjustments = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await db.CashCountSnapshots.AsNoTracking()
            .Include(s => s.CashCount)
            .SingleOrDefaultAsync(s => s.Id == snapshotId, cancellationToken)
            ?? throw new InvalidOperationException("Cash-count snapshot not found.");
        var cutoffs = CountSnapshotCodec.Deserialize(snapshot.WalletsJson)
            .ToDictionary(w => w.WalletId, w => w.ObservedAt);
        return await CalculateAsync(snapshot.CashCount!.CashSessionId, snapshot.CashCount.AgentId,
            cutoffs, snapshot.CapturedAt, includePendingAdjustments, cancellationToken);
    }

    private async Task<List<SessionWalletPosition>> CalculateAsync(
        long sessionId, long agentId, IReadOnlyDictionary<long, DateTimeOffset>? cutoffs,
        DateTimeOffset? caseCutoff, bool includePendingAdjustments, CancellationToken cancellationToken)
    {
        var opening = await db.CashCountSnapshots.AsNoTracking()
            .Where(s => s.CashCount!.CashSessionId == sessionId && s.CashCount.AgentId == agentId &&
                        s.CashCount.IsOpening && s.CashCount.Status == CashCountStatus.Approved)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("An approved opening snapshot is required to calculate session balances.");
        var wallets = CountSnapshotCodec.Deserialize(opening.WalletsJson);
        var openingWalletIds = wallets.Select(w => w.WalletId).ToArray();
        var positions = wallets.ToDictionary(w => w.WalletId, w => new SessionWalletPosition
        {
            WalletId = w.WalletId, WalletName = w.WalletName, Currency = w.Currency, OpeningAmount = w.Amount
        });
        var startingAt = wallets.ToDictionary(w => w.WalletId, w => w.ObservedAt);
        var now = clock.UtcNow;
        if (cutoffs != null && wallets.Any(w => !cutoffs.TryGetValue(w.WalletId, out var cutoff) || cutoff < w.ObservedAt))
        {
            throw new InvalidOperationException("The closing snapshot has missing wallets or a cutoff before opening.");
        }

        bool Includes(long walletId, DateTimeOffset instant, bool bankOrigin = false)
        {
            if (!startingAt.TryGetValue(walletId, out var start))
            {
                throw new InvalidOperationException("A session movement refers to a wallet outside its opening snapshot.");
            }
            var end = cutoffs == null ? now : cutoffs.TryGetValue(walletId, out var cutoff)
                ? cutoff
                : throw new InvalidOperationException("A closing snapshot is missing an opening wallet.");
            // Agent entries have explicit session attribution; bank-origin reversals follow their posting interval.
            return (!bankOrigin || instant > start) && instant <= end;
        }

        var movements = await db.CustomerMovements.AsNoTracking()
            .Where(m => m.CustomerTransaction!.AgentId == agentId &&
                        (m.CashSessionId == sessionId ||
                         (openingWalletIds.Contains(m.CustomerTransaction.BankAccount!.WalletId) &&
                          m.CashEffect == 0 && (m.Kind == CustomerMovementKind.BankReversal ||
                                               m.Kind == CustomerMovementKind.BankPostingObserved))) &&
                        !db.CustomerMovements.Any(next => next.SupersedesMovementId == m.Id))
            .Select(m => new
            {
                m.CashEffect, m.BankEffect, m.OccurredAt, m.Kind, m.CustomerTransaction!.CashWalletId,
                BankWalletId = m.CustomerTransaction.BankAccount!.WalletId
            })
            .ToListAsync(cancellationToken);
        foreach (var movement in movements)
        {
            if (movement.CashEffect != 0 && Includes(movement.CashWalletId, movement.OccurredAt))
                positions[movement.CashWalletId].CustomerMovements += movement.CashEffect;
            if (movement.BankEffect != 0 &&
                Includes(movement.BankWalletId, movement.OccurredAt,
                    movement.Kind is CustomerMovementKind.BankReversal or CustomerMovementKind.BankPostingObserved))
                positions[movement.BankWalletId].CustomerMovements += movement.BankEffect;
        }

        var bankRuns = await db.BankRuns.AsNoTracking()
            .Where(r => r.CashSessionId == sessionId && r.AgentId == agentId)
            .Select(r => new { r.FromWalletId, r.ToWalletId, r.Amount, OccurredAt = r.OccurredAt ?? r.CreatedAt })
            .ToListAsync(cancellationToken);
        foreach (var run in bankRuns)
        {
            if (Includes(run.FromWalletId, run.OccurredAt))
                positions[run.FromWalletId].BankRuns -= run.Amount;
            if (Includes(run.ToWalletId, run.OccurredAt))
                positions[run.ToWalletId].BankRuns += run.Amount;
        }

        var adjustments = await db.WalletAdjustments.AsNoTracking()
            .Where(a => a.CashSessionId == sessionId && a.AgentId == agentId &&
                        (a.Status == WalletAdjustmentStatus.Approved ||
                         (includePendingAdjustments && a.Status == WalletAdjustmentStatus.Pending)))
            .Select(a => new { a.WalletId, a.Amount, OccurredAt = a.OccurredAt ?? a.CreatedAt })
            .ToListAsync(cancellationToken);
        foreach (var adjustment in adjustments)
        {
            if (Includes(adjustment.WalletId, adjustment.OccurredAt))
                positions[adjustment.WalletId].Adjustments += adjustment.Amount;
        }

        var customerCash = await db.CustomerMovements.AsNoTracking()
            .Where(m => m.CustomerTransaction!.AgentId == agentId &&
                        !db.CustomerMovements.Any(next => next.SupersedesMovementId == m.Id))
            .Select(m => new
            {
                m.CustomerTransactionId, m.CustomerTransaction!.CashWalletId,
                BankWalletId = m.CustomerTransaction.BankAccount!.WalletId,
                m.Kind, m.CashEffect, m.BankEffect, m.CustomerSettledAmount, m.OccurredAt,
                m.RelatedMovementId, m.TransactionKind, OriginalKind = m.CustomerTransaction.Kind
            })
            .ToListAsync(cancellationToken);
        foreach (var customer in customerCash.GroupBy(m => m.CustomerTransactionId))
        {
            var walletId = customer.First().CashWalletId;
            var end = caseCutoff ?? now;
            var occurred = customer.Where(m => m.OccurredAt <= end).ToList();
            var primary = occurred.SingleOrDefault(m => m.RelatedMovementId == null &&
                m.Kind is CustomerMovementKind.Completed or CustomerMovementKind.FailedAttempt or CustomerMovementKind.PendingAttempt or CustomerMovementKind.Voided);
            if (primary == null)
                continue;
            var completedAmount = occurred.Sum(m => m.CustomerSettledAmount);
            var kind = primary.TransactionKind ?? primary.OriginalKind;
            if (kind == CustomerTransactionKind.Deposit && positions.TryGetValue(walletId, out var position))
                position.ReservedCustomerCash += Math.Max(0, occurred.Sum(m => m.CashEffect) - completedAmount);
            if (positions.TryGetValue(primary.BankWalletId, out var bankPosition))
            {
                var expectedBankEffect = kind == CustomerTransactionKind.Deposit ? -completedAmount : completedAmount;
                bankPosition.ReservedBankSettlement += Math.Max(0, occurred.Sum(m => m.BankEffect) - expectedBankEffect);
            }
        }

        return positions.Values.OrderBy(p => p.WalletName).ToList();
    }
}
