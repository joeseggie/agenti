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
| Account Number, Date From/To, currency, Opening Balance, Total Credits/Debits | Not CSV transaction columns | Select the bound Bank account and one statement date in Agenti. Obtain a complete single-day export; cross-check its account, currency and totals. Empty and multi-day statements are rejected. Do not add these metadata as extra CSV columns. |
| Book Balance / Available Balance | Not `RunningBalance` | Do not substitute unless Finance confirms it is the same balance basis as the account binding. |

Use the [shipped CSV template](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v2.csv) and [column guide](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v2-guide.txt). Remove report title rows, subtotal/total rows, and other non-transaction lines; do not remove actual transaction rows. The CSV must have the exact seven headers, in order, and no additional columns.

Preserve the source workbook locally as finance evidence under the organization's approved handling rules. Upload only the prepared CSV to Agenti; statement uploads are private evidence. Do not upload source statements to public repositories, issue trackers, or unapproved conversion services.

## Important date-only limitation

The sample's transaction date does not establish intraday ordering. Agenti does not fabricate times. Daily closing reconciliation compares transaction-date movements and balances, but does not prove whether a posting preceded the teller's session cutoff. Empty/no-activity statements are unsupported in this release; no baseline or exception approval bypasses that limitation.

## Admin workflow

1. Bind the Centenary Bank wallet to the correct account identifier, UGX currency, `Africa/Kampala` timezone and Finance-confirmed balance basis in **Bank accounts**.
2. Download the template and convert the report using the mapping above. Check the signed direction and every cumulative balance.
3. On **Bank statements**, select the account and one date. Every TransactionDate must equal that date; confirm account identity and complete-day coverage.
4. Upload the CSV and resolve all diagnostics. Acceptance stores the file and rows atomically; there is no separate preview/commit stage.
5. Upload the other Bank wallets' statements, then open the closing approval detail and select **Reconcile closing** once for all Bank wallets.
6. Review the saved report and make a separate ordinary approval or authorized Admin exception decision. There is no matching, classification, flag workflow or rerun. Different replacement files are allowed only before use, with a retained prior revision and reason.

Unresolved daily control is a follow-up warning and does not by itself block the next session opening. Existing opening approvals, vault safeguards, and session operations remain in force. See the [business workflow](agenti-banking-business-workflow.md) for role, input, validation, outcome and exception details.
