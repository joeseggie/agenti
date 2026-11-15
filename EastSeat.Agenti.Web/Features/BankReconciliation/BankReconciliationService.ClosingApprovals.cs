using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService
{
    private async Task PopulateClosingApprovalsAsync(
        BankReconciliationReviewDto report, ReviewFacts facts, CancellationToken cancellationToken)
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
                .Where(w => w.WalletType == WalletTypeEnum.Bank);
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
                if (bank.BankAccountId == report.BankAccountId)
                    session.BankApprovalCurrent = current;
                if (!current)
                    session.PendingBankWallets.Add(bank.BankAccountId.HasValue
                        ? bank.WalletName : $"{bank.WalletName} (missing account binding in the closing snapshot)");
            }
            // Historical approvals remain recorded; the displayed status must reflect current evidence.
            if (session.PendingBankWallets.Count > 0 && session.ApprovalStatus == CashCountStatus.Approved)
                session.ApprovalStatus = CashCountStatus.PendingApproval;
        }
    }
}
