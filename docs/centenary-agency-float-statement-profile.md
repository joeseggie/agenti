# Centenary agency-float XLSX statement profile

## Status and release gate

Agenti has a candidate parser for Centenary Bank's **Account Transaction Report** XLSX export. The profile key is `centenary-agency-float-xlsx`, version 1, and its balance basis is Ledger. It is intentionally registered with `IsVerified = false`. It is not a production-ready importer: the format has only one representative sample, its transaction timestamps are date-only, and finance has not signed off the mapping. Until additional evidence resolves these gates, the app may retain an uploaded source as private evidence but will not parse it or permit the bank workflow to be enabled.

The date-only limitation is a hard reconciliation blocker, not just a display detail. The reconciliation engine cannot establish exact bank balances at session opening/closing cutoffs on dates with date-only transactions. It also cannot prove the midnight coverage bridge for an active date. Do **not** make up times such as 00:00 or 23:59, and do not mark this profile verified to work around the blocker.

Before release, choose and verify one of these paths:

1. **Preferred:** ask Centenary for an export that includes transaction times and confirm what those times mean (occurrence vs posting). Add samples from that export and test them against finance-approved expected values.
2. **Engine change:** approve a scoped reconciliation change to anchor a date-only row to an exactly matched register leg on that business date and allow only unambiguous exact-midnight boundaries. Unmatched date-only rows must continue to block approval. This requires design review and tests across session cutoffs, overnight bridges, matching and reconciliation basis hashes.
3. **Post-verification corrections:** after either release path is implemented and the profile is verified, an authorized reviewer may use **Correct extraction** to add exact times supported by receipts or other original bank evidence. Corrections are audited; they are manual and must preserve bank ledger order. They are not an interim way to proceed before verification. Never infer times from reference patterns.

G1 also requires more redacted Centenary examples: an empty day, overlapping and multi-day periods, reversals and fees, and a mid-day export. Finance must confirm the Ledger basis, account identity and row mapping. Keep original statements and unredacted data outside the public repository.

## Prepare each export

- Generate and download the original **Account Transaction Report** from Centenary for the desired account and period only after that period's end-of-period boundary has passed. Delaying import of a report generated earlier does not establish full-period coverage.
- Use one business day per file for the daily close process. Do not edit cells, rename sheets, remove rows, convert amounts, or open and re-save the workbook in Excel or another editor. Re-saving can change text cells or workbook parts and causes strict validation to reject the file.
- Keep the bank-provided `.xlsx` intact. Do not upload a screenshot, CSV conversion, or copied/pasted worksheet as this profile only accepts the original XLSX format.
- A statement whose **Date To** is day D covers through midnight at the start of D+1 in the account time zone. Generate and download it only after that boundary; a report generated during day D may omit later transactions even if imported after midnight.
- Confirm with finance that the requested bank statement is complete and its opening, closing and movement totals agree with the bank portal. A ready-to-review preview does not certify the closing; the profile must be verified and the transaction-time limitation resolved first.

## Profile mapping

The candidate parser expects one visible worksheet, with the bank's transaction table beginning at the row containing the 12 expected headings. It reads the account and period metadata from the labeled cells above that table.

| Bank export field | Agenti field / rule |
|---|---|
| Account Number | Account identifier digits and 3-letter currency token. The digits must match the configured bank account exactly; leading zeros matter. |
| Date From / Date To | Inclusive business dates in `dd/MM/yyyy`. They become a local-midnight interval `[Date From 00:00, (Date To + 1) 00:00)` in `Africa/Kampala`. |
| Opening Balance | Statement opening balance. |
| Transaction Date | `OccurredAt` at local midnight; `HasPreciseTimestamp = false`. |
| Value Date | Optional `ValueDate`. It is not substituted for transaction time. |
| Debit / Credit (D/C) and Amount | D becomes a negative ledger movement; C becomes a positive ledger movement. Amounts must be textual values with two decimal places. |
| Cumulative Balance | `RunningBalance` after each row. Agenti checks that opening plus each signed movement chains to the row balance. |
| Transaction / S/N | Candidate transaction number. Neither is treated as a proven unique, stable identity yet. |
| Description / Comments | Combined as narration; original row text is retained for review. A small set of observed comment shapes can be extracted as a candidate reference, but this mapping still needs bank/finance confirmation. |
| Total Credits / Total Debits | Checked against the sum of parsed credit/debit rows. |
| Book Balance / Available Balance | Not used as the closing balance because their exact as-at semantics have not been established. Closing is the last cumulative balance, or opening for an empty day. |

Unexpected or missing headings, formula/error cells, numeric amount cells, unsupported external workbook links, totals mismatch, and non-empty content after the table are rejected rather than guessed. The parser applies bounded package, XML-part, cell and row processing; it never evaluates formulas.

## Configure the account in Agenti

After the candidate profile is installed, an Admin can prepare (but not activate) the binding:

1. Open **Bank Reconciliation** > **Statements and Accounts**.
2. In **Register a bank account**, select the teller's existing **Bank Account** wallet for the Centenary float. The wallet currency must be UGX.
3. Enter **Bank name** as `Centenary Bank`.
4. Enter **Account identifier shown on statement** as the account-number digits printed by Centenary, without the trailing currency token. Preserve leading zeros and do not include spaces or `UGX`.
5. Set **Statement time zone** to `Africa/Kampala`.
6. Set **Confirmed balance basis** to `Ledger`, subject to finance confirming that the export's cumulative balance represents the ledger balance required by the reconciliation.
7. Select **Centenary Bank agency float (XLSX)**. The app marks this candidate **(not verified)**; selecting it only prepares the binding. Save the account binding, but do not enable new-session enrollment.

The web profile picker is labeled **Statement profile**; Android calls it **Import profile**. After a later release sets `IsVerified = true`, re-open the account's **Account configuration** and select **Save configuration** again. The account's verified-profile flag is copied when the configuration is saved; merely deploying the verified profile does not refresh an existing account binding.

## Import after verification

Once G1 passes and the verified profile is released to the deployed clients:

1. Refresh the account configuration and save it again, confirming the bank, identifier, UGX wallet, Kampala time zone, Ledger basis and verified profile.
2. After the next-day coverage boundary has passed, upload the original XLSX using **Upload the bank's source file** on web, or **Upload original statement** on Android.
3. Review the parsed account, period, opening/closing balance, row count, credits/debits, row ordering and each running balance against the bank export and finance's control totals.
4. Resolve all overlaps and identity exceptions explicitly. Do not infer identity uniqueness from the visible S/N or Transaction fields until it has been checked across overlapping samples.
5. Only commit the statement and enable the workflow after the profile's timestamp treatment can establish session cutoffs and overnight coverage, and finance signs off the trial.

## Open verification questions

- Can Centenary provide a statement export with exact transaction/posting times?
- Does Finance confirm that **Cumulative Balance** and the statement **Opening Balance** are on the same ledger basis as the account configured in Agenti?
- Which exact reference do tellers capture from Cente receipts? Does that value match the export's `Transaction` field, an observed token from `Comments`, or neither? Verify with paired redacted receipts and statements before using a reference for automatic matching.
- Are `Transaction` or `S/N` unique and stable across separate and overlapping exports? Until proved, the profile intentionally returns no reliable row identity and relies on reviewed overlap decisions.

For general release gates and enrollment steps, see the [bank workflow enrollment guide](bank-workflow-enrollment-guide.md).
