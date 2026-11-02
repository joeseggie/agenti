using System.Data;
using System.Text.Json;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CustomerTransactions;

public sealed partial class CustomerTransactionService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock) : ICustomerTransactionService
{
    private const decimal MaximumAmount = 9_999_999_999_999_999.99m;

    public async Task<CustomerCaptureOptionsDto> GetCaptureOptionsAsync(
        string userId, long sessionId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        await access.RequireTradingSessionAsync(actor, sessionId, cancellationToken);
        var accounts = await db.BankAccounts.AsNoTracking()
            .Where(a => a.BranchId == actor.BranchId && a.Wallet!.AgentId == actor.AgentId &&
                        a.IsActive && a.Wallet.IsActive)
            .OrderBy(a => a.BankName)
            .Select(a => new CustomerBankOptionDto(a.Id, a.BankName, a.Wallet!.Name, a.Currency))
            .ToListAsync(cancellationToken);
        var cashWallets = await db.Wallets.AsNoTracking()
            .Where(w => w.AgentId == actor.AgentId && w.IsActive && w.WalletType!.Type == WalletTypeEnum.Cash)
            .OrderBy(w => w.Name)
            .Select(w => new CustomerCashWalletOptionDto(w.Id, w.Name, w.Currency))
            .ToListAsync(cancellationToken);
        return new CustomerCaptureOptionsDto(sessionId, clock.UtcNow, accounts, cashWallets);
    }

