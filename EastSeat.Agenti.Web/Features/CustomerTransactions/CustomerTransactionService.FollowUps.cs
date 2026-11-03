using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CustomerTransactions;

public sealed partial class CustomerTransactionService
{
    public async Task<CustomerTransactionDto> RecordFollowUpAsync(
        string userId, long transactionId, CustomerFollowUpForm form, CancellationToken cancellationToken = default)
    {
        ValidateCommand(form.CommandId);
        if (form.Kind == CustomerMovementKind.SupportUpdate)
        {
            if (form.Amount != 0)
                throw new BankingRuleException("A support update cannot post a financial amount.");
        }
        else
            ValidateAmount(form.Amount);
        ValidateNotes(form.Notes);
        if (!Enum.IsDefined(form.Kind) || !Enum.IsDefined(form.Outcome))
            throw new BankingRuleException("Choose a supported follow-up action and outcome.");
        if (form.Kind != CustomerMovementKind.SupportUpdate && (!form.EvidenceFileId.HasValue || form.EvidenceFileId <= 0))
            throw new BankingRuleException("Bank or customer receipt evidence is required for a follow-up.");
        if (form.SupportTicketNumber?.Trim().Length > 100)
            throw new BankingRuleException("A support ticket number cannot exceed 100 characters.");
        var hash = Hash(new
        {
            Version = 1, transactionId, form.CashSessionId, form.RelatedMovementId, form.Kind, form.Outcome,
            form.Amount, form.BankMoved, form.CashExchanged, form.CustomerCompletionReversed,
            form.BankCredit, SupportTicketNumber = form.SupportTicketNumber?.Trim(),
            BankReference = CleanReference(form.BankReference), form.ReferenceUnavailable,
            OccurredAt = form.OccurredAt.ToUniversalTime(), form.EvidenceFileId, Notes = form.Notes.Trim()
        });
        try
        {
            return await ExecuteAsync(async () =>
            {
                var actor = await access.GetActorAsync(userId, cancellationToken);
                var entry = await FullQuery().SingleOrDefaultAsync(t => t.Id == transactionId, cancellationToken)
                    ?? throw new BankingRuleException("Customer transaction not found.");
                await access.RequireAccountAsync(userId, entry.BankAccountId, cancellationToken: cancellationToken);
                if (entry.AgentId != actor.AgentId)
                    throw new UnauthorizedAccessException("Only the owning agent may record customer follow-ups.");
                var replay = await db.CustomerMovements.AsNoTracking().SingleOrDefaultAsync(
                    m => m.RecordedByUserId == userId && m.CommandId == form.CommandId, cancellationToken);
                if (replay != null)
                {
                    VerifyReplay(replay.RequestHash, hash);
                    return Map(entry);
                }
                var active = ActiveMovements(entry);
                var primary = PrimaryMovement(active);
                if (primary.Kind == CustomerMovementKind.Voided)
                    throw new BankingRuleException("A voided transaction cannot receive new follow-ups.");
                var relatedId = form.Kind == CustomerMovementKind.SupportUpdate && form.RelatedMovementId == 0
                    ? primary.Id : form.RelatedMovementId;
                var related = active.SingleOrDefault(m => m.Id == relatedId)
                    ?? throw new BankingRuleException("Select a current movement from this customer transaction.");
                if (form.OccurredAt == default || form.OccurredAt > clock.UtcNow || form.OccurredAt < related.OccurredAt)
                    throw new BankingRuleException("The follow-up time must follow the related movement and must not be in the future.");
                var bankObservation = form.Kind is CustomerMovementKind.BankReversal or
                    CustomerMovementKind.BankCompletionConfirmed or CustomerMovementKind.BankResolutionVerified or
                    CustomerMovementKind.BankPostingObserved or CustomerMovementKind.SupportUpdate;
                var sessionId = bankObservation && form.CashSessionId == 0 ? entry.OriginatingSessionId : form.CashSessionId;
                if (bankObservation)
                {
                    if (!await db.CashSessions.AnyAsync(s => s.Id == sessionId && s.BranchId == actor.BranchId, cancellationToken))
                        throw new UnauthorizedAccessException("You do not have access to the selected recording session.");
                }
                else
                {
                    await access.RequireTradingSessionAsync(actor, sessionId, cancellationToken);
                    await ValidateOccurrenceAsync(sessionId, form.OccurredAt, cancellationToken);
                }
                await ValidateEvidenceAsync(form.EvidenceFileId, entry.BankAccountId, cancellationToken);

                var kind = primary.TransactionKind ?? entry.Kind;
                var settled = active.Sum(m => m.CustomerSettledAmount);
                var refunded = active.Where(m => m.Kind == CustomerMovementKind.Refund).Sum(m => -m.CashEffect);
                var cancelled = active.Where(m => m.Kind == CustomerMovementKind.Cancelled).Sum(m => m.Amount);
                var remaining = primary.Amount - settled - refunded - cancelled;
                var cashHeld = Math.Max(0, active.Sum(m => m.CashEffect) - settled);
                var lineage = Lineage(related, entry.Movements);
                var dependents = active.Where(m => m.RelatedMovementId.HasValue && lineage.Contains(m.RelatedMovementId.Value)).ToList();
                var movement = new CustomerMovement
                {
                    CustomerTransactionId = entry.Id, CashSessionId = sessionId, Kind = form.Kind,
                    TransactionKind = kind, CustomerIdentifier = primary.CustomerIdentifier ?? entry.CustomerIdentifier,
                    Amount = form.Amount, RelatedMovementId = related.Id, EvidenceFileId = form.EvidenceFileId,
                    SupportTicketNumber = form.SupportTicketNumber?.Trim(),
                    BankReference = CleanReference(form.BankReference), ReferenceUnavailable = form.ReferenceUnavailable,
                    Notes = form.Notes.Trim(), CommandId = form.CommandId, RequestHash = hash,
                    RecordedByUserId = actor.UserId, OccurredAt = form.OccurredAt.ToUniversalTime(), RecordedAt = clock.UtcNow
                };
                movement.OccurrenceDate = clock.GetBusinessDate(movement.OccurredAt);
                switch (form.Kind)
                {
                    case CustomerMovementKind.BankReversal:
                    {
                        ValidateReference(form.BankReference, form.ReferenceUnavailable);
                        var reversed = dependents.Where(m => m.Kind == CustomerMovementKind.BankReversal)
                            .Sum(m => Math.Abs(m.BankEffect));
                        if (related.BankEffect == 0 || form.Amount > Math.Abs(related.BankEffect) - reversed)
                            throw new BankingRuleException("The reversal exceeds the remaining amount of its bank posting.");
                        movement.BankEffect = -Math.Sign(related.BankEffect) * form.Amount;
                        movement.ReversalVerifiedByAgent = true;
                        if (form.CustomerCompletionReversed)
                        {
                            var alreadyReversed = dependents.Sum(m => Math.Max(0, -m.CustomerSettledAmount));
                            if (kind != CustomerTransactionKind.Deposit ||
                                form.Amount > related.CustomerSettledAmount - alreadyReversed || form.Amount > settled)
                                throw new BankingRuleException("This posting cannot reverse the stated customer completion.");
                            movement.CustomerSettledAmount = -form.Amount;
                        }
                        break;
                    }
                    case CustomerMovementKind.BankCompletionConfirmed:
                        if (kind != CustomerTransactionKind.Deposit || related.BankEffect >= 0 ||
                            form.Amount > remaining ||
                            form.Amount > Math.Abs(related.BankEffect) - dependents.Sum(m => Math.Max(0, m.CustomerSettledAmount)))
                            throw new BankingRuleException("Customer completion must refer to an existing, not-yet-completed deposit debit.");
                        movement.CustomerSettledAmount = form.Amount;
                        break;
                    case CustomerMovementKind.BankResolutionVerified:
                        if (related.Kind == CustomerMovementKind.BankReversal && related.BankEffect > 0)
                        {
                            var verified = dependents.Where(m => m.Kind == CustomerMovementKind.BankResolutionVerified).Sum(m => m.Amount);
                            if (related.ReversalVerifiedByAgent || form.Amount > related.BankEffect - verified)
                                throw new BankingRuleException("This bank return is already verified or the confirmation exceeds its remaining credit.");
                            break;
                        }
                        var accounted = dependents.Where(m => m.Kind == CustomerMovementKind.BankPostingObserved)
                            .Sum(m => Math.Abs(m.BankEffect)) +
                            dependents.Where(m => m.Kind == CustomerMovementKind.BankResolutionVerified).Sum(m => m.Amount);
                        if (related.BankEffect != 0 || form.Amount > related.Amount - accounted)
                            throw new BankingRuleException("A no-posting confirmation must cover an unposted part of its referenced attempt. Otherwise record the actual reversal.");
                        break;
                    case CustomerMovementKind.BankPostingObserved:
                        ValidateReference(form.BankReference, form.ReferenceUnavailable);
                        movement.BankEffect = form.BankCredit ? form.Amount : -form.Amount;
                        if (form.Outcome == CustomerCaptureOutcome.Completed && kind == CustomerTransactionKind.Deposit &&
                            !form.BankCredit && remaining > 0)
                            movement.CustomerSettledAmount = Math.Min(remaining, form.Amount);
                        break;
                    case CustomerMovementKind.Refund:
                    {
                        if (kind != CustomerTransactionKind.Deposit || form.Amount > cashHeld || form.Amount > remaining)
                            throw new BankingRuleException("The refund exceeds customer cash still held.");
                        var bankCredit = VerifiedCredit(related, dependents);
                        if (form.Amount > bankCredit)
                            throw new BankingRuleException("Verify the bank reversal first; its available credit must cover the refund.");
                        movement.CashEffect = -form.Amount;
                        break;
                    }
                    case CustomerMovementKind.Retry:
                    {
                        ValidateReference(form.BankReference, form.ReferenceUnavailable);
                        if (kind == CustomerTransactionKind.Deposit)
                        {
                            if (form.CashExchanged || form.Amount > remaining || form.Amount > cashHeld ||
                                form.Amount > VerifiedCredit(related, dependents))
                                throw new BankingRuleException("A deposit retry must use cash already held and a verified bank resolution, without receiving cash twice.");
                            movement.BankEffect = form.BankMoved ? -form.Amount : 0m;
                            if (form.Outcome == CustomerCaptureOutcome.Completed)
                            {
                                if (!form.BankMoved)
                                    throw new BankingRuleException("A successful retry requires a recorded bank debit.");
                                movement.CustomerSettledAmount = form.Amount;
                            }
                        }
                        else
                        {
                            if (active.Sum(m => m.BankEffect) + (form.BankMoved ? form.Amount : 0m) > primary.Amount)
                                throw new BankingRuleException("This retry would duplicate the withdrawal's bank credit.");
                            if (form.CashExchanged && form.Amount > remaining)
                                throw new BankingRuleException("Customer cash has already been paid.");
                            movement.BankEffect = form.BankMoved ? form.Amount : 0m;
                            movement.CashEffect = form.CashExchanged ? -form.Amount : 0m;
                            movement.CustomerSettledAmount = form.CashExchanged ? form.Amount : 0m;
                        }
                        if (form.Outcome != CustomerCaptureOutcome.Completed)
                            movement.Kind = InitialKind(form.Outcome);
                        break;
                    }
                    case CustomerMovementKind.CashPayment:
                        if (kind != CustomerTransactionKind.Withdrawal || form.Amount > remaining)
                            throw new BankingRuleException("The payment exceeds the withdrawal amount still owed to the customer.");
                        movement.CashEffect = -form.Amount;
                        movement.CustomerSettledAmount = form.Amount;
                        break;
                    case CustomerMovementKind.CashReceipt:
                        if (kind != CustomerTransactionKind.Deposit ||
                            form.Amount > primary.Amount - refunded - cancelled - active.Sum(m => m.CashEffect))
                            throw new BankingRuleException("The cash receipt exceeds what remains to be collected for this deposit.");
                        movement.CashEffect = form.Amount;
                        break;
                    case CustomerMovementKind.Cancelled:
                        if (related.Kind != CustomerMovementKind.BankResolutionVerified ||
                            form.Amount > remaining || form.Amount > VerifiedCredit(related, dependents) || cashHeld != 0 ||
                            (kind == CustomerTransactionKind.Deposit && settled > active.Sum(m => m.CashEffect)) ||
                            active.Sum(m => m.BankEffect) != (kind == CustomerTransactionKind.Deposit ? -settled : settled))
                            throw new BankingRuleException("Cancellation requires verified bank resolution and no customer funds or bank settlement still outstanding.");
                        movement.BankEffect = 0m;
                        break;
                    case CustomerMovementKind.SupportUpdate:
                        break;
                    default:
                        throw new BankingRuleException("This action is not a supported customer follow-up.");
                }

                db.CustomerMovements.Add(movement);
                await EnsurePendingCaseAsync(entry, form.Notes, cancellationToken);
                if (!string.IsNullOrWhiteSpace(form.SupportTicketNumber))
                {
                    var pending = db.PendingTransactions.Local.FirstOrDefault(p => p.CustomerTransactionId == entry.Id)
                        ?? await db.PendingTransactions.SingleAsync(p => p.CustomerTransactionId == entry.Id, cancellationToken);
                    pending.TicketNumber = form.SupportTicketNumber.Trim();
                    pending.Status = PendingTransactionStatus.ReportedToBank;
                }
                await TouchAsync(entry, sessionId, movement.OccurredAt, actor.UserId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return await GetAsync(userId, entry.Id, cancellationToken);
            }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var replay = await db.CustomerMovements.AsNoTracking().SingleOrDefaultAsync(
                m => m.RecordedByUserId == userId && m.CommandId == form.CommandId, cancellationToken);
            if (replay == null)
                throw;
            VerifyReplay(replay.RequestHash, hash);
            return await GetAsync(userId, replay.CustomerTransactionId, cancellationToken);
        }
    }

    private static HashSet<long> Lineage(CustomerMovement movement, IEnumerable<CustomerMovement> all)
    {
        var byId = all.ToDictionary(m => m.Id);
        var ids = new HashSet<long>();
        var current = movement;
        while (true)
        {
            if (!ids.Add(current.Id))
                throw new BankingRuleException("The correction history contains an invalid cycle.");
            if (!current.SupersedesMovementId.HasValue)
                return ids;
            current = byId.TryGetValue(current.SupersedesMovementId.Value, out var previous)
                ? previous
                : throw new BankingRuleException("The correction history is incomplete.");
        }
    }

    private static decimal VerifiedCredit(
        CustomerMovement source, IReadOnlyCollection<CustomerMovement> dependents)
    {
        decimal credit;
        if (source.Kind == CustomerMovementKind.BankReversal && source.BankEffect > 0 &&
            source.ReversalVerifiedByAgent && source.EvidenceFileId.HasValue)
        {
            credit = source.BankEffect;
        }
        else if (source.Kind == CustomerMovementKind.BankResolutionVerified && source.EvidenceFileId.HasValue)
        {
            credit = source.Amount;
        }
        else
        {
            return 0m;
        }
        var consumed = dependents.Sum(m => Math.Max(0, -m.BankEffect)) +
                       dependents.Where(m => m.Kind == CustomerMovementKind.Refund).Sum(m => -m.CashEffect) +
                       dependents.Where(m => m.Kind == CustomerMovementKind.Cancelled).Sum(m => m.Amount) +
                       dependents.Where(m => m.Kind == CustomerMovementKind.PendingAttempt && m.BankEffect == 0).Sum(m => m.Amount);
        return Math.Max(0, credit - consumed);
    }
}
