using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.BankRuns;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
using Microsoft.Extensions.Logging;

namespace EastSeat.Agenti.Android.Pages;

public sealed class BankOperationsPage(
    IBankingApiClient api, IAuthService auth, IBankingDraftStore drafts, ILogger<BankOperationsPage> logger)
    : BankingPageBase(api, auth, drafts, logger)
{
    protected override async Task LoadAsync()
    {
        var current = await Api.GetAsync<CurrentSessionDto>("current");
        Title = "Cash operations";
        Reset("Session counts and bank runs");
        Text(current.StatusText);
        Text(current.WorkflowVersion == SessionWorkflowVersion.BankReconciliation
            ? "Closing submission stops trading and returns reported float once. Admin bank review is separate; an older pending approval does not prevent the next day's opening."
            : "Legacy workflow: this session retains its existing approval and settlement rules.");
        if (!string.IsNullOrWhiteSpace(current.BlockReason)) Text(current.BlockReason);
        Action("Refresh", LoadAsync);
        Action("Opening count", () => CountAsync(current, true)).IsEnabled = current.CanPerformOpeningCount;
        Action("Closing count", () => CountAsync(current, false)).IsEnabled = current.CanPerformClosingCount;
        if (current.ClosingCountId.HasValue)
            Action("View submitted closing", () => ViewCountAsync(current.ClosingCountId.Value));
        if (current.CanRecordBankRun) Action("Record bank run", () => BankRunAsync(current));
        if (current.CanRecordAdjustment) Action("Record wallet adjustment", () => AdjustmentAsync(current));
        if (Auth.CurrentUser?.AgentId.HasValue == true)
            Action("Current bank runs", async () =>
            {
                var runs = await Api.GetAsync<List<BankRunDto>>("bank-runs");
                Reset("Recorded bank runs");
                Action("Back", LoadAsync);
                if (runs.Count == 0) Text("No bank runs in the current session.");
                foreach (var run in runs)
                    Text($"#{run.Id}: {run.FromWalletName} -> {run.ToWalletName} | {run.Amount:N2} {run.Currency} | {run.ReceiptNumber}");
            });
        if (CanApproveOpening) Action("Pending count approvals", PendingAsync);
    }

    private async Task CountAsync(CurrentSessionDto current, bool opening)
    {
        var countId = opening ? current.OpeningCountId : current.ClosingCountId;
        var serverForm = countId.HasValue
            ? await Api.GetAsync<CashCountFormModel>($"counts/{countId}")
            : await Api.GetAsync<CashCountFormModel>($"counts/initialize?isOpening={opening.ToString().ToLowerInvariant()}");
        if (serverForm.IsReadOnly)
        {
            await ViewCountAsync(serverForm.CashCountId!.Value);
            return;
        }
        Reset(opening ? "Opening observation" : "Closing observation");
        var key = $"count-{serverForm.CashSessionId}-{opening}";
        var draft = await EditAsync(key, () => serverForm);
        var form = draft.Form;
        if (serverForm.CashCountId.HasValue) form.CashCountId = serverForm.CashCountId;
        Text("Enter each actual wallet balance and when it was observed. Manual amounts replace any old denomination breakdown.");
        foreach (var wallet in form.WalletEntries)
        {
            Text($"{wallet.WalletName} ({wallet.WalletTypeName}) | expected {wallet.ExpectedBalance:N2}");
            Field(draft, $"amount-{wallet.WalletId}", "Counted amount", Number(wallet.CountedAmount),
                value => { wallet.CountedAmount = Money(value); wallet.Denominations = null; }, true);
            if (current.WorkflowVersion == SessionWorkflowVersion.BankReconciliation)
            {
                var observation = Field(draft, $"observed-{wallet.WalletId}", "Actually observed at (Uganda: yyyy-MM-dd HH:mm:ss)",
                    wallet.ObservedAt.HasValue ? Time(wallet.ObservedAt.Value) : "", value => wallet.ObservedAt = BusinessTimeText.Parse(value));
                Action("Confirm this balance was observed now", async () =>
                {
                    var refreshed = await Api.GetAsync<CurrentSessionDto>("current");
                    if (!refreshed.ServerTime.HasValue) throw new BankingRuleException("The server did not supply an observation time.");
                    observation.Text = Time(refreshed.ServerTime.Value);
                    await SaveDraftAsync();
                });
            }
        }
        Field(draft, "explanation", "Count explanation / variance notes", form.Explanation ?? "", value => form.Explanation = value);
        Action("Save draft", async () =>
        {
            await ReadFormAsync();
            var result = await Api.PostAsync<CashCountFormModel, CashCountSaveResult>("counts", form);
            form.CashCountId = result.CashCountId;
            form.CashSessionId = result.CashSessionId;
            await SaveDraftAsync();
            Text("Draft saved. It has not been submitted.");
        });
        Action(opening ? "Submit for opening approval" : "Submit and end trading", async () =>
        {
            if (!await DisplayAlertAsync("Submit count", opening ? "Send this opening count for approval?" :
                "Freeze these observations and stop trading in this session? The administrator will approve the financial evidence separately.", "Submit", "Cancel"))
                return;
            await ReadFormAsync();
            if (form.CashCountId.HasValue)
            {
                var saved = await Api.GetAsync<CashCountFormModel>($"counts/{form.CashCountId}");
                if (saved.IsReadOnly)
                {
                    Drafts.Remove(UserId, key);
                    await ViewCountAsync(saved.CashCountId!.Value);
                    return;
                }
            }
            var result = await Api.PostAsync<CashCountFormModel, CashCountSaveResult>("counts", form);
            form.CashCountId = result.CashCountId ?? throw new BankingRuleException("The saved count identity was not returned.");
            await SaveDraftAsync();
            await Api.PostAsync<CashCountSaveResult>($"counts/{form.CashCountId}/submit");
            Drafts.Remove(UserId, key);
            await LoadAsync();
        });
        Action("Keep draft and return", LoadAsync);
    }

    private async Task ViewCountAsync(long id)
    {
        var form = await Api.GetAsync<CashCountFormModel>($"counts/{id}");
        Reset($"{(form.IsOpening ? "Opening" : "Closing")} count {id}");
        Text(form.IsReadOnly ? "Submitted observations are read-only." : "This count is still editable through its session.");
        foreach (var wallet in form.WalletEntries)
            Text($"{wallet.WalletName}: {wallet.CountedAmount:N2} | observed {(wallet.ObservedAt.HasValue ? Time(wallet.ObservedAt.Value) : "not recorded (legacy)")}");
        Text(form.Explanation ?? string.Empty);
        Action("Back", LoadAsync);
    }

    private async Task BankRunAsync(CurrentSessionDto current)
    {
        var wallets = await Api.GetAsync<List<WalletTypeOptionDto>>("bank-runs/wallet-types");
        Reset("Record physical bank run");
        Text("This is an internal cash-to-bank transfer, not a customer transaction. Available balances are recalculated before the transfer.");
        var key = $"bank-run-{current.SessionId}";
        var draft = await EditAsync(key, () => new BankRunFormModel { CommandId = Guid.NewGuid() });
        var form = draft.Form;
        Select("Destination wallet", wallets, w => w.Name, w => w.WalletTypeId == form.ToWalletTypeId, w => form.ToWalletTypeId = w.WalletTypeId);
        Field(draft, "amount", "Amount deposited", Number(form.Amount), value => form.Amount = Money(value), true);
        Field(draft, "reference", "Deposit receipt reference", form.ReceiptNumber ?? "", value => form.ReceiptNumber = value);
        Field(draft, "time", "Receipt time (Uganda: yyyy-MM-dd HH:mm:ss)", form.OccurredAt.HasValue ? Time(form.OccurredAt.Value) : "",
            value => form.OccurredAt = string.IsNullOrWhiteSpace(value) && current.WorkflowVersion == SessionWorkflowVersion.Legacy ? null : BusinessTimeText.Parse(value));
        Field(draft, "notes", "Notes", form.Notes ?? "", value => form.Notes = value);
        Action("Attach deposit slip image", async () =>
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { FileTypes = FilePickerFileType.Images, PickerTitle = "Deposit slip" });
            if (file == null) return;
            await using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + count > 5 * 1024 * 1024) throw new BankingRuleException("The deposit slip image must be at most 5 MB.");
                buffer.Write(chunk, 0, count);
            }
            form.ReceiptImage = buffer.ToArray();
            form.ReceiptImageContentType = file.ContentType;
            await SaveDraftAsync();
            Text($"Attached deposit slip: {file.FileName}");
        });
        Action("Save bank run (or retry)", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<BankRunFormModel, BankRunSaveResult>("bank-runs", form);
            Drafts.Remove(UserId, key);
            await LoadAsync();
        });
        Action("Keep draft and return", LoadAsync);
    }

    private async Task AdjustmentAsync(CurrentSessionDto current)
    {
        var options = await Api.GetAsync<CashCountFormModel>("counts/initialize?isOpening=false");
        Reset("Record wallet adjustment");
        var key = $"wallet-adjustment-{current.SessionId}";
        var draft = await EditAsync(key, () => new WalletAdjustmentFormModel { CommandId = Guid.NewGuid() });
        var form = draft.Form;
        Select("Wallet", options.WalletEntries, w => $"{w.WalletName} | expected {w.ExpectedBalance:N2}", w => w.WalletId == form.WalletId, w => form.WalletId = w.WalletId);
        SelectEnum("Reason", form.Reason, value => form.Reason = value);
        Field(draft, "amount", "Amount", Number(form.Amount), value => form.Amount = Money(value), true);
        Field(draft, "reference", "Bank reference (if applicable)", form.BankReference ?? "", value => form.BankReference = value);
        Field(draft, "time", "Occurrence (Uganda: yyyy-MM-dd HH:mm:ss)", form.OccurredAt.HasValue ? Time(form.OccurredAt.Value) : "",
            value => form.OccurredAt = string.IsNullOrWhiteSpace(value) && current.WorkflowVersion == SessionWorkflowVersion.Legacy ? null : BusinessTimeText.Parse(value));
        Field(draft, "notes", "Explanation", form.Notes ?? "", value => form.Notes = value);
        Action("Request adjustment", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<WalletAdjustmentFormModel, WalletAdjustmentSaveResult>("wallet-adjustments", form);
            Drafts.Remove(UserId, key);
            await LoadAsync();
        });
        Action("Keep draft and return", LoadAsync);
    }

    private async Task PendingAsync()
    {
        var counts = await Api.GetAsync<List<PendingApprovalDto>>("counts/pending");
        var adjustments = await Api.GetAsync<List<WalletAdjustmentDto>>("wallet-adjustments/pending");
        Reset("Count approvals");
        Action("Back", LoadAsync);
        Action("Refresh", PendingAsync);
        if (counts.Count == 0) Text("No counts await approval.");
        foreach (var count in counts)
        {
            Text($"#{count.CashCountId} {count.SessionDate:yyyy-MM-dd} | {count.AgentName} | {(count.IsOpening ? "Opening" : "Closing")} | {count.TotalAmount:N2}");
            Text(count.Explanation ?? string.Empty);
            if (!count.IsOpening && count.WorkflowVersion == SessionWorkflowVersion.BankReconciliation)
            {
                Text("Administrator bank reconciliation is required. There is no direct approval shortcut.");
                if (IsAdmin) Action("Open bank reconciliation", () => Shell.Current.GoToAsync("//bankreviews"));
                continue;
            }
            Action($"Approve count {count.CashCountId}", async () =>
            {
                if (!await DisplayAlertAsync("Approve count", "Approve this count and its applicable internal settlement?", "Approve", "Cancel")) return;
                await Api.PostAsync<CashCountSaveResult>($"counts/{count.CashCountId}/approve");
                await PendingAsync();
            });
            Action($"Reject count {count.CashCountId}", async () =>
            {
                var reason = await DisplayPromptAsync("Reject count", "Reason");
                if (reason == null) return;
                await Api.PostAsync<ReviewReasonRequest, CashCountSaveResult>($"counts/{count.CashCountId}/reject", new(0, reason));
                await PendingAsync();
            });
        }
        Text("Wallet adjustments awaiting their separate approval");
        if (adjustments.Count == 0) Text("No pending wallet adjustments.");
        foreach (var adjustment in adjustments)
        {
            Text($"#{adjustment.Id}: {adjustment.AgentName} | {adjustment.WalletName} | {adjustment.Amount:N2} | {adjustment.ReasonDisplay}");
            Text(adjustment.Notes ?? string.Empty);
            Action($"Approve adjustment {adjustment.Id}", async () =>
            {
                await Api.PostAsync<WalletAdjustmentSaveResult>($"wallet-adjustments/{adjustment.Id}/approve");
                await PendingAsync();
            });
            Action($"Reject adjustment {adjustment.Id}", async () =>
            {
                var reason = await DisplayPromptAsync("Reject adjustment", "Reason (at least 10 characters)");
                if (reason == null) return;
                await Api.PostAsync<ReviewReasonRequest, WalletAdjustmentSaveResult>($"wallet-adjustments/{adjustment.Id}/reject", new(0, reason));
                await PendingAsync();
            });
        }
    }
}