    public async Task<List<CustomerTransactionDto>> GetForSessionAsync(
        string userId, long sessionId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        if (!await db.CashSessions.AnyAsync(s => s.Id == sessionId && s.BranchId == actor.BranchId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have access to this session.");
        var entries = await FullQuery()
            .Where(t => t.BankAccount!.BranchId == actor.BranchId &&
                        (actor.Role == UserRole.Admin || t.AgentId == actor.AgentId) &&
                        (t.OriginatingSessionId == sessionId || t.Movements.Any(m => m.CashSessionId == sessionId)))
            .OrderByDescending(t => t.Id).ToListAsync(cancellationToken);
        return entries.Select(Map).ToList();
    }

    public async Task<CustomerCaptureOptionsDto> GetCorrectionOptionsAsync(
        string userId, long correctionRequestId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        var request = await db.CashCountCorrectionRequests.AsNoTracking().Include(r => r.CashCount).ThenInclude(c => c!.CashSession)
            .SingleOrDefaultAsync(r => r.Id == correctionRequestId && r.IsOpen &&
                r.AgentId == actor.AgentId && r.CashCount!.CashSession!.BranchId == actor.BranchId, cancellationToken)
            ?? throw new UnauthorizedAccessException("No open correction request is available to this agent.");
        var walletIds = await db.CashCountDetails.Where(d => d.CashCount!.CashSessionId == request.CashCount!.CashSessionId &&
            d.CashCount.AgentId == actor.AgentId).Select(d => d.WalletId).Distinct().ToListAsync(cancellationToken);
        var accounts = await db.BankAccounts.AsNoTracking().Where(a => a.BranchId == actor.BranchId &&
                a.Wallet!.AgentId == actor.AgentId && walletIds.Contains(a.WalletId))
            .OrderBy(a => a.BankName).Select(a => new CustomerBankOptionDto(a.Id, a.BankName, a.Wallet!.Name, a.Currency))
            .ToListAsync(cancellationToken);
        var cash = await db.Wallets.AsNoTracking().Where(w => w.AgentId == actor.AgentId &&
                walletIds.Contains(w.Id) && w.WalletType!.Type == WalletTypeEnum.Cash)
            .OrderBy(w => w.Name).Select(w => new CustomerCashWalletOptionDto(w.Id, w.Name, w.Currency))
            .ToListAsync(cancellationToken);
        return new CustomerCaptureOptionsDto(request.CashCount!.CashSessionId, clock.UtcNow, accounts, cash);
    }

    public async Task<List<CustomerTransactionDto>> GetOpenCasesAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(userId, cancellationToken);
        var entries = await FullQuery()
            .Where(t => t.BankAccount!.BranchId == actor.BranchId &&
                        (actor.Role == UserRole.Admin || t.AgentId == actor.AgentId) &&
                        db.PendingTransactions.Any(p => p.CustomerTransactionId == t.Id &&
                            p.Status != PendingTransactionStatus.Resolved && p.Status != PendingTransactionStatus.Cancelled))
            .OrderBy(t => t.Id).ToListAsync(cancellationToken);
        return entries.Select(Map).ToList();
    }

    public async Task<CustomerTransactionDto> GetAsync(
        string userId, long transactionId, CancellationToken cancellationToken = default)
    {
        var entry = await FullQuery().SingleOrDefaultAsync(t => t.Id == transactionId, cancellationToken)
            ?? throw new BankingRuleException("Customer transaction not found.");
        await access.RequireAccountAsync(userId, entry.BankAccountId, cancellationToken: cancellationToken);
        return Map(entry);
    }

    public async Task<CustomerTransactionDto> CaptureAsync(
        string userId, CustomerTransactionForm form, CancellationToken cancellationToken = default)
    {
        ValidateInitial(form);
        var hash = Hash(new
        {
            Version = 1, form.CashSessionId, form.BankAccountId, form.CashWalletId, form.Kind, form.Outcome, form.Amount,
            CustomerIdentifier = form.CustomerIdentifier.Trim(), BankReference = CleanReference(form.BankReference),
            form.ReferenceUnavailable, form.CashExchanged, form.BankMoved, OccurredAt = form.OccurredAt.ToUniversalTime(),
            form.EvidenceFileId, Notes = form.Notes?.Trim(), form.CorrectionRequestId
        });
        try
        {
            return await ExecuteAsync(async () =>
            {
                var actor = await access.GetActorAsync(userId, cancellationToken);
                if (!actor.AgentId.HasValue)
                    throw new UnauthorizedAccessException("An active agent assignment is required.");
                var account = await access.RequireAccountAsync(userId, form.BankAccountId, cancellationToken: cancellationToken);
                if (account.AgentId != actor.AgentId)
                    throw new UnauthorizedAccessException("You may only capture transactions for your own bank wallet.");
                var replay = await FullQuery().SingleOrDefaultAsync(
                    t => t.AgentId == actor.AgentId && t.CommandId == form.CommandId, cancellationToken);
                if (replay != null)
                {
                    VerifyReplay(replay.RequestHash, hash);
                    return Map(replay);
                }
                if (!account.IsActive && !form.CorrectionRequestId.HasValue)
                    throw new BankingRuleException("The selected bank account is inactive.");
                var cashWallet = await db.Wallets.AsNoTracking().SingleOrDefaultAsync(
                    w => w.Id == form.CashWalletId && w.AgentId == actor.AgentId && (w.IsActive || form.CorrectionRequestId.HasValue) &&
                         w.WalletType!.Type == WalletTypeEnum.Cash, cancellationToken)
                    ?? throw new BankingRuleException("Select an active cash wallet belonging to this agent.");
                if (cashWallet.Currency != account.Currency)
                    throw new BankingRuleException("Cash and bank wallets must use the same currency.");
                if (form.CorrectionRequestId.HasValue)
                    await RequireCorrectionAsync(actor, form.CorrectionRequestId.Value, form.CashSessionId,
                        null, cancellationToken);
                else
                    await access.RequireTradingSessionAsync(actor, form.CashSessionId, cancellationToken);
                await ValidateOccurrenceAsync(form.CashSessionId, form.OccurredAt, cancellationToken);
                await ValidateEvidenceAsync(form.EvidenceFileId, account.Id, cancellationToken);
                var opening = await db.CashCountSnapshots.AsNoTracking()
                    .SingleOrDefaultAsync(s => s.CashCount!.CashSessionId == form.CashSessionId &&
                        s.CashCount.AgentId == actor.AgentId && s.CashCount.IsOpening, cancellationToken);
                if (opening != null)
                {
                    var openingWallets = CountSnapshotCodec.Deserialize(opening.WalletsJson);
                    if (!openingWallets.Any(w => w.WalletId == cashWallet.Id) ||
                        !openingWallets.Any(w => w.WalletId == account.WalletId && w.BankAccountId == account.Id))
                        throw new BankingRuleException("The selected wallets were not allocated to this session.");
                }
                else if (!form.CorrectionRequestId.HasValue)
                {
                    throw new BankingRuleException("An approved opening snapshot is required.");
                }

                var entry = new CustomerTransaction
                {
                    BankAccountId = account.Id, AgentId = actor.AgentId.Value,
                    OriginatingSessionId = form.CashSessionId, CashWalletId = cashWallet.Id,
                    Kind = form.Kind, Amount = form.Amount, Currency = account.Currency,
                    CustomerIdentifier = form.CustomerIdentifier.Trim(), CommandId = form.CommandId, RequestHash = hash,
                    RecordedByUserId = actor.UserId, OccurredAt = form.OccurredAt.ToUniversalTime(), RecordedAt = clock.UtcNow
                };
                db.CustomerTransactions.Add(entry);
                await db.SaveChangesAsync(cancellationToken);
                var movement = new CustomerMovement
                {
                    CustomerTransactionId = entry.Id, CashSessionId = form.CashSessionId,
                    Kind = InitialKind(form.Outcome), TransactionKind = form.Kind,
                    Amount = form.Amount, CustomerIdentifier = entry.CustomerIdentifier,
                    BankReference = CleanReference(form.BankReference), ReferenceUnavailable = form.ReferenceUnavailable,
                    CashEffect = form.CashExchanged ? (form.Kind == CustomerTransactionKind.Deposit ? form.Amount : -form.Amount) : 0m,
                    BankEffect = form.BankMoved ? (form.Kind == CustomerTransactionKind.Deposit ? -form.Amount : form.Amount) : 0m,
                    CustomerSettledAmount = form.Kind == CustomerTransactionKind.Deposit
                        ? form.Outcome == CustomerCaptureOutcome.Completed ? form.Amount : 0m
                        : form.CashExchanged ? form.Amount : 0m,
                    EvidenceFileId = form.EvidenceFileId, Notes = form.Notes?.Trim(),
                    CorrectionRequestId = form.CorrectionRequestId, CommandId = form.CommandId, RequestHash = hash,
                    RecordedByUserId = actor.UserId, OccurredAt = entry.OccurredAt, RecordedAt = clock.UtcNow
                };
                movement.OccurrenceDate = clock.GetBusinessDate(movement.OccurredAt);
                db.CustomerMovements.Add(movement);
                if (form.Outcome != CustomerCaptureOutcome.Completed || !form.CashExchanged || !form.BankMoved)
                    await EnsurePendingCaseAsync(entry, form.Notes!, cancellationToken);
                await TouchAsync(entry, form.CashSessionId, movement.OccurredAt, actor.UserId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return await GetAsync(userId, entry.Id, cancellationToken);
            }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var replay = await FullQuery().SingleOrDefaultAsync(
                t => t.RecordedByUserId == userId && t.CommandId == form.CommandId, cancellationToken);
            if (replay == null)
                throw;
            VerifyReplay(replay.RequestHash, hash);
            await access.RequireAccountAsync(userId, replay.BankAccountId, cancellationToken: cancellationToken);
            return Map(replay);
        }
    }

    private IQueryable<CustomerTransaction> FullQuery() =>
        db.CustomerTransactions.AsNoTracking().Include(t => t.BankAccount).Include(t => t.Movements);

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await action();
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

    private async Task ValidateOccurrenceAsync(long sessionId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (occurredAt == default || occurredAt > clock.UtcNow)
            throw new BankingRuleException("Provide the receipt's non-future transaction date and time.");
        var date = await db.CashSessions.Where(s => s.Id == sessionId)
            .Select(s => s.SessionDate).SingleAsync(cancellationToken);
        if (clock.GetBusinessDate(occurredAt) != date)
            throw new BankingRuleException("The transaction must belong to this session's business date. Historical entries need a correction request for their original session.");
    }

    private async Task ValidateEvidenceAsync(long? evidenceId, long accountId, CancellationToken cancellationToken)
    {
        if (evidenceId.HasValue && !await db.BankEvidenceFiles.AnyAsync(
                f => f.Id == evidenceId && f.BankAccountId == accountId && !f.IsStatement, cancellationToken))
            throw new UnauthorizedAccessException("Select receipt evidence for this bank account, not an admin statement.");
    }

    private async Task<CashCountCorrectionRequest> RequireCorrectionAsync(
        BankingActor actor, long requestId, long sessionId, long? movementId, CancellationToken cancellationToken)
    {
        var request = await db.CashCountCorrectionRequests.Include(r => r.CashCount).ThenInclude(c => c!.CashSession)
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new BankingRuleException("Correction request not found.");
        if (request.AgentId != actor.AgentId || request.CashCount!.CashSession!.BranchId != actor.BranchId ||
            request.CashCount.CashSessionId != sessionId)
            throw new UnauthorizedAccessException("This correction request does not cover the selected session.");
        if (!request.IsOpen)
            throw new BankingRuleException("This correction request is already closed.");
        if (movementId.HasValue)
        {
            var allowed = JsonSerializer.Deserialize<List<long>>(request.AllowedMovementIdsJson)
                ?? throw new BankingRuleException("The correction request scope is invalid.");
            var cursor = (long?)movementId.Value;
            var visited = new HashSet<long>();
            while (cursor.HasValue && !allowed.Contains(cursor.Value))
            {
                if (!visited.Add(cursor.Value))
                    throw new BankingRuleException("The correction lineage is invalid.");
                cursor = await db.CustomerMovements.Where(m => m.Id == cursor.Value &&
                    m.CashSessionId == sessionId && m.CustomerTransaction!.AgentId == actor.AgentId)
                    .Select(m => m.SupersedesMovementId).SingleOrDefaultAsync(cancellationToken);
            }
            if (!cursor.HasValue)
                throw new UnauthorizedAccessException("This movement is outside the administrator's correction request.");
        }
        else if (!request.AllowMissingTransactions)
        {
            throw new UnauthorizedAccessException("This request does not permit adding missing transactions.");
        }
        request.Version++;
        return request;
    }

    private async Task EnsurePendingCaseAsync(CustomerTransaction entry, string notes, CancellationToken cancellationToken)
    {
        var pending = await db.PendingTransactions.SingleOrDefaultAsync(p => p.CustomerTransactionId == entry.Id, cancellationToken);
        if (pending == null)
        {
            var bankWalletId = await db.BankAccounts.Where(a => a.Id == entry.BankAccountId)
                .Select(a => a.WalletId).SingleAsync(cancellationToken);
            db.PendingTransactions.Add(new PendingTransaction
            {
                CustomerTransactionId = entry.Id, CashSessionId = entry.OriginatingSessionId,
                AgentId = entry.AgentId, WalletId = entry.Kind == CustomerTransactionKind.Deposit ? bankWalletId : entry.CashWalletId,
                Type = PendingTransactionType.OutboundTransferFailed, Status = PendingTransactionStatus.Open,
                Amount = entry.Amount, Currency = entry.Currency, CustomerAccountNumber = entry.CustomerIdentifier,
                Notes = notes.Trim(), RecordedByUserId = entry.RecordedByUserId, CreatedAt = clock.UtcNow
            });
        }
        else
        {
            pending.UpdatedAt = clock.UtcNow;
            if (pending.Status is PendingTransactionStatus.Resolved or PendingTransactionStatus.Cancelled)
                pending.Status = PendingTransactionStatus.Open;
        }
    }

    private async Task TouchAsync(
        CustomerTransaction entry, long recordedSessionId, DateTimeOffset occurredAt, string userId, CancellationToken cancellationToken)
    {
        var session = await db.CashSessions.SingleAsync(s => s.Id == recordedSessionId, cancellationToken);
        session.RegisterVersion++;
        session.Version++;
        var account = await db.BankAccounts.SingleAsync(a => a.Id == entry.BankAccountId, cancellationToken);
        account.Version++;
        var date = clock.GetBusinessDate(occurredAt);
        var reviews = await db.BankReconciliationReviews.Where(r => r.BankAccountId == entry.BankAccountId &&
                ((r.FromDate <= date && r.ToDate >= date) ||
                 db.BankReconciliationResults.Any(result => result.ReviewId == r.Id &&
                     result.CashSessionId == entry.OriginatingSessionId)))
            .ToListAsync(cancellationToken);
        foreach (var review in reviews)
        {
            review.Version++;
            if (review.Status is BankReconciliationStatus.Reconciled or BankReconciliationStatus.WithExceptions)
            {
                review.Status = BankReconciliationStatus.NeedsReview;
                db.BankReconciliationDecisions.Add(new BankReconciliationDecision
                {
                    BankAccountId = account.Id, ReviewId = review.Id, CashSessionId = recordedSessionId,
                    Kind = ReconciliationDecisionKind.ReviewInvalidated,
                    DetailsJson = JsonSerializer.Serialize(new { CustomerTransactionId = entry.Id, session.RegisterVersion }),
                    Reason = "Customer register or case evidence changed.",
                    PerformedByUserId = userId, PerformedAt = clock.UtcNow
                });
            }
        }
    }

    private static void ValidateInitial(CustomerTransactionForm form)
    {
        ValidateCommand(form.CommandId);
        ValidateAmount(form.Amount);
        if (!Enum.IsDefined(form.Kind) || !Enum.IsDefined(form.Outcome))
            throw new BankingRuleException("Choose a valid transaction type and outcome.");
        ValidateIdentifier(form.CustomerIdentifier);
        ValidateReference(form.BankReference, form.ReferenceUnavailable);
        if (form.Outcome != CustomerCaptureOutcome.Completed || !form.BankMoved || !form.CashExchanged)
            ValidateNotes(form.Notes);
        if (form.Notes?.Length > 2000)
            throw new BankingRuleException("Notes cannot exceed 2,000 characters.");
    }

    private static void ValidateCommand(Guid commandId)
    {
        if (commandId == Guid.Empty)
            throw new BankingRuleException("A stable request identity is required.");
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > MaximumAmount || decimal.Round(amount, 2) != amount)
            throw new BankingRuleException("Enter a positive amount within the supported range and with at most two decimal places.");
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Trim().Length > 256 || identifier.Trim().Any(char.IsControl))
            throw new BankingRuleException("A customer account or phone identifier of at most 256 characters is required.");
    }

    private static void ValidateReference(string? reference, bool unavailable)
    {
        if (reference?.Trim().Length > 256 || reference?.Trim().Any(char.IsControl) == true)
            throw new BankingRuleException("The bank reference cannot exceed 256 characters.");
        if (string.IsNullOrWhiteSpace(reference) != unavailable)
            throw new BankingRuleException("Enter the bank reference, or explicitly indicate that the bank did not supply one.");
    }

    private static void ValidateNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length is < 10 or > 2000)
            throw new BankingRuleException("Provide an explanation between 10 and 2,000 characters.");
    }

    private static string? CleanReference(string? reference) => string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    private static string Hash<T>(T value) => BankingRequestIdentity.Hash(value);
    private static void VerifyReplay(string recordedHash, string requestedHash)
    {
        if (recordedHash != requestedHash)
            throw new BankingRuleException("This request identity was already used for different transaction details.");
    }

    private static CustomerMovementKind InitialKind(CustomerCaptureOutcome outcome) => outcome switch
    {
        CustomerCaptureOutcome.Completed => CustomerMovementKind.Completed,
        CustomerCaptureOutcome.Pending => CustomerMovementKind.PendingAttempt,
        CustomerCaptureOutcome.Failed => CustomerMovementKind.FailedAttempt,
        _ => throw new BankingRuleException("Unsupported transaction outcome.")
    };

    private static List<CustomerMovement> ActiveMovements(CustomerTransaction entry)
    {
        var superseded = entry.Movements.Where(m => m.SupersedesMovementId.HasValue)
            .Select(m => m.SupersedesMovementId!.Value).ToHashSet();
        return entry.Movements.Where(m => !superseded.Contains(m.Id)).ToList();
    }

    private static CustomerMovement PrimaryMovement(IReadOnlyCollection<CustomerMovement> movements) =>
        movements.SingleOrDefault(m => m.RelatedMovementId == null &&
            m.Kind is CustomerMovementKind.Completed or CustomerMovementKind.PendingAttempt or CustomerMovementKind.FailedAttempt or CustomerMovementKind.Voided)
        ?? throw new BankingRuleException("The customer transaction has no valid primary record.");

    private static CustomerTransactionDto Map(CustomerTransaction entry)
    {
        var active = ActiveMovements(entry);
        var primary = PrimaryMovement(active);
        var kind = primary.TransactionKind ?? entry.Kind;
        var settled = active.Sum(m => m.CustomerSettledAmount);
        var superseded = entry.Movements.Where(m => m.SupersedesMovementId.HasValue)
            .Select(m => m.SupersedesMovementId!.Value).ToHashSet();
        return new CustomerTransactionDto
        {
            Id = entry.Id, AgentId = entry.AgentId, OriginatingSessionId = entry.OriginatingSessionId, BankAccountId = entry.BankAccountId,
            BankName = entry.BankAccount?.BankName ?? throw new BankingRuleException("The transaction's bank account link is invalid."),
            Kind = kind, Amount = primary.Amount,
            Currency = entry.Currency, CustomerIdentifier = primary.CustomerIdentifier ?? entry.CustomerIdentifier,
            OccurredAt = primary.OccurredAt, CustomerSettledAmount = settled,
            RefundedAmount = active.Where(m => m.Kind == CustomerMovementKind.Refund).Sum(m => -m.CashEffect),
            CashHeld = kind == CustomerTransactionKind.Deposit ? Math.Max(0, active.Sum(m => m.CashEffect) - settled) : 0,
            CustomerCashDue = kind == CustomerTransactionKind.Deposit ? Math.Max(0, settled - active.Sum(m => m.CashEffect)) : 0,
            CancelledAmount = active.Where(m => m.Kind == CustomerMovementKind.Cancelled).Sum(m => m.Amount),
            BankExposure = active.Sum(m => m.BankEffect) - (kind == CustomerTransactionKind.Deposit ? -settled : settled),
            SupportTicketNumber = active.Where(m => !string.IsNullOrWhiteSpace(m.SupportTicketNumber))
                .OrderByDescending(m => m.Id).Select(m => m.SupportTicketNumber).FirstOrDefault(),
            IsVoided = primary.Kind == CustomerMovementKind.Voided,
            Movements = entry.Movements.OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).Select(m =>
                new CustomerMovementDto(m.Id, m.CashSessionId, m.Kind, m.Amount, m.CashEffect, m.BankEffect,
                    m.CustomerSettledAmount, m.BankReference, m.ReferenceUnavailable, m.OccurredAt, m.RecordedAt,
                    m.RecordedByUserId, m.EvidenceFileId, m.RelatedMovementId, m.SupersedesMovementId,
                    superseded.Contains(m.Id), m.Notes)).ToList()
        };
    }
}
