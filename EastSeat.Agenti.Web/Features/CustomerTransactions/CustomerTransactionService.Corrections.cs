using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CustomerTransactions;

public sealed partial class CustomerTransactionService
{
    public async Task<CustomerTransactionDto> CorrectAsync(
        string userId, CustomerMovementCorrectionForm form, CancellationToken cancellationToken = default)
    {
        ValidateCommand(form.CommandId);
        ValidateNotes(form.Reason);
        if (form.EvidenceFileId <= 0)
            throw new BankingRuleException("Receipt evidence is required for a correction.");
        if (!form.Void)
        {
            ValidateIdentifier(form.CustomerIdentifier);
            if (!Enum.IsDefined(form.TransactionKind) || !Enum.IsDefined(form.Outcome))
                throw new BankingRuleException("Choose a valid transaction type and outcome.");
        }
        var hash = Hash(new
        {
            Version = 1, form.CorrectionRequestId, form.MovementId, form.Void, form.TransactionKind,
            form.Outcome, form.Amount, form.CashExchanged, form.BankMoved,
            CustomerIdentifier = form.CustomerIdentifier?.Trim(), BankReference = CleanReference(form.BankReference),
            form.ReferenceUnavailable, OccurredAt = form.OccurredAt.ToUniversalTime(),
            form.EvidenceFileId, Reason = form.Reason.Trim()
        });
        return await ExecuteAsync(async () =>
        {
            var actor = await access.GetActorAsync(userId, cancellationToken);
            var original = await db.CustomerMovements.AsNoTracking()
                .SingleOrDefaultAsync(m => m.Id == form.MovementId, cancellationToken)
                ?? throw new BankingRuleException("Movement not found.");
            var entry = await FullQuery().SingleAsync(t => t.Id == original.CustomerTransactionId, cancellationToken);
            await access.RequireAccountAsync(userId, entry.BankAccountId, cancellationToken: cancellationToken);
            if (entry.AgentId != actor.AgentId)
                throw new UnauthorizedAccessException("Only the owning agent may submit this correction.");
            var replay = await db.CustomerMovements.AsNoTracking().SingleOrDefaultAsync(
                m => m.RecordedByUserId == userId && m.CommandId == form.CommandId, cancellationToken);
            if (replay != null)
            {
                VerifyReplay(replay.RequestHash, hash);
                return Map(entry);
            }
            await RequireCorrectionAsync(actor, form.CorrectionRequestId, original.CashSessionId,
                original.Id, cancellationToken);
            var active = ActiveMovements(entry);
            if (!active.Any(m => m.Id == original.Id))
                throw new BankingRuleException("This movement has already been corrected. Refresh the request.");
            if (!form.Void)
            {
                if (original.Kind == CustomerMovementKind.SupportUpdate)
                {
                    if (form.Amount != 0)
                        throw new BankingRuleException("A support-only correction cannot post a financial amount.");
                }
                else ValidateAmount(form.Amount);
                if (!original.RelatedMovementId.HasValue || original.BankEffect != 0)
                    ValidateReference(form.BankReference, form.ReferenceUnavailable);
                if (original.RelatedMovementId.HasValue &&
                    (form.TransactionKind != (original.TransactionKind ?? entry.Kind) ||
                     form.CashExchanged != (original.CashEffect != 0) || form.BankMoved != (original.BankEffect != 0)))
                    throw new BankingRuleException("A linked-event correction must preserve its movement direction and cash/bank legs. Record an evidenced follow-up for a changed bank outcome.");
            }
            if (!original.RelatedMovementId.HasValue &&
                active.Any(m => m.RelatedMovementId.HasValue && Lineage(original, entry.Movements).Contains(m.RelatedMovementId.Value)) &&
                (form.Void || form.Amount != original.Amount || form.TransactionKind != (original.TransactionKind ?? entry.Kind) ||
                 InitialKind(form.Outcome) != original.Kind || form.CashExchanged != (original.CashEffect != 0) ||
                 form.BankMoved != (original.BankEffect != 0)))
                throw new BankingRuleException("This record has financial follow-ups. Its amount or outcome needs a complete case review before correction.");
            await ValidateEvidenceAsync(form.EvidenceFileId, entry.BankAccountId, cancellationToken);
            var bankObservation = original.Kind is CustomerMovementKind.BankReversal or CustomerMovementKind.BankPostingObserved or
                CustomerMovementKind.BankCompletionConfirmed or CustomerMovementKind.BankResolutionVerified or CustomerMovementKind.SupportUpdate;
            if (!form.Void && !bankObservation)
                await ValidateOccurrenceAsync(original.CashSessionId, form.OccurredAt, cancellationToken);
            else if (!form.Void && (form.OccurredAt == default || form.OccurredAt > clock.UtcNow))
                throw new BankingRuleException("Provide the non-future time of the bank observation.");
            var kind = form.Void ? original.TransactionKind ?? entry.Kind : form.TransactionKind;
            var movement = new CustomerMovement
            {
                CustomerTransactionId = entry.Id, CashSessionId = original.CashSessionId,
                Kind = form.Void ? CustomerMovementKind.Voided : InitialKind(form.Outcome),
                TransactionKind = kind, Amount = form.Void ? 0m : form.Amount,
                CustomerIdentifier = form.Void ? original.CustomerIdentifier : form.CustomerIdentifier.Trim(),
                BankReference = form.Void ? original.BankReference : CleanReference(form.BankReference),
                ReferenceUnavailable = form.Void ? original.ReferenceUnavailable : form.ReferenceUnavailable,
                CashEffect = form.Void || !form.CashExchanged ? 0m : kind == CustomerTransactionKind.Deposit ? form.Amount : -form.Amount,
                BankEffect = form.Void || !form.BankMoved ? 0m : kind == CustomerTransactionKind.Deposit ? -form.Amount : form.Amount,
                CustomerSettledAmount = form.Void ? 0m : kind == CustomerTransactionKind.Deposit
                    ? form.Outcome == CustomerCaptureOutcome.Completed ? form.Amount : 0m
                    : form.CashExchanged ? form.Amount : 0m,
                RelatedMovementId = original.RelatedMovementId, SupersedesMovementId = original.Id,
                CorrectionRequestId = form.CorrectionRequestId, EvidenceFileId = form.EvidenceFileId,
                CommandId = form.CommandId, RequestHash = hash, Notes = form.Reason.Trim(), RecordedByUserId = userId,
                OccurredAt = form.Void ? original.OccurredAt : form.OccurredAt.ToUniversalTime(), RecordedAt = clock.UtcNow
            };
            if (original.RelatedMovementId.HasValue && !form.Void)
            {
                movement.Kind = original.Kind;
                movement.TransactionKind = original.TransactionKind;
                movement.CashEffect = Math.Sign(original.CashEffect) * form.Amount;
                movement.BankEffect = Math.Sign(original.BankEffect) * form.Amount;
                movement.CustomerSettledAmount = Math.Sign(original.CustomerSettledAmount) * form.Amount;
                movement.ReversalVerifiedByAgent = original.ReversalVerifiedByAgent;
                movement.SupportTicketNumber = original.SupportTicketNumber;
            }
            movement.OccurrenceDate = clock.GetBusinessDate(movement.OccurredAt);
            ValidateCorrectedCase(entry, original, movement);
            db.CustomerMovements.Add(movement);
            var matches = await db.BankReconciliationMatches
                .Where(m => m.CustomerMovementId == original.Id && m.IsActive).ToListAsync(cancellationToken);
            foreach (var match in matches)
            {
                match.IsActive = false;
                match.Version++;
                db.BankReconciliationDecisions.Add(new BankReconciliationDecision
                {
                    BankAccountId = entry.BankAccountId, CashSessionId = original.CashSessionId,
                    BankTransactionId = match.BankTransactionId, Kind = ReconciliationDecisionKind.ReviewInvalidated,
                    DetailsJson = JsonSerializer.Serialize(new { match.Id, OriginalMovementId = original.Id, form.CorrectionRequestId }),
                    Reason = "The source record was corrected under an administrator's request.",
                    PerformedByUserId = userId, PerformedAt = clock.UtcNow
                });
            }
            if (!form.Void && (form.Outcome != CustomerCaptureOutcome.Completed || !form.BankMoved || !form.CashExchanged))
                await EnsurePendingCaseAsync(entry, form.Reason, cancellationToken);
            await TouchAsync(entry, original.CashSessionId, movement.OccurredAt, actor.UserId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return await GetAsync(userId, entry.Id, cancellationToken);
        }, cancellationToken);
    }

    private static void ValidateCorrectedCase(CustomerTransaction entry, CustomerMovement original, CustomerMovement replacement)
    {
        var all = entry.Movements.Append(replacement).ToList();
        var active = ActiveMovements(entry).Where(m => m.Id != original.Id).Append(replacement).ToList();
        var primary = PrimaryMovement(active);
        var settled = active.Sum(m => m.CustomerSettledAmount);
        var refunded = active.Where(m => m.Kind == CustomerMovementKind.Refund).Sum(m => -m.CashEffect);
        var cancelled = active.Where(m => m.Kind == CustomerMovementKind.Cancelled).Sum(m => m.Amount);
        if (settled < 0 || refunded < 0 || settled + refunded + cancelled > primary.Amount)
            throw new BankingRuleException("The correction would over-complete or over-refund this customer transaction.");
        if ((primary.TransactionKind ?? entry.Kind) == CustomerTransactionKind.Deposit &&
            (active.Sum(m => m.CashEffect) < 0 || active.Sum(m => Math.Max(0, m.CashEffect)) > primary.Amount))
            throw new BankingRuleException("The correction would refund cash never received or receive the same customer cash twice.");
        foreach (var source in active)
        {
            var lineage = Lineage(source, all);
            var children = active.Where(m => m.RelatedMovementId.HasValue && lineage.Contains(m.RelatedMovementId.Value)).ToList();
            if (source.Kind == CustomerMovementKind.Voided && children.Count != 0)
                throw new BankingRuleException("Correct the dependent follow-ups before voiding their source record.");
            if (children.Any(m => m.OccurredAt < source.OccurredAt))
                throw new BankingRuleException("A corrected timestamp cannot put a follow-up before its source.");
            if (children.Where(m => m.Kind == CustomerMovementKind.BankReversal).Sum(m => Math.Abs(m.BankEffect)) > Math.Abs(source.BankEffect))
                throw new BankingRuleException("The corrected reversals exceed their original bank posting.");
            if (source.Kind is CustomerMovementKind.BankReversal or CustomerMovementKind.BankResolutionVerified)
            {
                var credit = source.Kind == CustomerMovementKind.BankReversal ? Math.Max(0, source.BankEffect) : source.Amount;
                var consumed = children.Sum(m => Math.Max(0, -m.BankEffect)) +
                    children.Where(m => m.Kind == CustomerMovementKind.Refund).Sum(m => -m.CashEffect) +
                    children.Where(m => m.Kind == CustomerMovementKind.Cancelled ||
                        (m.Kind == CustomerMovementKind.PendingAttempt && m.BankEffect == 0)).Sum(m => m.Amount);
                if (consumed > credit)
                    throw new BankingRuleException("The corrected bank return cannot fund its recorded refunds or retries.");
            }
        }
    }

    public async Task ResubmitCorrectionsAsync(
        string userId, long correctionRequestId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            var actor = await access.GetActorAsync(userId, cancellationToken);
            var request = await db.CashCountCorrectionRequests.Include(r => r.CashCount).ThenInclude(c => c!.CashSession)
                .SingleOrDefaultAsync(r => r.Id == correctionRequestId, cancellationToken)
                ?? throw new BankingRuleException("Correction request not found.");
            if (request.AgentId != actor.AgentId || request.CashCount!.CashSession!.BranchId != actor.BranchId)
                throw new UnauthorizedAccessException("You do not have access to this correction request.");
            if (!request.IsOpen)
                return true;
            if (request.Version != expectedVersion)
                throw new BankingRuleException("The correction request changed. Refresh before resubmitting.");
            if (!await db.CustomerMovements.AnyAsync(m => m.CorrectionRequestId == request.Id, cancellationToken))
                throw new BankingRuleException("Record the requested correction before resubmitting.");
            request.IsOpen = false;
            request.ResubmittedAt = clock.UtcNow;
            request.Version++;
            var session = request.CashCount.CashSession;
            session.Version++;
            await WorkflowNotifications.AddForBranchAsync(db, actor.BranchId, "Closing corrections ready for review",
                $"Corrections for {session.SessionDate:yyyy-MM-dd} have been resubmitted.",
                NotificationType.CountPendingApproval, $"/bank-reconciliation?sessionId={session.Id}", clock.UtcNow,
                cancellationToken: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }
}
