using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.Extensions.Logging;

namespace EastSeat.Agenti.Android.Pages;

public sealed class BankStatementsPage(
    IBankingApiClient api, IAuthService auth, IBankingDraftStore drafts, ILogger<BankStatementsPage> logger)
    : BankingPageBase(api, auth, drafts, logger)
{
    protected override async Task LoadAsync()
    {
        if (!IsAdmin) throw new BankingRuleException("Only administrators can access bank statements.");
        var accounts = await Api.GetAsync<List<BankAccountDto>>("admin/accounts");
        Title = "Bank statements";
        Reset("Bank accounts and statements");
        Action("Refresh accounts", LoadAsync);
        Action("Configure a bank account", () => EditAccountAsync(null));
        if (accounts.Count == 0) Text("No bank accounts configured. Link the branch's bank wallet first.");
        foreach (var account in accounts)
        {
            Text($"{account.BankName} | {account.AccountIdentifier} | {account.Currency} | {account.WalletName}");
            Text($"Workflow {(account.WorkflowEnabled ? "enabled" : "not enabled")}; profile {(account.ProfileVerified ? "verified" : "unverified")}");
            Action($"Statements: {account.BankName}", () => ImportsAsync(account));
            Action($"Edit: {account.BankName}", () => EditAccountAsync(account));
            var activation = Action(account.WorkflowEnabled ? "Stop new workflow enrollment" : "Enable new-session workflow", async () =>
            {
                if (!await DisplayAlertAsync("Workflow enrollment",
                    "This applies only to new sessions. Existing session accounting and settlement will not be replayed.", "Confirm", "Cancel"))
                    return;
                await Api.PostAsync<BankWorkflowRequest, BankAccountDto>($"admin/accounts/{account.Id}/workflow",
                    new(!account.WorkflowEnabled, account.Version));
                await LoadAsync();
            });
            activation.IsEnabled = account.WorkflowEnabled || account.ProfileVerified;
            if (!account.ProfileVerified) Text("Activation is blocked until representative bank files have a verified import profile.");
        }
    }

    private async Task EditAccountAsync(BankAccountDto? account)
    {
        var setup = await Api.GetAsync<BankAccountSetupDto>("admin/accounts/setup");
        Reset(account == null ? "New bank account" : $"Configure {account.BankName}");
        var key = $"bank-account-{account?.Id ?? 0}";
        var draft = await EditAsync(key, () => new BankAccountForm
        {
            WalletId = account?.WalletId ?? setup.Wallets.FirstOrDefault()?.Id ?? 0,
            BankName = account?.BankName ?? "", AccountIdentifier = account?.AccountIdentifier ?? "",
            TimeZoneId = account?.TimeZoneId ?? "Africa/Kampala", BalanceBasis = account?.BalanceBasis ?? BankBalanceBasis.Unverified,
            ImportProfileKey = account?.ImportProfileKey
        });
        var form = draft.Form;
        if (account == null)
            Select("Bank wallet", setup.Wallets, w => $"{w.Name} - {w.AgentName} ({w.Currency})", w => w.Id == form.WalletId, w => form.WalletId = w.Id);
        else Text($"Bound wallet: {account.WalletName}");
        Field(draft, "bank", "Bank name", form.BankName, value => form.BankName = value);
        Field(draft, "identifier", "Bank account identifier", form.AccountIdentifier, value => form.AccountIdentifier = value);
        Field(draft, "timezone", "Bank timezone identifier", form.TimeZoneId, value => form.TimeZoneId = value);
        SelectEnum("Balance basis", form.BalanceBasis, value => form.BalanceBasis = value);
        if (!setup.Profiles.Any(p => p.IsVerified)) Text("No verified production import profiles are installed. Original files can still be retained privately.");
        if (setup.Profiles.Count > 0)
            Select("Import profile", setup.Profiles, p => $"{p.DisplayName} v{p.Version}{(p.IsVerified ? "" : " (not verified)")}",
                p => p.Key == form.ImportProfileKey, p => form.ImportProfileKey = p.Key);
        Action("Save account configuration", async () =>
        {
            await ReadFormAsync();
            if (account == null) await Api.PostAsync<BankAccountForm, BankAccountDto>("admin/accounts", form);
            else await Api.PostAsync<ConfigureBankAccountRequest, BankAccountDto>($"admin/accounts/{account.Id}/configure", new(form, account.Version));
            Drafts.Remove(UserId, key);
            await LoadAsync();
        });
        Action("Keep draft and return", LoadAsync);
    }

    private async Task ImportsAsync(BankAccountDto account)
    {
        var imports = await Api.GetAsync<List<BankStatementImportDto>>($"admin/accounts/{account.Id}/imports");
        Reset($"{account.BankName}: statement imports");
        Action("Back to accounts", LoadAsync);
        Action("Refresh processing status", () => ImportsAsync(account));
        Action("Upload original statement", () => UploadAsync(account));
        if (imports.Count == 0) Text("No statement files have been uploaded for this account.");
        foreach (var import in imports)
        {
            Text($"Import {import.Id}: {import.FileName} | {import.Status}");
            if (!string.IsNullOrWhiteSpace(import.ErrorMessage)) Text(import.ErrorMessage);
            Original(import.EvidenceFileId);
            if (import.Status is StatementImportStatus.ReadyForReview or StatementImportStatus.Committed)
                Action($"Preview import {import.Id}", () => PreviewAsync(account, import.Id));
            if (import.Status == StatementImportStatus.Failed)
                Action("Reprocess with the verified profile", async () =>
                {
                    await Api.PostAsync<StartBankImportRequest, BankStatementImportDto>("admin/imports",
                        new(account.Id, import.EvidenceFileId, true));
                    await ImportsAsync(account);
                });
        }
    }

    private async Task UploadAsync(BankAccountDto account)
    {
        Reset($"Upload to {account.BankName}");
        Text("Upload CSV, a supported Excel workbook, or selectable-text PDF. Unsupported bank formats fail explicitly; they cannot be reconciled as empty statements.");
        var key = $"statement-upload-{account.Id}";
        var draft = await EditAsync(key, () => new StatementUploadDraft());
        if (draft.Form.EvidenceFileId > 0) Original(draft.Form.EvidenceFileId);
        Evidence(draft, () => account.Id, true, id => draft.Form.EvidenceFileId = id);
        Action("Start staged import", async () =>
        {
            if (draft.Form.EvidenceFileId <= 0) throw new BankingRuleException("Upload the statement file first.");
            await Api.PostAsync<StartBankImportRequest, BankStatementImportDto>("admin/imports", new(account.Id, draft.Form.EvidenceFileId));
            Drafts.Remove(UserId, key);
            await ImportsAsync(account);
        });
        Action("Keep draft and return", () => ImportsAsync(account));
    }

    private async Task PreviewAsync(BankAccountDto account, long id)
    {
        var preview = await Api.GetAsync<BankStatementPreviewDto>($"admin/imports/{id}");
        Reset($"Import {id}: {preview.Import.Status}");
        Text($"Coverage: {preview.Import.CoverageFrom:O} to {preview.Import.CoverageTo:O}");
        Text($"Opening {preview.Import.OpeningBalance:N2}; closing {preview.Import.ClosingBalance:N2}; verified coverage: {preview.Import.CoverageVerified}");
        foreach (var error in preview.Errors) Text(error);
        Original(preview.Import.EvidenceFileId);
        Action("Back to imports", () => ImportsAsync(account));
        Action("Refresh preview", () => PreviewAsync(account, id));
        var key = $"statement-commit-{id}-{preview.Import.Version}";
        var draft = await EditAsync(key, () => new CommitStatementForm { ExpectedVersion = preview.Import.Version });
        if (preview.Rows.Count == 0) Text("No extracted transactions. Approval still requires verified period coverage.");
        foreach (var row in preview.Rows)
        {
            Text($"Row {row.Id} | {row.SourceLocation} | {Time(row.OccurredAt)} | {row.SignedAmount:N2} {row.Currency} | balance {row.RunningBalance:N2}");
            Text($"{row.TransactionNumber ?? row.Reference} | {row.CustomerIdentifier} | {row.Narration}");
            if (preview.Import.Status != StatementImportStatus.Committed)
                Action($"Correct row {row.Id}", () => CorrectRowAsync(account, preview.Import, row));
            if (row.RequiresIdentityDecision)
            {
                Text($"Possible prior occurrences: {string.Join(", ", row.PossibleExistingTransactionIds)}");
                var existing = draft.Form.Resolutions.SingleOrDefault(r => r.RowId == row.Id);
                var choice = existing?.Kind ?? StatementRowResolutionKind.NewOccurrence;
                var transactionId = existing?.ExistingTransactionId;
                var reason = existing?.Reason ?? string.Empty;
                SelectEnum($"Row {row.Id}: overlap decision", choice, value => choice = value);
                Field(draft, $"row-{row.Id}-existing", "Prior occurrence ID (blank for a genuinely new occurrence)",
                    transactionId?.ToString() ?? "", value => transactionId = OptionalId(value), true);
                Field(draft, $"row-{row.Id}-reason", "Reason for this identity decision", reason, value => reason = value);
                Action($"Save decision for row {row.Id}", async () =>
                {
                    await ReadFormAsync();
                    if (reason.Trim().Length < 10) throw new BankingRuleException("Explain the overlap decision with at least 10 characters.");
                    draft.Form.Resolutions.RemoveAll(r => r.RowId == row.Id);
                    draft.Form.Resolutions.Add(new(row.Id, choice, transactionId, reason));
                    await SaveDraftAsync();
                    Text($"Decision retained for row {row.Id}.");
                });
            }
        }
        if (preview.Import.Status == StatementImportStatus.ReadyForReview)
            Action("Commit reviewed statement", async () =>
            {
                await ReadFormAsync();
                await Api.PostAsync<CommitStatementForm, BankStatementImportDto>($"admin/imports/{id}/commit", draft.Form);
                Drafts.Remove(UserId, key);
                await PreviewAsync(account, id);
            }).IsEnabled = preview.Errors.Count == 0;
    }

    private async Task CorrectRowAsync(BankAccountDto account, BankStatementImportDto import, StatementRowDto row)
    {
        Reset($"Correct extracted row {row.Id}");
        Text("The original source is preserved; this appends a normalization revision.");
        var key = $"statement-row-{import.Id}-{row.Id}";
        var draft = await EditAsync(key, () => new CorrectStatementRowForm
        {
            ExpectedVersion = import.Version, TransactionNumber = row.TransactionNumber, Reference = row.Reference,
            CustomerIdentifier = row.CustomerIdentifier, Narration = row.Narration, Currency = row.Currency,
            SignedAmount = row.SignedAmount, RunningBalance = row.RunningBalance, OccurredAt = row.OccurredAt,
            PostedAt = row.PostedAt, ValueDate = row.ValueDate, HasPreciseTimestamp = row.HasPreciseTimestamp
        });
        var form = draft.Form;
        Field(draft, "number", "Bank transaction number", form.TransactionNumber ?? "", value => form.TransactionNumber = value);
        Field(draft, "reference", "Reference", form.Reference ?? "", value => form.Reference = value);
        Field(draft, "customer", "Customer identifier", form.CustomerIdentifier ?? "", value => form.CustomerIdentifier = value);
        Field(draft, "narration", "Narration", form.Narration, value => form.Narration = value);
        Field(draft, "currency", "Currency", form.Currency, value => form.Currency = value);
        Field(draft, "amount", "Signed bank amount", Number(form.SignedAmount), value => form.SignedAmount = Money(value), true);
        Field(draft, "balance", "Running balance", Number(form.RunningBalance), value => form.RunningBalance = Money(value), true);
        Field(draft, "occurred", "Occurrence (Uganda: yyyy-MM-dd HH:mm:ss)", Time(form.OccurredAt), value => form.OccurredAt = BusinessTimeText.Parse(value));
        Field(draft, "posted", "Posting time (blank if absent)", form.PostedAt.HasValue ? Time(form.PostedAt.Value) : "",
            value => form.PostedAt = string.IsNullOrWhiteSpace(value) ? null : BusinessTimeText.Parse(value));
        Field(draft, "value-date", "Value date (yyyy-MM-dd; blank if absent)", form.ValueDate?.ToString("yyyy-MM-dd") ?? "",
            value => form.ValueDate = string.IsNullOrWhiteSpace(value) ? null : Date(value));
        Toggle("The source supplies a precise time", form.HasPreciseTimestamp, value => form.HasPreciseTimestamp = value);
        Field(draft, "reason", "Correction reason", form.Reason, value => form.Reason = value);
        Original(import.EvidenceFileId);
        Action("Append corrected extraction", async () =>
        {
            await ReadFormAsync();
            await Api.PostAsync<CorrectStatementRowForm, BankStatementPreviewDto>($"admin/imports/{import.Id}/rows/{row.Id}", form);
            Drafts.Remove(UserId, key);
            await PreviewAsync(account, import.Id);
        });
        Action("Keep draft and return", () => PreviewAsync(account, import.Id));
    }

    public sealed class StatementUploadDraft
    {
        public long EvidenceFileId { get; set; }
    }
}
