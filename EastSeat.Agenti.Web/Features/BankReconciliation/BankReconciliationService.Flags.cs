using System.Data;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.BankReconciliation;

public sealed partial class BankReconciliationService
{
    public async Task<BankReconciliationReviewDto> FlagAsync(
        string userId, long reviewId, int expectedVersion, string reason,
        CancellationToken cancellationToken = default)
    {
        RequireReviewReason(reason);
        var accountId = await ReviewAccountIdAsync(reviewId, cancellationToken);
        await RequireReviewerAsync(userId, accountId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var review = await db.BankReconciliationReviews.SingleAsync(r => r.Id == reviewId, cancellationToken);
            await db.Entry(review).ReloadAsync(cancellationToken);
            if (review.Version != expectedVersion)
                throw new BankingConflictException("This review changed. Refresh before flagging it.");
            var openFlags = await OpenFlagsAsync(reviewId, cancellationToken);
            if (openFlags.Count > 0)
                throw new BankingRuleException("Respond to the existing supervisor flag before raising another.");
            AddDecision(review, userId, ReconciliationDecisionKind.SupervisorFlagged, reason,
                new { RaisedByRole = (await access.GetActorAsync(userId, cancellationToken)).Role.ToString() });
            review.Version++;
            await db.SaveChangesAsync(cancellationToken);
            var result = await EvaluateAsync(review, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<BankReconciliationReviewDto> RespondToFlagAsync(
        string userId, long reviewId, BankFlagResponseForm form,
        CancellationToken cancellationToken = default)
    {
        RequireReviewReason(form.Response);
        var accountId = await ReviewAccountIdAsync(reviewId, cancellationToken);
        await access.RequireAccountAsync(userId, accountId, adminOnly: true, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var review = await db.BankReconciliationReviews.SingleAsync(r => r.Id == reviewId, cancellationToken);
            await db.Entry(review).ReloadAsync(cancellationToken);
            if (review.Version != form.ExpectedReviewVersion)
                throw new BankingConflictException("This review changed. Refresh before responding to the flag.");
            var flag = await db.BankReconciliationDecisions.SingleOrDefaultAsync(d =>
                d.Id == form.FlagId && d.ReviewId == reviewId &&
                d.Kind == ReconciliationDecisionKind.SupervisorFlagged, cancellationToken)
                ?? throw new BankingRuleException("Supervisor flag not found.");
            if (!(await OpenFlagsAsync(reviewId, cancellationToken)).Any(d => d.Id == flag.Id))
                throw new BankingRuleException("This flag has already received an administrator response.");
            AddDecision(review, userId, ReconciliationDecisionKind.AdminFlagResponse, form.Response,
                new { FlagId = flag.Id, Action = form.Void ? "Voided" : "Responded" });
            review.Version++;
            await db.SaveChangesAsync(cancellationToken);
            var result = await EvaluateAsync(review, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<List<BankReconciliationDecision>> OpenFlagsAsync(long reviewId, CancellationToken cancellationToken)
    {
        var decisions = await db.BankReconciliationDecisions.AsNoTracking().Where(d =>
            d.ReviewId == reviewId && (d.Kind == ReconciliationDecisionKind.SupervisorFlagged ||
                d.Kind == ReconciliationDecisionKind.AdminFlagResponse)).ToListAsync(cancellationToken);
        var responded = decisions.Where(d => d.Kind == ReconciliationDecisionKind.AdminFlagResponse)
            .Select(d => FlagIdFrom(d.DetailsJson)).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        return decisions.Where(d => d.Kind == ReconciliationDecisionKind.SupervisorFlagged && !responded.Contains(d.Id)).ToList();
    }
}
