using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService
{
    private async Task PopulateClosingApprovalsAsync(
        BankReconciliationReviewDto report, ReviewFacts facts, CancellationToken cancellationToken,
        bool synchronizeAggregateStatus)
    {
        var sessionIds = report.Sessions.Select(s => s.CashSessionId).ToList();
        var approvals = await db.BankReconciliationResults.AsNoTracking().Include(r => r.Review)
            .Where(r => sessionIds.Contains(r.CashSessionId) &&
                r.Review!.BankAccount!.BranchId == facts.Account.BranchId &&
                r.Review.BankAccount.Wallet!.AgentId == facts.Account.Wallet!.AgentId)
            .OrderByDescending(r => r.Id).ToListAsync(cancellationToken);
        var evaluated = new Dictionary<long, BankReconciliationReviewDto> { [report.Id] = report };

        foreach (var session in report.Sessions)
        {
            var snapshot = facts.Snapshots.SingleOrDefault(s => s.Id == session.ClosingSnapshotId);
            if (snapshot == null)
                continue;
            var banks = CountSnapshotCodec.Deserialize(snapshot.WalletsJson)
                .Where(w => w.WalletType == WalletTypeEnum.Bank).ToList();
            var currentApprovals = new List<BankReconciliationResult>();
            foreach (var bank in banks)
            {
                var approval = approvals.FirstOrDefault(a => a.CashSessionId == session.CashSessionId &&
                    a.Review!.BankAccountId == bank.BankAccountId);
                var current = false;
                if (approval != null && approval.SnapshotId == snapshot.Id &&
                    approval.RegisterVersion == session.RegisterVersion)
                {
                    if (!evaluated.TryGetValue(approval.ReviewId, out var accountReport))
                    {
                        accountReport = await EvaluateAsync(approval.Review!, cancellationToken, includeClosingApprovals: false);
                        evaluated.Add(approval.ReviewId, accountReport);
                    }
                    var accountSession = accountReport.Sessions.SingleOrDefault(s => s.CashSessionId == session.CashSessionId);
                    current = accountSession?.CanApprove == true &&
                        accountSession.ClosingSnapshotId == snapshot.Id &&
                        accountSession.BasisHash == approval.BasisHash;
                }
                if (current)
                    currentApprovals.Add(approval!);
                if (bank.BankAccountId == report.BankAccountId)
                    session.BankApprovalCurrent = current;
                if (!current)
                    session.PendingBankWallets.Add(bank.BankAccountId.HasValue
                        ? bank.WalletName : $"{bank.WalletName} (missing account binding in the closing snapshot)");
            }
            if (banks.Count == 0 || !synchronizeAggregateStatus || !session.ClosingCountId.HasValue)
                continue;

            var count = await db.CashCounts.SingleAsync(c => c.Id == session.ClosingCountId.Value, cancellationToken);
            await db.Entry(count).ReloadAsync(cancellationToken);
            if (count.Status is CashCountStatus.Approved or CashCountStatus.PendingApproval)
            {
                var previousStatus = count.Status;
                var newStatus = session.PendingBankWallets.Count == 0
                    ? CashCountStatus.Approved
                    : CashCountStatus.PendingApproval;
                if (newStatus != previousStatus)
                {
                    count.Status = newStatus;
                    if (newStatus == CashCountStatus.Approved)
                    {
                        var latestApproval = currentApprovals.MaxBy(a => a.Id)!;
                        count.ApprovedAt = latestApproval.ReviewedAt;
                        count.ApprovedByUserId = latestApproval.ReviewedByUserId;
                    }
                    db.CashCountAuditLogs.Add(new CashCountAuditLog
                    {
                        CashCountId = count.Id,
                        CashSessionId = count.CashSessionId,
                        AgentId = count.AgentId,
                        Action = newStatus == CashCountStatus.Approved
                            ? CashCountAuditAction.Approved
                            : CashCountAuditAction.Unapproved,
                        PreviousStatus = previousStatus,
                        NewStatus = newStatus,
                        TotalAmount = count.TotalAmount,
                        PerformedAt = clock.UtcNow,
                        Notes = newStatus == CashCountStatus.Approved
                            ? "Closing approval was restored because every captured bank wallet again has current evidence."
                            : $"Closing approval was invalidated; current approval is required for: {string.Join(", ", session.PendingBankWallets)}."
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
                session.ApprovalStatus = count.Status;
            }
        }
    }
}
