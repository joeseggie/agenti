using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using Microsoft.Extensions.Logging;

namespace EastSeat.Agenti.Android.Pages;

public sealed class CustomerBankingPage(
    IBankingApiClient api, IAuthService auth, IBankingDraftStore drafts, ILogger<CustomerBankingPage> logger)
    : BankingPageBase(api, auth, drafts, logger)
{
    protected override async Task LoadAsync()
    {
        var session = await Api.GetAsync<CurrentSessionDto>("current");
        var cases = await Api.GetAsync<List<CustomerTransactionDto>>("transactions/cases");
        var corrections = await Api.GetAsync<List<BankCorrectionRequestDto>>("corrections");
        var entries = session.SessionId.HasValue
            ? await Api.GetAsync<List<CustomerTransactionDto>>($"transactions/session/{session.SessionId}") : [];
        Title = "Customer banking";
        Reset("Customer register");
        Text(session.StatusText);
        Action("Refresh", LoadAsync);
        if (session.CanRecordCustomerTransactions && session.SessionId.HasValue)
            Action("Record customer transaction", () => CaptureAsync(session.SessionId.Value));
        if (entries.Count == 0) Text("No customer transactions in the current session.");
        foreach (var entry in entries) Transaction(entry);
        Text("Cases across sessions");
        if (cases.Count == 0) Text("No open customer cases.");
        foreach (var entry in cases) Transaction(entry);
        Text("Requested historical corrections");
        if (corrections.Count == 0) Text("No correction requests.");
        foreach (var request in corrections)
        {
            Text($"Session {request.CashSessionId}: {request.Reason}");
            Action("Open correction request", () => CorrectionAsync(request));
        }
        if (session.SessionId.HasValue)
            Action("View a historical session", async () =>
            {
                var input = await DisplayPromptAsync("Session register", "Session ID", keyboard: Keyboard.Numeric);
                if (input == null) return;
                var history = await Api.GetAsync<List<CustomerTransactionDto>>($"transactions/session/{ParseId(input)}");
                Reset($"Session {input}");
                Action("Back to current register", LoadAsync);
                if (history.Count == 0) Text("No captured transactions for this session.");
                foreach (var entry in history) Transaction(entry);
            });
    }

    private void Transaction(CustomerTransactionDto entry)
    {
        Text($"{Time(entry.OccurredAt)} | {entry.Kind} | {entry.Amount:N2} {entry.Currency} | {entry.CustomerIdentifier}");
        Text($"Held cash: {entry.CashHeld:N2}; customer cash due: {entry.CustomerCashDue:N2}; bank exposure: {entry.BankExposure:N2}");
        Action($"Open transaction {entry.Id}", () => DetailAsync(entry.Id));
    }

    private async Task CaptureAsync(long sessionId, BankCorrectionRequestDto? correction = null)
    {
        var options = await Api.GetAsync<CustomerCaptureOptionsDto>(correction == null
            ? $"customer-options/{sessionId}" : $"corrections/{correction.Id}/options");
        Reset(correction == null ? "Capture receipt" : "Record missing historical receipt");
        Text("This changes the expected tally, not stored wallet balances. Use the actual receipt time.");
        var key = $"capture-{sessionId}-{correction?.Id}";
        var draft = await EditAsync(key, () => new CustomerTransactionForm
        {
            CommandId = Guid.NewGuid(), CashSessionId = sessionId, CorrectionRequestId = correction?.Id,
            BankAccountId = options.BankAccounts.FirstOrDefault()?.Id ?? 0,
            CashWalletId = options.CashWallets.FirstOrDefault()?.Id ?? 0
        });
        var form = draft.Form;
        Select("Bank account", options.BankAccounts, a => $"{a.BankName} - {a.WalletName}", a => a.Id == form.BankAccountId, a => form.BankAccountId = a.Id);
        Select("Cash wallet", options.CashWallets, w => w.Name, w => w.Id == form.CashWalletId, w => form.CashWalletId = w.Id);
        SelectEnum("Operation", form.Kind, value => form.Kind = value);
        SelectEnum("Bank outcome", form.Outcome, value => form.Outcome = value);
        Field(draft, "amount", "Amount", Number(form.Amount), value => form.Amount = Money(value), true);
        Field(draft, "customer", "Customer account or phone (preserve leading zeros)", form.CustomerIdentifier, value => form.CustomerIdentifier = value);
        Field(draft, "reference", "Bank transaction number", form.BankReference ?? "", value => form.BankReference = value);
        Toggle("The bank did not supply a reference", form.ReferenceUnavailable, value => form.ReferenceUnavailable = value);
        Toggle("Customer cash actually exchanged", form.CashExchanged, value => form.CashExchanged = value);
        Toggle("Bank float actually moved", form.BankMoved, value => form.BankMoved = value);
        Field(draft, "time", "Receipt time (Uganda: yyyy-MM-dd HH:mm:ss)", Time(form.OccurredAt), value => form.OccurredAt = BusinessTimeText.Parse(value));
        Field(draft, "notes", "Explanation (required for incomplete movements)", form.Notes ?? "", value => form.Notes = value);
        if (form.EvidenceFileId.HasValue) Original(form.EvidenceFileId.Value);
        Evidence(draft, () => form.BankAccountId, false, id => form.EvidenceFileId = id);
        Action("Save transaction", async () =>
        {
            await ReadFormAsync();
            if (form.ReferenceUnavailable) form.BankReference = null;
            await SaveDraftAsync();
            var result = await Api.PostAsync<CustomerTransactionForm, CustomerTransactionDto>("transactions", form);
            Drafts.Remove(UserId, key);
            await DetailAsync(result.Id);
        });
        Action("Keep draft and return", LoadAsync);
    }

    private async Task DetailAsync(long id)
    {
        var entry = await Api.GetAsync<CustomerTransactionDto>($"transactions/{id}");
        Reset($"Transaction {entry.Id}");
        Text($"{entry.Kind}: {entry.Amount:N2} {entry.Currency} | {entry.CustomerIdentifier}");
        Text($"Customer completed: {entry.CustomerSettledAmount:N2}; refunded: {entry.RefundedAmount:N2}; held: {entry.CashHeld:N2}; bank exposure: {entry.BankExposure:N2}");
        Action("Back to register", LoadAsync);
        Action("Refresh case", () => DetailAsync(id));
        Action("Record linked follow-up", () => FollowUpAsync(entry));
        foreach (var movement in entry.Movements)
        {
            Text($"#{movement.Id} {movement.Kind}{(movement.IsSuperseded ? " (superseded)" : "")} | session {movement.CashSessionId}");
            Text($"{Time(movement.OccurredAt)} | cash {movement.CashEffect:N2} | bank {movement.BankEffect:N2} | {movement.BankReference}");
            Text(movement.Notes ?? string.Empty);
            if (movement.EvidenceFileId.HasValue) Original(movement.EvidenceFileId.Value);
        }
    }

    private async Task FollowUpAsync(CustomerTransactionDto entry)
    {
        var session = await Api.GetAsync<CurrentSessionDto>("current");
        Reset($"Follow-up: transaction {entry.Id}");
        var key = $"follow-up-{entry.Id}";
        var draft = await EditAsync(key, () => new CustomerFollowUpForm
        {
            CommandId = Guid.NewGuid(), CashSessionId = session.CanRecordCustomerTransactions ? session.SessionId ?? 0 : 0,
            RelatedMovementId = entry.Movements.LastOrDefault(m => !m.IsSuperseded)?.Id ?? 0,
            Kind = CustomerMovementKind.BankReversal, Amount = entry.Amount
        });
        var form = draft.Form;
        Text("Cash movements use the current trading session. Bank observations can be recorded after closing. A refund or deposit retry requires a verified bank return.");
        var kinds = new[] { CustomerMovementKind.BankReversal, CustomerMovementKind.Retry, CustomerMovementKind.Refund,
            CustomerMovementKind.CashPayment, CustomerMovementKind.CashReceipt, CustomerMovementKind.BankCompletionConfirmed,
            CustomerMovementKind.BankResolutionVerified, CustomerMovementKind.BankPostingObserved, CustomerMovementKind.SupportUpdate, CustomerMovementKind.Cancelled };
        Select("Action", kinds, k => k.ToString(), k => k == form.Kind, k => form.Kind = k);
        Select("Related current movement", entry.Movements.Where(m => !m.IsSuperseded).ToList(),
            m => $"#{m.Id} {m.Kind} {m.Amount:N2}", m => m.Id == form.RelatedMovementId, m => form.RelatedMovementId = m.Id);
        SelectEnum("Bank outcome", form.Outcome, value => form.Outcome = value);
        Field(draft, "amount", "Amount (zero for a support-only update)", Number(form.Amount), value => form.Amount = Money(value), true);
        Field(draft, "reference", "Bank reference", form.BankReference ?? "", value => form.BankReference = value);
        Toggle("Bank reference unavailable", form.ReferenceUnavailable, value => form.ReferenceUnavailable = value);
        Toggle("Bank moved during this attempt", form.BankMoved, value => form.BankMoved = value);
        Toggle("Cash exchanged (never receive held deposit cash twice)", form.CashExchanged, value => form.CashExchanged = value);
        Toggle("Reversal also undid customer completion", form.CustomerCompletionReversed, value => form.CustomerCompletionReversed = value);
        Toggle("Observed posting is a bank credit", form.BankCredit, value => form.BankCredit = value);
        Field(draft, "time", "Actual occurrence time (Uganda: yyyy-MM-dd HH:mm:ss)", Time(form.OccurredAt), value => form.OccurredAt = BusinessTimeText.Parse(value));
        Field(draft, "ticket", "Bank support ticket", form.SupportTicketNumber ?? "", value => form.SupportTicketNumber = value);
        Field(draft, "notes", "Evidence / explanation", form.Notes, value => form.Notes = value);
        if (form.EvidenceFileId.HasValue) Original(form.EvidenceFileId.Value);
        Evidence(draft, () => entry.BankAccountId, false, evidence => form.EvidenceFileId = evidence);
        Action("Record follow-up", async () =>
        {
            await ReadFormAsync();
            if (form.ReferenceUnavailable) form.BankReference = null;
            await SaveDraftAsync();
            await Api.PostAsync<CustomerFollowUpForm, CustomerTransactionDto>($"transactions/{entry.Id}/follow-ups", form);
            Drafts.Remove(UserId, key);
            await DetailAsync(entry.Id);
        });
        Action("Keep draft and return", () => DetailAsync(entry.Id));
    }

    private async Task CorrectionAsync(BankCorrectionRequestDto request)
    {
        var entries = await Api.GetAsync<List<CustomerTransactionDto>>($"transactions/session/{request.CashSessionId}");
        Reset($"Correction request {request.Id}");
        Text(request.Reason);
        Text("This never reopens trading or rewrites the submitted count.");
        Action("Back", LoadAsync);
        if (request.AllowMissingTransactions)
            Action("Add missing original receipt", () => CaptureAsync(request.CashSessionId, request));
        foreach (var entry in entries)
        foreach (var movement in entry.Movements.Where(m => !m.IsSuperseded && m.CashSessionId == request.CashSessionId))
            Action($"Correct #{movement.Id} {movement.Kind} ({entry.CustomerIdentifier})", () => CorrectMovementAsync(request, entry, movement));
        Action("Resubmit corrections", async () =>
        {
            var current = (await Api.GetAsync<List<BankCorrectionRequestDto>>("corrections")).Single(r => r.Id == request.Id);
            await Api.PostAsync<ResubmitBankCorrectionsRequest, string>($"corrections/{request.Id}/submit", new(current.Version));
            await LoadAsync();
        });
    }

    private async Task CorrectMovementAsync(BankCorrectionRequestDto request, CustomerTransactionDto entry, CustomerMovementDto movement)
    {
        Reset($"Correct movement {movement.Id}");
        var key = $"correct-{request.Id}-{movement.Id}";
        var draft = await EditAsync(key, () => new CustomerMovementCorrectionForm
        {
            CommandId = Guid.NewGuid(), CorrectionRequestId = request.Id, MovementId = movement.Id,
            TransactionKind = entry.Kind, Amount = movement.Amount, CustomerIdentifier = entry.CustomerIdentifier,
            Outcome = movement.Kind == CustomerMovementKind.FailedAttempt ? CustomerCaptureOutcome.Failed :
                movement.Kind == CustomerMovementKind.PendingAttempt ? CustomerCaptureOutcome.Pending : CustomerCaptureOutcome.Completed,
            CashExchanged = movement.CashEffect != 0, BankMoved = movement.BankEffect != 0,
            BankReference = movement.BankReference, ReferenceUnavailable = movement.ReferenceUnavailable, OccurredAt = movement.OccurredAt
        });
        var form = draft.Form;
        Toggle("Void an incorrect record", form.Void, value => form.Void = value);
        SelectEnum("Transaction direction", form.TransactionKind, value => form.TransactionKind = value);
        SelectEnum("Original outcome", form.Outcome, value => form.Outcome = value);
        Field(draft, "amount", "Corrected amount", Number(form.Amount), value => form.Amount = Money(value), true);
        Field(draft, "customer", "Customer account/phone", form.CustomerIdentifier, value => form.CustomerIdentifier = value);
        Field(draft, "reference", "Bank reference", form.BankReference ?? "", value => form.BankReference = value);
        Toggle("Reference unavailable", form.ReferenceUnavailable, value => form.ReferenceUnavailable = value);
        Toggle("Actual cash movement", form.CashExchanged, value => form.CashExchanged = value);
        Toggle("Actual bank movement", form.BankMoved, value => form.BankMoved = value);
        Field(draft, "time", "Original occurrence (Uganda: yyyy-MM-dd HH:mm:ss)", Time(form.OccurredAt), value => form.OccurredAt = BusinessTimeText.Parse(value));
        Field(draft, "reason", "Correction reason", form.Reason, value => form.Reason = value);
        if (form.EvidenceFileId > 0) Original(form.EvidenceFileId);
        Evidence(draft, () => entry.BankAccountId, false, evidence => form.EvidenceFileId = evidence);
        Action("Append correction", async () =>
        {
            await ReadFormAsync();
            if (form.ReferenceUnavailable) form.BankReference = null;
            await SaveDraftAsync();
            await Api.PostAsync<CustomerMovementCorrectionForm, CustomerTransactionDto>("transactions/correct", form);
            Drafts.Remove(UserId, key);
            await CorrectionAsync(request);
        });
        Action("Keep draft and return", () => CorrectionAsync(request));
    }
}
