using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.Extensions.Logging;

namespace EastSeat.Agenti.Android.Pages;

public sealed class BankReviewPage(
    IBankingApiClient api, IAuthService auth, IBankingDraftStore drafts, ILogger<BankReviewPage> logger)
    : BankingPageBase(api, auth, drafts, logger)
{
    protected override async Task LoadAsync()
    {
        if (!IsAdmin) throw new BankingRuleException("Administrator access is required for reconciliation.");
        var accounts = await Api.GetAsync<List<BankAccountDto>>("admin/accounts");
        Title = "Bank reconciliation";
        Reset("Reconciliation accounts");
        Action("Refresh", LoadAsync);
        if (accounts.Count == 0) Text("Configure a bank account before starting a review.");
        foreach (var account in accounts)
            Action($"{account.BankName} - {account.AccountIdentifier}", () => ReviewsAsync(account));
    }

    private async Task ReviewsAsync(BankAccountDto account)
    {
        var reviews = await Api.GetAsync<List<BankReviewListItemDto>>($"admin/accounts/{account.Id}/reviews");
        Reset($"{account.BankName}: reviews");
        Action("Back to accounts", LoadAsync);
        Action("Start date-range review", () => NewReviewAsync(account));
        if (reviews.Count == 0) Text("No reviews yet. Import verified source evidence, then choose a daily, weekly, or monthly range.");
        foreach (var review in reviews)
            Action($"{review.FromDate:yyyy-MM-dd} - {review.ToDate:yyyy-MM-dd}: {review.Status}", () => ReviewAsync(review.Id));
    }

    private async Task NewReviewAsync(BankAccountDto account)
    {
        Reset("New review window");
        Text("Each session and intervening overnight interval is assessed independently.");
        var key = $"review-window-{account.Id}";
        var draft = await EditAsync(key, () => new ReviewWindowDraft());
        Field(draft, "from", "First business date (yyyy-MM-dd)", draft.Form.FromDate, value => draft.Form.FromDate = value);
        Field(draft, "to", "Last business date (yyyy-MM-dd)", draft.Form.ToDate, value => draft.Form.ToDate = value);
        Action("Create review", async () =>
        {
            await ReadFormAsync();
            var review = await Api.PostAsync<CreateBankReviewRequest, BankReconciliationReviewDto>("admin/reviews",
                new(account.Id, Date(draft.Form.FromDate), Date(draft.Form.ToDate)));
            Drafts.Remove(UserId, key);
            await ReviewAsync(review.Id);
        });
        Action("Keep draft and return", () => ReviewsAsync(account));
    }

    private async Task ReviewAsync(long id)
    {
        var review = await Api.GetAsync<BankReconciliationReviewDto>($"admin/reviews/{id}");
        Reset($"{review.BankName}: {review.FromDate:yyyy-MM-dd} - {review.ToDate:yyyy-MM-dd}");
        Text($"{review.Status}; revision {review.Version}");
        Action("Back to accounts", LoadAsync);
        Action("Refresh evidence and decisions", () => ReviewAsync(id));
        foreach (var issue in review.PeriodIssues) Text(issue);
        Action("Match unique exact candidates", async () =>
        {
            await Api.PostAsync<ReviewVersionRequest, BankReconciliationReviewDto>($"admin/reviews/{id}/auto-match", new(review.Version));
            await ReviewAsync(id);
        });
        Action("Manual match", () => MatchAsync(review));
        Action("Classify a bank-only movement", () => ClassifyAsync(review));
        if (review.Sessions.Count == 0) Text("No sessions are covered. This review cannot certify a closing.");
        foreach (var session in review.Sessions)
        {
            Text($"Session {session.CashSessionId} | {session.SessionDate:yyyy-MM-dd} | {session.Status} | approval {session.ApprovalStatus}");
            foreach (var wallet in session.Wallets)
                Text($"{wallet.WalletName}: expected {wallet.ExpectedClosing:N2}, counted {wallet.ReportedClosing:N2}, difference {wallet.Variance:N2}");
            Text($"Bank opening: {session.StatementOpeningBalance:N2}; closing: {session.StatementClosingBalance:N2}; unexplained: {session.UnexplainedBankDifference:N2}");
            foreach (var reason in session.BlockingReasons) Text(reason);
            foreach (var note in session.Notes) Text(note);
            Action("Record documented exception", () => ExceptionAsync(review, session));
            if (session.ClosingCountId.HasValue)
                Action("Return scoped corrections", () => CorrectionAsync(review, session));
            Action("Approve this frozen closing", async () =>
            {
                if (!await DisplayAlertAsync("Financial approval",
                    "Certify this closing against the displayed evidence? This does not move vault or wallet funds.", "Approve", "Cancel"))
                    return;
                var key = $"closing-approval-{id}-{session.CashSessionId}-{review.Version}";
                var command = await Drafts.LoadAsync<ApprovalDraft>(UserId, key) ?? new ApprovalDraft();
                await Drafts.SaveAsync(UserId, key, command);
                await Api.PostAsync<ApproveBankClosingRequest, BankReconciliationReviewDto>($"admin/reviews/{id}/approve",
                    new(session.CashSessionId, command.CommandId, review.Version, session.BasisHash));
                Drafts.Remove(UserId, key);
                await ReviewAsync(id);
            }).IsEnabled = session.CanApprove;
        }
        Text("Bank source entries");
        foreach (var bank in review.BankEntries)
        {
            Text($"Bank #{bank.Id} | {Time(bank.LedgerAt)} | {bank.SignedAmount:N2} | balance {bank.RunningBalance:N2} | {bank.TransactionNumber ?? bank.Reference} | {bank.Classification}");
            Text(bank.Narration);
            Original(bank.EvidenceFileId);
        }
        Text("Internal bank legs");
        foreach (var leg in review.RegisterEntries)
            Text($"{leg.Kind} #{leg.Id} | session {leg.CashSessionId} | {leg.SignedAmount:N2} | {leg.Reference} | {leg.CustomerIdentifier}");
        foreach (var match in review.Matches)
        {
            Text($"Match {match.Id}: bank #{match.BankTransactionId} to {match.Kind} #{match.SourceId}");
            Action($"Remove match {match.Id}", async () =>
            {
                var reason = await DisplayPromptAsync("Remove match", "Reason (at least 10 characters)");
                if (reason == null) return;
                await Api.PostAsync<ReviewReasonRequest, BankReconciliationReviewDto>($"admin/reviews/{id}/matches/{match.Id}/remove", new(review.Version, reason));
                await ReviewAsync(id);
            });
        }
        foreach (var exception in review.Exceptions)
        {
            Text($"Exception {exception.Id}: {exception.Kind} {exception.Amount:N2} | {(exception.IsResolved ? "resolved" : "outstanding")}");
            Text(exception.Explanation);
            Original(exception.EvidenceFileId);
            if (!exception.IsResolved) Action($"Resolve exception {exception.Id}", () => ResolveAsync(review, exception));
        }
        foreach (var request in review.CorrectionRequests)
            Text($"Correction {request.Id}: session {request.CashSessionId}, {(request.IsOpen ? "with agent" : "resubmitted")} - {request.Reason}");
    }

    private async Task MatchAsync(BankReconciliationReviewDto review)
    {
        Reset("Manual match");
        var key = $"bank-match-{review.Id}-{review.Version}";
        var draft = await EditAsync(key, () => new BankMatchForm { ExpectedReviewVersion = review.Version });
        var form = draft.Form;
        Select("Bank entry", review.BankEntries, b => $"#{b.Id} {b.SignedAmount:N2} {b.Reference ?? b.TransactionNumber}",
            b => b.Id == form.BankTransactionId, b => form.BankTransactionId = b.Id);
        Select("Internal leg", review.RegisterEntries, l => $"{l.Kind} #{l.Id} {l.SignedAmount:N2} {l.Reference}",
            l => l.Id == form.SourceId && l.Kind == form.Kind, l => { form.SourceId = l.Id; form.Kind = l.Kind; });
        Field(draft, "related", "Related movement ID (for an expected customer settlement)", form.RelatedMovementId?.ToString() ?? "",
            value => form.RelatedMovementId = OptionalId(value), true);
        Field(draft, "reason", "Why these are the same bank occurrence", form.Reason, value => form.Reason = value);
        Action("Save match", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<BankMatchForm, BankReconciliationReviewDto>($"admin/reviews/{review.Id}/match", form);
            Drafts.Remove(UserId, key);
            await ReviewAsync(review.Id);
        });
        Action("Keep draft and return", () => ReviewAsync(review.Id));
    }

    private async Task ClassifyAsync(BankReconciliationReviewDto review)
    {
        Reset("Classify genuine bank-only movement");
        var key = $"classification-{review.Id}-{review.Version}";
        var draft = await EditAsync(key, () => new BankClassificationForm { ExpectedReviewVersion = review.Version });
        var form = draft.Form;
        Select("Bank entry", review.BankEntries, b => $"#{b.Id} {b.SignedAmount:N2} {b.Narration}", b => b.Id == form.BankTransactionId,
            b => { form.BankTransactionId = b.Id; form.EvidenceFileId = b.EvidenceFileId; });
        SelectEnum("Classification", form.Classification, value => form.Classification = value);
        Field(draft, "case", "Related customer transaction (if applicable)", form.RelatedCustomerTransactionId?.ToString() ?? "",
            value => form.RelatedCustomerTransactionId = OptionalId(value), true);
        Field(draft, "reason", "Verified explanation", form.Reason, value => form.Reason = value);
        Action("Save classification", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<BankClassificationForm, BankReconciliationReviewDto>($"admin/reviews/{review.Id}/classify", form);
            Drafts.Remove(UserId, key);
            await ReviewAsync(review.Id);
        });
        Action("Keep draft and return", () => ReviewAsync(review.Id));
    }

    private async Task ExceptionAsync(BankReconciliationReviewDto review, SessionReconciliationDto session)
    {
        Reset($"Exception for session {session.CashSessionId}");
        var key = $"exception-{review.Id}-{session.CashSessionId}-{review.Version}";
        var draft = await EditAsync(key, () => new BankExceptionForm
        {
            ExpectedReviewVersion = review.Version, CashSessionId = session.CashSessionId
        });
        var form = draft.Form;
        SelectEnum("Exception type", form.Kind, value => form.Kind = value);
        Field(draft, "amount", "Amount", Number(form.Amount), value => form.Amount = Money(value), true);
        Select("Affected internal bank leg (if any)", review.RegisterEntries.Where(l => l.CashSessionId == session.CashSessionId).ToList(),
            l => $"{l.Kind} #{l.Id} {l.SignedAmount:N2}", l => (l.Kind == BankLegKind.CustomerMovement && l.Id == form.CustomerMovementId) ||
                (l.Kind == BankLegKind.BankRun && l.Id == form.BankRunId) || (l.Kind == BankLegKind.WalletAdjustment && l.Id == form.WalletAdjustmentId),
            l =>
            {
                form.CustomerMovementId = l.Kind == BankLegKind.CustomerMovement ? l.Id : null;
                form.BankRunId = l.Kind == BankLegKind.BankRun ? l.Id : null;
                form.WalletAdjustmentId = l.Kind == BankLegKind.WalletAdjustment ? l.Id : null;
                form.CustomerTransactionId = l.CustomerTransactionId;
            });
        Field(draft, "bank", "Bank occurrence ID (if present)", form.BankTransactionId?.ToString() ?? "", value => form.BankTransactionId = OptionalId(value), true);
        Field(draft, "customer", "Customer transaction ID (if no internal leg was selected)", form.CustomerTransactionId?.ToString() ?? "",
            value => { if (!string.IsNullOrWhiteSpace(value)) form.CustomerTransactionId = ParseId(value); }, true);
        Toggle("The bank explanation has been verified", form.BankExplanationVerified, value => form.BankExplanationVerified = value);
        Select("Is the delayed posting reflected in the observed closing?", new[] { "Not applicable / unknown", "Yes", "No" },
            value => value, value => value == (form.ReflectedInClosingBalance == true ? "Yes" : form.ReflectedInClosingBalance == false ? "No" : "Not applicable / unknown"),
            value => form.ReflectedInClosingBalance = value == "Yes" ? true : value == "No" ? false : null);
        Field(draft, "explanation", "Specific explanation (not a general balancing adjustment)", form.Explanation, value => form.Explanation = value);
        if (form.EvidenceFileId > 0) Original(form.EvidenceFileId);
        Evidence(draft, () => review.BankAccountId, false, evidence => form.EvidenceFileId = evidence);
        Action("Save evidenced exception", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<BankExceptionForm, BankReconciliationReviewDto>($"admin/reviews/{review.Id}/exceptions", form);
            Drafts.Remove(UserId, key);
            await ReviewAsync(review.Id);
        });
        Action("Keep draft and return", () => ReviewAsync(review.Id));
    }

    private async Task ResolveAsync(BankReconciliationReviewDto review, BankExceptionDto exception)
    {
        Reset($"Resolve exception {exception.Id}");
        var key = $"resolve-{review.Id}-{exception.Id}-{review.Version}";
        var draft = await EditAsync(key, () => new ResolveDraft());
        Select("Verified resolving bank occurrence", review.BankEntries, b => $"#{b.Id} {b.SignedAmount:N2} {b.Reference}",
            b => b.Id == draft.Form.BankTransactionId, b => draft.Form.BankTransactionId = b.Id);
        Field(draft, "reason", "Resolution evidence and reason", draft.Form.Reason, value => draft.Form.Reason = value);
        Action("Clear reconciliation flag", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<ResolveBankExceptionRequest, BankReconciliationReviewDto>($"admin/reviews/{review.Id}/exceptions/{exception.Id}/resolve",
                new(draft.Form.BankTransactionId, review.Version, draft.Form.Reason));
            Drafts.Remove(UserId, key);
            await ReviewAsync(review.Id);
        });
        Action("Keep draft and return", () => ReviewAsync(review.Id));
    }

    private async Task CorrectionAsync(BankReconciliationReviewDto review, SessionReconciliationDto session)
    {
        Reset($"Return session {session.CashSessionId} for correction");
        var key = $"return-{review.Id}-{session.CashSessionId}-{review.Version}";
        var draft = await EditAsync(key, () => new ClosingCorrectionForm
        {
            ExpectedReviewVersion = review.Version, CashCountId = session.ClosingCountId!.Value
        });
        Text("Grant only the historical movements needing correction. No new trading or count rewriting is allowed.");
        Field(draft, "movements", "Allowed movement IDs (comma-separated)", string.Join(",", draft.Form.MovementIds),
            value => draft.Form.MovementIds = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(ParseId).ToList());
        Toggle("Allow adding omitted original receipts", draft.Form.AllowMissingTransactions, value => draft.Form.AllowMissingTransactions = value);
        Field(draft, "reason", "What must be corrected and why", draft.Form.Reason, value => draft.Form.Reason = value);
        Action("Send scoped correction request", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<ClosingCorrectionForm, BankReconciliationReviewDto>($"admin/reviews/{review.Id}/corrections", draft.Form);
            Drafts.Remove(UserId, key);
            await ReviewAsync(review.Id);
        });
        Action("Keep draft and return", () => ReviewAsync(review.Id));
    }

    public sealed class ReviewWindowDraft
    {
        public string FromDate { get; set; } = string.Empty;
        public string ToDate { get; set; } = string.Empty;
    }
    public sealed class ApprovalDraft { public Guid CommandId { get; set; } = Guid.NewGuid(); }
    public sealed class ResolveDraft
    {
        public long BankTransactionId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
