# Agenti: daily banking and closing reconciliation

This guide covers customer activity, operational closing and the replacement
approval workflow. The authoritative reconciliation procedure is
[Closing balance reconciliation: business process](closing-reconciliation-business-process.md);
account setup and CSV preparation are in the
[enrollment guide](bank-workflow-enrollment-guide.md).
This is not a production-deployment confirmation.

The earlier companion diagrams describe the retired per-account review and
correction workflow and are not current operating instructions.

## Responsibilities and distinct milestones

| Role | Responsibility |
|---|---|
| Agent/teller | Opening and closing counts, customer capture, actual movement times, bank runs, adjustment requests and customer-case follow-ups on web or Android. |
| Admin | Account setup, single-day statement uploads, whole-closing reconciliation, ordinary approval and reasoned reconciliation-exception approval. |
| Supervisor | Existing opening/adjustment approvals; read the branch's closing and report; ordinarily approve a successful closing. No upload, reconciliation execution or exception approval. |
| Application | Validate roles/branches, preserve immutable evidence and reports, settle once and issue in-app notifications. |

**Operational closing**, **reconciliation**, and **financial approval** are three
different events. Submission locks trading, freezes observations and returns the
reported allocation internally. Reconciliation checks all Bank wallets without
moving money. An authorized independent reviewer then explicitly approves.
Success never approves automatically; exception approval never changes Failed
to Success.

Agenti records bank/customer activity; it does not initiate external bank
transfers or automatically download statements. Vault allocations are internal
accounting movements, not bank-statement entries.

## Daily agent workflow

| Step | Action and result |
|---|---|
| Open | Sign in, inspect the current business-day session, record every allocated wallet once, and submit the opening for independent Admin/Supervisor approval. Trading remains unavailable until approval. |
| Count accurately | Use actual observed amounts/times and cash denominations where supported. Cash cannot be negative; monetary precision is two decimals. Explain discrepancies rather than inventing a balancing movement. |
| Capture customers | Record Deposit/Withdrawal, amount, bank/cash wallets, customer account or phone, actual occurrence time, bank outcome, reference or reference-unavailable indication, actual cash/bank effects, and required notes/evidence. |
| Follow up cases | Record bank progress and customer settlement separately, with linked movement, amount, actual time, explanation and evidence. A bank return is not a customer refund. |
| Record a bank run | After the physical cash-to-bank operation, record destination, amount, actual receipt time, reference, slip and notes. Requires approved opening, active trading and sufficient transferable cash; held customer cash is not freely available. |
| Review adjustments | Submit supported wallet adjustments for independent review. Pending adjustments can appear in the provisional tally but block final approval. No self-review or fictitious adjustments to force agreement. |
| Close | Enter all closing wallet observations and explain any differences from recorded movements. Submit the closing for approval. Trading ends, allocations are returned once and wallet caches are zeroed. External bank accounts are not emptied. |
| Await approval | Active branch Admins and Supervisors receive a persistent notification linking to the closing. The agent sees ordinary count/approval status, not reconciliation controls. |

Customer entries affect the expected position but do not themselves directly
overwrite stored wallet balances. Every capture command has a client-generated
retry identity; unchanged retries retrieve the same result.

Customer account/phone identifiers remain strings, including leading zeroes.
Non-completed operations or missing actual cash/bank legs require 10-2,000
character notes. Initial private JPEG/PNG/PDF receipts are optional; monetary or
bank follow-ups require evidence. The default evidence limit is 5 MB.

## Admin and Supervisor closing workflow

1. Open the approval notification or `/cashcount-approvals`, then **Review closing**.
2. Admin opens the separate `/bank-statements` page and uploads a complete
   universal CSV for **each Bank wallet**, for exactly the closing date.
   Cash and Mobile Money require no statements, nor do Bank wallet types whose
   statement reconciliation an Admin has waived (reported as "Not reconciled").
3. Admin returns to the closing and selects **Reconcile closing**. Missing
   approved opening, binding or statement blocks the whole operation before any
   report is saved.
4. Review the saved per-wallet report. It checks approved opening plus credits
   minus debits against submitted closing and the statement's opening/ending
   anchors. Equal and opposite bank differences do not cancel.
5. After overall Success, Admin/Supervisor separately selects **Approve closing**.
   Independent-approval, non-bank, discrepancy, adjustment and vault checks remain.
6. After Failed, only Admin may select **Approve with reconciliation exception**,
   explicitly acknowledge failures and provide a 10-2,000 character reason.
   Approval is recorded separately; the immutable report remains Failed.

