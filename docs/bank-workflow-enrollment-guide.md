# Agenti bank workflow enrollment guide

## Purpose and limits

This guide covers account binding and enabling the modern session workflow, then the Web Admin process for daily bank control. The statement intake contract is the release-shipped universal CSV schema (`agenti-bank-statement-v1`); there is no bank-specific profile picker and no executable parser uploaded by an Admin.

The teller can capture transactions and complete session counts in web or Android. Bank statement upload, matching, classification, flags, and daily review are Web workflows. Android statement-import/reconciliation screens and their admin API routes are removed. A CSV date-only row supports a transaction-date daily control only; it cannot prove the bank balance at an intraday session cutoff. The daily-control result does not replace existing opening approval, vault safeguards, or a separately eligible closing certification.

No production database was accessed. Enabling or deploying this workflow remains subject to your normal reviewed release process.

## Roles

| Actor | Responsibility |
|---|---|
| Finance / account owner | Confirms bank-account identity, currency, timezone, balance basis, and source for any opening baseline. |
| Admin | Binds accounts, uploads and commits CSVs, reviews/matches/classifies rows, records corrections, responds to flags, and manages workflow enrollment. |
| Teller / Agent | Uses the existing wallet, captures customer activity, submits opening/closing observations, and continues next-day operations. |
| Supervisor | Performs existing opening/adjustment approvals, reviews branch daily controls, and can raise an audited flag for Admin response. |
| Application | Checks CSV schema, range coverage, row and balance arithmetic, access, revisions, and audit history. It never fabricates statement times. |

## Enrollment and daily process at a glance

| Step | Actor | Input/action | Validation/output |
|---|---|---|---|
| E0 | Finance + Admin | Confirm the branch, agent, Bank wallet, account identity, currency, timezone and balance basis. | The account owner confirms what the statement balance represents. |
| E1 | Admin | In `/bank-statements`, register the existing Bank wallet and account details. | One active bank-account binding per Bank wallet; no money moves. |
| E2 | Admin | Download the CSV template and guide; convert the bank export outside Agenti. | Exact schema and data rules below; source XLSX/PDF is not uploaded as a statement. |
| E3 | Admin + Finance | For a first-ever empty date period, provide the opening baseline and its source/reason. | Baseline, evidence, actor, time and reason are audited; later periods use the prior committed closing balance. |
| E4 | Admin | Finish active legacy sessions; enable new-session workflow if needed. | Existing session/vault safeguards remain. Enrollment does not rewrite legacy history. |
| E5 | Teller + opening approver | Operate the day: approve opening, capture customer activity, submit closing. | Closing locks trading and performs only the existing operational settlement once. |
| E6 | Admin | Upload CSV, inspect diagnostics/preview, commit rows and open `/bank-reconciliation`. | Durable import/revision, private CSV evidence and a daily status per covered date. |
| E7 | Admin | Match unique entries, manually resolve ambiguous rows, classify evidenced bank-only activity. | Unknown rows and unexplained differences keep the day open. |
| E8 | Supervisor | Inspect the control and submit a reasoned flag if needed. | Immutable flag; the Admin must respond with a note or void reason. |
| E9 | Admin | Correct by a retained revision when source facts change; re-review affected days. | Prior evidence/decisions remain auditable; no old wallet/vault movement is replayed. |
| E10 | Teller + Admin | Start the next day and finish any outstanding prior daily review. | Unresolved bank control warns but does not by itself block the next session opening. |

The detailed teller/customer journey is in [Agenti: customer banking and bank-statement reconciliation](agenti-banking-business-workflow.md).

## E0-E1: confirm and bind the account

1. Sign in as an Admin and open **Bank statements** (`/bank-statements`).
2. Expand **Register a bank account**.
3. Select the teller's active **Bank wallet**. Cash and Mobile Money wallets are not eligible.
4. Enter the bank name, account identifier exactly as printed, statement timezone (normally `Africa/Kampala`), and the confirmed balance basis (Ledger, Available or Float).
5. Select **Save account binding**. Currency comes from the wallet.
6. Confirm the selected account and wallet in the page. A bank wallet can have only one account binding. After financial history exists, account identity/timezone/basis are protected against casual edits.

