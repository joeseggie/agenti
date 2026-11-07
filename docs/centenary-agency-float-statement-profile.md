# Centenary agency-float statement: superseded XLSX profile

> **Superseded:** Agenti no longer parses the Centenary XLSX with a bank-specific statement profile. The historical parser and profile-verification workflow have been removed. For future imports, prepare a universal Agenti CSV from the bank export and use the Admin web process in the [bank workflow enrollment guide](bank-workflow-enrollment-guide.md). Existing imports/evidence remain historical records and are not reprocessed through the new schema.

## XLSX-to-universal-CSV preparation

Use the Centenary **Account Transaction Report** as the source, then map its rows to the exact universal headers and order:

`TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance`

| Centenary source | Universal CSV | Preparation rule |
|---|---|---|
| Transaction Date | `TransactionDate` | Convert to `yyyy-MM-dd`. Keep the bank's transaction date. |
| No verified transaction time in the date-only sample | `TransactionTime` | Leave blank. Never set midnight, closing time, or a time inferred from row order/reference. |
| Transaction number, only if confirmed to be the actual bank transaction reference | `Reference` | Preserve as text, including leading zeroes. Do not assume S/N or a value embedded in Comments is a stable reference. Otherwise leave blank. |
| Description and Comments | `Description` | Combine the source narration without changing its meaning; retain enough context for Admin review. |
| D/C plus Amount | `Debit` and `Credit` | Put the positive amount in `Debit` when D, or `Credit` when C; leave the other field empty. A reversal/fee follows its actual bank direction, not the label you expect. |
| Cumulative Balance | `RunningBalance` | Use the balance after that transaction. Verify each row equals the previous balance less debit or plus credit. |
| Account Number, Date From/To, currency, Opening Balance, Total Credits/Debits | Not CSV transaction columns | Select the bound Bank account and inclusive date range in Agenti. Use the export to cross-check account, currency, period and totals. For the first empty period, enter the audited opening baseline in Agenti with its source/reason. Do not add these metadata as extra CSV columns. |
| Book Balance / Available Balance | Not `RunningBalance` | Do not substitute unless Finance confirms it is the same balance basis as the account binding. |

Use the [shipped CSV template](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v1.csv) and [column guide](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v1-guide.txt). Remove report title rows, subtotal/total rows, and other non-transaction lines; do not remove actual transaction rows. The CSV must have the exact seven headers, in order, and no additional columns.

Preserve the source workbook locally as finance evidence under the organization's approved handling rules. Upload only the prepared CSV to Agenti; statement uploads are private evidence. Do not upload source statements to public repositories, issue trackers, or unapproved conversion services.

## Important date-only limitation

The sample's transaction date does not establish intraday ordering. Agenti does not fabricate times. A resulting **daily control** can compare transaction-date movements and balances, but it does not prove whether a bank posting happened before or after a teller's session closed. Date-only statement data must not be described as exact session-cutoff certification. If the date range begins with an empty date, the first-ever import requires a bank-sourced opening baseline and audited reason even if later dates have transactions.

## Admin workflow

1. Bind the Centenary Bank wallet to the correct account identifier, UGX currency, `Africa/Kampala` timezone and Finance-confirmed balance basis in **Bank statements**.
2. Download the template and convert the report using the mapping above. Check the signed direction and every cumulative balance.
3. Select the account and inclusive date range. Add the opening baseline and reason only when Agenti requests the first-period trust anchor.
4. Upload the CSV, resolve all parser diagnostics, and inspect the preview before committing.
5. In **Bank reconciliation**, review every date, match only unique candidates, resolve ambiguity manually, classify bank-only entries with evidence, and leave unknowns/differences open.
6. Preserve corrected imports as new revisions. Supervisor flags and Admin responses remain in the audit history.

Unresolved daily control is a follow-up warning and does not by itself block the next session opening. Existing opening approvals, vault safeguards, and session operations remain in force. See the [business workflow](agenti-banking-business-workflow.md) for role, input, validation, outcome and exception details.