The report includes wallet/account identity, statement revision/reference,
opening, debits, credits, expected/submitted ending, difference, outcome and
explanation. Reviewers can download the retained original CSV.

**Limits:** multi-day and empty statements are rejected. No-activity days are
unsupported; missing evidence cannot be overridden. There is no transaction
matching, classification, baseline wizard, per-bank approval, historical
correction grant, cutoff certification or rerun. Actual supplied times are
retained; date-only entries never acquire fabricated times.

An accepted statement can be replaced before use with an audited reason; previous
revisions remain. After a recorded run, inputs and results are locked. Failed
preflight or rolled-back technical errors may be retried because no report exists.

## Failed/pending customer operations remain separate

| Action | Meaning and safeguard |
|---|---|
| Record Failed/Pending or a missing leg | Preserve the original cash/bank effects and open a linked customer case. Held cash, bank exposure and customer obligations are separate facts. |
| Support update | Record progress/ticket number; a support-only update has zero monetary amount. |
| Bank reversal/posting/resolution | Append the observed bank outcome with evidence and actual time. It does not automatically refund or complete the customer. |
| Customer refund/payment/retry | Record the actual customer settlement in a permitted active session, linked to the earlier movement. Do not receive held cash twice or refund more than available verified funds. |
| Later investigation | Compare available evidence and document the customer-case outcome using its supported follow-ups. A successful closing report does not close a customer case or establish transaction legitimacy. |

For example, a failed UGX 50,000 deposit may leave cash `+50,000` and bank float
`-50,000`. A verified bank return adds bank float `+50,000` without refunding the
customer. A later refund records cash `-50,000`; an alternative retry with the
same held cash records no second cash receipt. Never refund and retry the same
funds twice.

Later cash settlements belong to their actual active session. Supported bank
observations remain linked to the original case without reopening old trading.
Reconciliation-issued historical corrections/resubmission are retired; frozen
counts and immutable customer history must not be rewritten.

## Next business day and worked example

An outstanding daily reconciliation or financial approval does not itself block
the next day's opening. A prior operationally unclosed session still does.
Normal independent opening approval, funding and session safeguards remain.

With opening cash `100,000` and bank `1,000,000`, a customer deposit of `500,000`
gives expected cash `600,000` and bank `500,000`. A cash-to-bank run of `400,000`
gives closing cash `200,000` and bank `900,000`. Submission returns the reported
`1,100,000` internally once. The bank check is:

```text
1,000,000 opening + 400,000 statement credits - 500,000 statement debits
= 900,000 submitted bank closing
```

If a genuine overnight bank credit of `2,000` changes the next opening, the modern
session process records a separate opening-bank variance/bridge rather than
rewriting yesterday's closing or inventing a vault top-up. This retained record
is not an exact-cutoff reconciliation certificate.

## History, notifications and platform boundary

Old reconciliation-only tables move to `reconciliation_archive`, retaining
evidence and cross-schema references with database write protection.
Already-approved closings retain their status. Pending closings use fresh
single-day uploads; old certifications are not converted into new successes.
Pre-cutover pending legacy closings receive only their missing first settlement,
never a replay of an existing settlement.

Notifications are persistent in-app messages, not new email/SMS/push channels.
On an interrupted request, refresh status before retrying. Android retains its
normal banking/count operations and protected retry drafts, but gains no
reconciliation pages or API. MCP remains read-only and gains no reconciliation
workflow.

## Implementation references

| Area | Source |
|---|---|
| Statement validation and revisions | `EastSeat.Agenti.Web\Features\BankStatements\DailyBankStatementService.cs` |
| Whole-closing report and separate approval | `EastSeat.Agenti.Web\Features\ClosingReconciliation\ClosingReconciliationService.cs` |
| Reviewer workspace | `EastSeat.Agenti.Web\Components\Pages\ClosingApproval.razor` |
| Session operations and exactly-once settlement | `EastSeat.Agenti.Web\Features\CashCounts\BankSessionWorkflow.cs`; `EastSeat.Agenti.Web\Shared\Services\SessionSettlementService.cs` |
| Customer cases | `EastSeat.Agenti.Web\Features\CustomerTransactions\CustomerTransactionService.FollowUps.cs` |
| Archive and replacement schema | `EastSeat.Agenti.Web\Data\Migrations\20261116191920_ReplaceClosingReconciliation.cs` |