The universal CSV parser ships with Agenti. There is no profile upload, per-bank code registration, or Admin schema edit. New statement uploads must use the CSV contract described below. Old profile metadata on historical imports remains historical only.

## E2: prepare a CSV

Download:

- [Blank universal CSV template](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v1.csv)
- [Column guide](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v1-guide.txt)

Convert the bank export outside Agenti and upload the resulting `.csv`. Do not upload the original XLSX/PDF, screenshots, macros, or formulas as a statement. Do not send customer statements to outside conversion services.

Required exact header and order:

```csv
TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance
```

| Column | Format / rule |
|---|---|
| `TransactionDate` | Required `yyyy-MM-dd`; this is the bank transaction date used for daily partitioning. |
| `TransactionTime` | Optional real bank-provided `HH:mm:ss`. Leave blank if the source has date only; never add a guessed midnight/closing time. |
| `Reference` | Bank transaction reference as text; preserve leading zeroes. Blank when the bank did not provide one. |
| `Description` | Required narration. |
| `Debit` / `Credit` | Exactly one positive amount per transaction row, two decimals. The other field is blank. Quoted thousands separators are supported. |
| `RunningBalance` | Required balance after the row; debit decreases and credit increases the balance. May be negative if the account legitimately permits it. |

Do not reorder, rename, add, or remove columns. Agenti accumulates row/column diagnostics and rejects the whole invalid import; it does not commit partial entries.

## E3: select dates and baseline

1. Select the Bank account and enter the period's inclusive start and end dates.
2. Upload one complete CSV for that range. Multi-day files are partitioned into daily controls.
3. For the account's first imported period, if the first selected date has no transactions, enter the opening balance independently obtained from the bank and a source/reason. This baseline is a trust anchor and is audited with the Admin identity/time and retained statement evidence.
4. If the first date contains rows, Agenti derives the range opening from the first running balance and movement. A supplied baseline must agree with that derived opening.
5. Later imports must start on the day after the last committed range and must carry the previous committed closing balance. Do not re-enter a new baseline.
6. Empty days carry the prior closing balance and display zero movements. A date with no statement coverage is not considered an empty day.

The app rejects future ranges, any selected date with an open session, missing sequential dates, broken running-balance chains, and conflicting account/currency/balance-basis facts. An identical file replay is idempotent. Conflicting overlaps require explicit review; separate identical-looking bank transactions are not automatically deduplicated.

## E4-E5: enable operations and close the day

If the branch uses the modern workflow, satisfy the existing enrollment gates on the account page: active account/wallets, confirmed matching balance basis/currency, required wallet configuration, and no active legacy session. The initial workflow supports one active agent and one enrolled account per branch. These are session-enrollment limits, not a reason to merge multiple bank wallets into one statement account.

The teller uses the normal opening/count process. Admin or Supervisor opening approval and vault funding safeguards are unchanged. During the day, the teller records customer movements, bank runs and cases in their normal screens. At closing, the teller submits actual wallet observations; Agenti freezes the snapshot, ends trading and performs the existing operational settlement once. A later bank review never deposits or zeros wallets a second time.

## E6-E7: commit and reconcile

1. Wait until the bank's statement is complete for the selected period; do not treat an early partial export as full-day coverage.
2. On **Bank statements**, inspect every parser error and each preview row before **Commit reviewed bank entries**.
3. On **Bank reconciliation**, select the account/date range and start a review.
4. Review each date's opening/closing balance, statement movement total, register movement total, classified bank-only movements, difference and unaccounted row count.
5. Run unique exact matching. Auto-match is allowed only for a unique one-to-one amount/direction/currency/date candidate (and uses the reference when both sides provide it). Manually resolve ambiguous candidates with a reason.
6. Classify fees, interest, transfers, reversals or other bank-only movements only with evidence and explanation. `Unknown` is an explicit unresolved classification and keeps the date open.
7. Leave all unexplained differences visible. Do not invent a customer transaction or a bank reference to force agreement.

