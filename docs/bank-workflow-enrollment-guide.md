# Bank account setup and closing reconciliation

Use this guide for account setup and statement preparation. The complete approval
procedure is [Closing balance reconciliation: business process](closing-reconciliation-business-process.md).
These documents describe the replacement implementation, not a production deployment.

**Important:** only complete, non-empty, single-day universal CSV statements are
supported. Cash and Mobile Money need no statement. A Bank wallet with genuinely
no transactions cannot complete this release's reconciliation; do not invent a
transaction or use exception approval to bypass missing evidence.

If a bank's statements cannot be obtained, an Admin can switch **Statement
reconciliation** off for that Bank wallet type on **Wallet types** (`/wallettypes`).
Its wallets then need no statement (binding is still required) and appear as
"Not reconciled" in closing reports. Each change is audited; switch it back on
when statements become available.

## 1. Bind each Bank wallet

1. Sign in as Admin and open **Bank accounts** (`/bank-accounts`).
2. Expand **Register a bank account** and select the teller's active Bank wallet.
3. Enter the bank name, exact account identifier, statement timezone (normally
   `Africa/Kampala`), and confirmed balance basis: Ledger, Available, or Float.
   Currency comes from the wallet.
4. Select **Save account binding**. Repeat for every Bank wallet. One bank's
   statement cannot cover another wallet.
5. Use **Account configuration** for permitted settings changes. Financial
   history protects the account's identity, currency, timezone, and balance basis.
6. Only for accounts whose exports lack balances (such as ABSA), enable **Allow
   statements without running balances**. It is off by default. Changing it
   affects future uploads, not previously accepted evidence or reports.

There is no bank-specific parser selector, uploaded parser, or editable schema.
Use **Open statements** for the selected account. **Manage account** on the
statement page returns to that account's settings.

An agent has one Cash wallet and zero or more distinct Bank/Mobile Money wallet
types, with at most one wallet per catalog type. Cash and Mobile Money are not
eligible for bank-account binding.

## 2. Understand enrollment versus reconciliation

**Enable new-session workflow** and **Pause new enrollment** remain on Bank
accounts. Modern-session enrollment requires active accounts/wallets, matching
currency and balance basis, exactly one active Cash wallet, no active legacy
session, and the existing one-active-agent-per-branch gates.

Enrollment governs new operating sessions, not whether an already-submitted
Bank closing needs reconciliation. All captured Bank wallets must be covered,
including paused accounts and eligible legacy closings. Existing sessions are
not rewritten when enrollment changes.

Opening approval, funding, trading, customer follow-ups, bank runs, and wallet
adjustments retain their existing safeguards. Closing submission returns the
reported allocation once. Later reconciliation and approval never repeat that
settlement.

## 3. Prepare one day's CSV

Download the [blank template](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v2.csv)
and [column guide](../EastSeat.Agenti.Web/wwwroot/templates/agenti-bank-statement-v2-guide.txt).
Convert the bank export outside Agenti, using approved local tools. Never send
customer statements to public repositories or unapproved conversion services.

The exact header and order are:

```csv
TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance
```

| Column | Rule |
|---|---|
| TransactionDate | `yyyy-MM-dd`; every row must equal the single date selected in Agenti. |
| TransactionTime | Optional actual bank-provided `HH:mm:ss`. Leave blank when absent; never guess a time. |
| Reference | Optional bank reference as text; preserve leading zeroes. |
| Description | Required bank narration. |
| Debit / Credit | Exactly one positive amount per row, with two decimals; leave the opposite column blank. |
| RunningBalance | Balance after this row, with two decimals. Or leave every cell blank on an explicitly enabled account; mixed blank/populated balances are rejected. Keep the header. |

Save UTF-8 CSV. Preserve bank ledger order. Quote fields containing commas,
including thousands separators. Remove headings, subtotals and non-transaction
rows, but retain all genuine transactions for the selected day. Do not upload
XLSX, PDF, images, formulas, extra columns, or multi-day files.

For all-blank balances, the same wallet's approved opening count for the selected
date must exist before upload. Agenti uses that recorded Bank wallet amount,
not the total opening cash count or live balance, to compute running balances.
The source CSV stays unchanged and the opening-count reference is retained.
Fully supplied balances keep all existing checks, even on an opted-in account.

## 4. Upload on the separate statement page

1. Open **Bank statements** (`/bank-statements`).
2. Select the bound account and **Statement date**.
3. Select its complete CSV and confirm that it belongs to that account and covers
   the complete selected day. The CSV itself cannot prove either fact.
4. Select **Upload statement**. Correct any row/date/balance diagnostics and
   retry. Invalid files create no accepted statement or partial rows.
5. Repeat for every Bank wallet in the closing.

Accepted files and rows are stored immediately in the database with account,
date, schema identity, uploader/time and revision. There is no background import
processor, preview/commit stage, baseline wizard, date range, or sequential-gap
requirement.

An identical file safely returns the current revision if its approved opening
baseline (when used) is unchanged. Before reconciliation, a changed file or
changed baseline requires a replacement reason of 10-2,000 characters; older
evidence remains available. Once used in a recorded run, that account/day cannot be replaced.
Switching accounts/dates clears the unsubmitted file and confirmation.

## 5. Reconcile and separately approve

Open the approval notification or **Closing approvals**, then **Review closing**.
Admin selects **Reconcile closing** once all Bank statements are uploaded. A
missing binding, approved opening, or same-day statement blocks the entire run.

The saved report compares each Bank wallet independently:

```text
Approved opening + statement credits - statement debits = submitted closing
```

It also checks bank-supplied opening/ending anchors and running balances.
For all-blank files, the report labels balances **computed from approved opening**
and discloses that the bank supplied no independent balances. The same opening
count and wallet amount used at upload are required; a changed baseline blocks
reconciliation. Arithmetic agreement still permits ordinary approval after Success.
Every Bank wallet must succeed for overall Success; differences never net across
wallets. This is daily arithmetic, not customer-transaction matching or exact
session-cutoff certification.

After Success, Admin or Supervisor separately selects **Approve closing**.
After Failed, only Admin may use **Approve with reconciliation exception**, with
a 10-2,000 character reason and explicit acknowledgement. Failed remains Failed.
Other approval safeguards still apply. No recorded run can be repeated.

An outstanding reconciliation does not itself block the next business day's
opening. Operational closure, opening approval and vault funding still apply.

## Troubleshooting

| Problem | Action |
|---|---|
| Missing statement | Upload every required Bank account's complete file for the closing date, then retry. If the bank's statements are unobtainable, an Admin may waive statement reconciliation for that wallet type. |
| TransactionDate mismatch or multiple dates | Obtain a complete export for exactly the selected day. Do not silently discard rows. |
| Empty statement | Unsupported this release; escalate the no-activity limitation. Never create a fake row. |
| Bad header, amount, encoding, or balance chain | Correct the source conversion using the shipped guide. Nothing is partially accepted. |
| Failed report | Investigate; an Admin may make a separately audited exception decision. No rerun or silent replacement. |
| Cash/Mobile Money or pending-adjustment approval blocker | Resolve the existing independent safeguard; bank exception approval cannot override it. |
| Concurrent change | Refresh and retry the unrecorded command, or inspect the already-recorded result. |

Legacy reviews, matching/classification, flags/responses, cutoff certification
and reconciliation-issued correction requests are retired. Their records remain
in a read-only archive; already-approved closings keep their status. Pending
closings require fresh single-day statements through this process.