The **Daily control** result is distinct from session certification. Even a daily status of `Reconciled` does not prove whether a date-only bank posting preceded a session's close. Exact cutoff certification remains unavailable unless the statement provides usable timestamps at the recorded observation points.

## E8-E10: flags, revisions, and next opening

- **Supervisor flag:** A Supervisor opens the branch review, enters a 10-2,000 character concern and raises the flag. The flag is immutable and changes the review to `NeedsReview`.
- **Admin response:** An Admin records a reasoned response note or marks the flag void with a reason. The response is linked to the flag; neither action deletes the original concern.
- **Correction/revision:** Keep the original file and accepted rows. Upload/reprocess a corrected CSV as a new revision, provide evidence/reason for normalization corrections, then re-review affected dates and rematch. A Supervisor flag can be voided with an Admin reason; a committed statement import is not silently voided. Never overwrite source facts or report a stale result as current.
- **Next opening:** A missing/unresolved daily bank control is a warning and follow-up item, not an opening blocker. Existing opening approval, operational-session, and vault funding checks still apply. Do not reopen yesterday's session.

## Troubleshooting

| Symptom | Check/action |
|---|---|
| Header/schema error | Use the shipped template exactly, including order and spelling. |
| Date/time, amount, or running-balance error | Preserve the bank's date/time, put positive amounts in only one of Debit/Credit, and verify every row's balance chain. |
| First empty period requires baseline | Obtain the opening balance from the bank's evidence, provide source/reason, and retain the evidence. Never infer it from a later day's row alone. |
| Range starts before/after expected date | Import missing dates first; later ranges must be sequential. |
| Selected date has an open session | Complete that session's closing before committing the statement range. |
| Unmatched or ambiguous row | Check the register/reference/date, then manually match or classify with evidence; do not accept a guessed match. |
| `Unknown`, `NeedsReview`, or `NeedsCorrection` | Investigate the row/flag/difference and record the appropriate Admin decision. |
| Exact closing approval remains blocked | Date-only evidence cannot certify intraday cutoff balances. Keep the daily control result separate; do not add a fabricated timestamp. |
| A later source contradicts a committed result | Create and review a new source revision. Confirm affected matches/results are re-evaluated. |

## Go-live checklist

- [ ] Admin confirmed the account identifier, branch, wallet, currency, timezone and balance basis.
- [ ] Admin downloaded the current template/guide and successfully validated a representative prepared CSV.
- [ ] First empty period baseline (if needed) has a documented source/reason and audit record.
- [ ] Sequential coverage, empty days, overlaps, open-session rejection, unknown classification and date-only cutoff protection are understood.
- [ ] Admin and Supervisor know their separate permissions and flag-response process.
- [ ] Teller can continue the normal opening/session/closing flow while daily statement review is outstanding.
- [ ] Migration, tests, and reviewed deployment are complete before production activation.
- [ ] Finance signs off the bank export conversion and balance basis. Synthetic/unit tests do not verify a bank's real layout.

## Implementation references

- Schema: `EastSeat.Agenti.Web/Features/BankStatements/Schema/agenti-bank-statement-v1.json`
- Parser/template: `EastSeat.Agenti.Web/Features/BankStatements/UniversalBankCsvStatementProfile.cs`, `EastSeat.Agenti.Web/wwwroot/templates/`
- Import, baseline audit, and range validation: `EastSeat.Agenti.Web/Features/BankStatements/BankStatementService.cs`
- Daily control and date-only cutoff guard: `EastSeat.Agenti.Web/Features/BankReconciliation/BankReconciliationService.Evaluation.cs`
- Matching, classifications and flags: `EastSeat.Agenti.Web/Features/BankReconciliation/`
- Web workspaces: `EastSeat.Agenti.Web/Components/Pages/BankStatements.razor`, `BankReconciliation.razor`
