# Agenti: customer banking and bank-statement reconciliation

## Purpose and scope

This workflow describes customer-transaction capture, session operations, and the Admin-operated universal CSV daily bank control. Teller capture and session work remain available on web and Android; statement import and reconciliation are Web Admin workflows only.

**Scope assumption:** "the new feature" means the implemented banking/reconciliation feature. The previously discussed production MCP service is a proposed, separate read-only extension; its optional user journey is in section 12.

This is a description of the implementation and its intended operating procedure after release, not confirmation that it is deployed or enabled in production. No production data was accessed to prepare it.

**Statement boundary:** Agenti accepts one release-shipped universal CSV contract, not bank-specific executable profiles. Admin downloads the template, converts the bank export outside Agenti, then uploads CSV. The date-level result is a daily movement control, not exact session-cutoff certification. A Centenary export must be mapped to the template before upload.

Companion files:

- [Editable process diagram](agenti-banking-business-workflow.excalidraw).
- [Scalable browser-readable diagram](agenti-banking-business-workflow.svg).
- [PNG preview](agenti-banking-business-workflow.png).

The diagram uses the step IDs below. Tables provide the detailed inputs and outcomes.

## 1. Business objective and operating rules

The agent records what actually happened to customer cash and bank float during the day. At closing, the agent submits the observed wallet balances and ends trading. An independent administrator compares those records against independently obtained bank evidence and explicitly certifies each closing.

**Three different milestones must not be confused:**

| Milestone | Meaning | What it does not mean |
|---|---|---|
| Operational closing submitted | The agent's trading is locked, observations are frozen, and reported float is returned internally once. | The bank statement is complete, the customer cases are resolved, or an admin has approved the closing. |
| Closing financially approved | An independent admin has certified the frozen closing against the current reviewed evidence. | There are necessarily no outstanding documented bank exceptions. |
| Reconciled / WithExceptions | The review establishes agreement, or agreement with explicitly verified outstanding exceptions. | A calculated eligible result replaces the admin's explicit approval action. |

Use Uganda business dates and actual observation/transaction times. Daily reconciliation after closing is the recommended procedure; daily, weekly and monthly review ranges are supported. A range still produces a decision for each session, not one blanket approval.

Agenti records bank/customer activity; it does not initiate a bank transfer, download statements automatically, or physically move money. Opening/closing vault allocations are internal accounting movements, not bank-statement transactions.

## 2. People, responsibilities and screens

Here, **agent/teller means the human banking operator**, not an AI agent.

| Participant | Responsibility | Main workspace |
|---|---|---|
| Agent/teller | Opening and closing observations; customer receipts; actual cash/bank effects; bank runs; adjustment requests; customer-case follow-ups; authorized historical corrections. | Web: Cash Count, Customer Transactions, Bank Runs. Android: Cash Count and Customer banking. |
| Supervisor | Independently approve/reject opening counts and wallet adjustments; review daily bank controls and raise a reasoned flag for Admin response. | Count/adjustment approvals; Web bank reconciliation read/flag view; Android Cash Count. |
| Administrator | Account setup, CSV statement intake, reconciliation, matching/classification, exception handling, import revisions, Supervisor responses, and any separate eligible closing certification. | Web: Statements (`/bank-statements`) and Reconciliation (`/bank-reconciliation`). No Android statement-import workflow. |
| Application | Validate permissions and state, calculate expected positions, preserve evidence/history, process imports, enforce review gates and issue in-app notifications. | Automatic responses to user actions and the server-side import processor. |
| Bank/customer | Supply the actual transaction result, receipt, statement, reversal confirmation or customer settlement evidence. | Outside Agenti; the user records/verifies this evidence in Agenti. |
| Implementation/release team | Verify real-bank formats, release compatible backend/mobile versions and satisfy deployment gates. | Release prerequisite, not a teller action. |

An approver cannot approve their own modern opening, review their own modern wallet adjustment, or certify their own closing. If an Admin is also the teller, another authorized person must perform the relevant approval. Supervisors can inspect and flag a branch's review; only Admins can upload/commit statements, match/classify rows, clear flags, or approve closings.

## 3. One-time onboarding and activation

| ID / actor | User action | Expected inputs | Output / outcome | If requirements are not met |
|---|---|---|---|---|
| **A1 - Admin** | Bind the existing Bank wallet to the bank account. | Wallet; bank name; account identifier; timezone; confirmed balance basis. The wallet supplies currency. | One bank account binding per Bank wallet. The universal CSV schema is shipped by Agenti; there is no profile picker. | Inactive/wrong-branch wallets and duplicate bindings are rejected. Identity/timezone/basis cannot be casually rewritten after financial activity. |
| **A2 - Admin** | Enable new-session workflow if this branch uses the modern session workflow. | Active account and wallets; matching currency/balance basis; finished legacy sessions; initial branch enrollment gates. | New eligible sessions use the modern workflow; existing sessions remain unchanged. | Session/vault safeguards remain. Pausing enrollment never rewrites sessions already created. |
| **A3 - Admin/Finance** | Verify CSV preparation and first-period baseline if required. | Shipped schema version/hash, bank export converted outside Agenti, documented account basis, and source/reason for a first empty period's opening balance. | A committed import retains private source evidence, schema hash, actor/time, and any baseline audit. | Do not upload source XLSX/PDF as a statement or fabricate times. |

For the detailed enrollment procedure, prerequisites, enable gates and troubleshooting, see [Bank workflow enrollment guide](bank-workflow-enrollment-guide.md).

## 4. Daily agent workflow

| ID / actor | User action | Expected inputs | Output / outcome | Decision / next step |
|---|---|---|---|---|
| **D1 - Agent** | Sign in and open Cash Count. Review session status and notices. | Existing app credentials; active user, agent assignment and branch. Session date/identity come from the server. | Current business-day state: ready to open, awaiting opening approval, session open, earlier session still unclosed, or session ended. | If an earlier session is not operationally closed, submit its closing first. An earlier closing merely awaiting financial approval is not the same blocker. |
| **D2 - Agent** | Record the opening, save a draft if needed, then submit for approval. | Every active wallet exactly once; actual amount; actual balance-observation time for each wallet; denominations where supported or manual totals; explanation where useful. | Saved count identity and an opening in `PendingApproval`; in-app approval work for Admin/Supervisor. Trading remains unavailable until approval. | Correct validation errors and resubmit. Cash cannot be negative; times cannot be future; amounts use at most two decimal places. New openings use today's Uganda business date. |
| **D3 - Supervisor/Admin** | Review the opening and approve, or reject with instructions. | Opening wallet observations; available internal vault float; reviewer identity. Rejection requires a reason of 10-2,000 characters. | Approval creates the opening snapshot and internal allocation once, sets wallet allocations and notifies the agent. Any bank change since the preceding operational closing is separately recorded for later review. | Self-approval or insufficient internal funding fails. Rejection returns the opening to the agent for correction/resubmission; no trading is authorized. |
| **D4 - Agent** | Conduct the customer operation outside Agenti, then open Customer Transactions / Customer banking and **Save transaction**. | Bank account/wallet; cash wallet; Deposit or Withdrawal; amount; customer account/phone; actual occurrence time; bank outcome; bank reference or explicit reference-unavailable indication; actual cash/bank movement flags; notes/evidence as applicable. | Attributable customer entry and movement history; updated expected wallet tally. Customer entries do not directly update stored wallet balances. | Repeat for each operation. A pending/failed outcome or missing actual cash/bank leg opens a linked customer case: follow D5/X1-X5. |
| **D5 - Agent, when needed** | Open the customer transaction/case and record follow-ups. | Related movement, action, amount, actual time, supporting evidence and explanation; bank/support reference where applicable. | Separate records for bank progress and customer settlement, with remaining held cash, exposure and obligations visible. | A bank reversal does not automatically refund the customer. Continue the exception workflow in section 8. |
| **D6 - Agent, optional bank run** | Record a physical cash-to-bank run after performing it. | Destination wallet; positive amount; actual receipt time; receipt number, deposit-slip image and notes as available. Source cash wallet is identified by the application. | One bank-run record; affected wallet positions refreshed from the opening plus recorded movements, then transferred cash-to-bank. No new customer receipt is invented. | Requires an approved opening, active trading and sufficient transferable cash. Reserved customer cash is not freely available. Match the actual bank posting during review. |
| **D7 - Agent, then Supervisor/Admin** | Request a wallet adjustment and obtain its independent review. | Wallet; signed amount; supported reason; occurrence time; explanation; bank reference if applicable. Reviewer supplies approval or rejection reason as required. | Adjustment remains attributable and distinguishable from ordinary customer transactions; status becomes Approved or Rejected after review. | No self-review. Pending adjustments can be visible in the provisional tally but block final closing approval until reviewed. Do not invent an adjustment simply to hide an unmatched bank item. |
| **D8 - Agent** | Before ending the day, review the register and cases, then enter the closing observations. | All actual operations/follow-ups; physical cash count; observed balances of every allocated wallet; each observation's actual time; explanation if any wallet differs from its expected amount. | Draft closing with reported and expected wallet amounts and variances. It is not yet a submitted closing. | Check missing receipts and wrong timestamps before submission. An explanatory note permits a variance to be submitted for review; it does not make the variance reconciled. |
| **D9 - Agent** | Confirm **Submit and end trading** / submit the closing count. | Complete draft closing, valid observation cutoffs and required variance explanation. | Immutable closing snapshot; one provisional internal return of reported float; current allocations zeroed; trading locked; closing becomes `PendingApproval`; admins receive an in-app bank-review notification. | Proceed to R1-R6 for independent review. In the initial one-agent scope the branch/day session closes at this point. Duplicate submission does not return float a second time. |

### Input checklist for D4

| Field | Requirement / meaning |
|---|---|
| Bank account and cash wallet | Must belong to the teller and be in the session's approved allocation, with matching currency. |
| Kind and amount | Deposit or Withdrawal; positive amount, at most two decimal places. |
| Customer account / phone | Required, preserved as a string, maximum 256 characters. Do not drop leading zeros. |
| Bank transaction reference | Enter the actual reference, or explicitly mark that the bank did not supply one. Do not manufacture a reference. |
| Actual occurrence time | Uganda-time input representing the real event, not merely when the user types it. It must fit the permitted session/correction window and cannot be future. |
| Outcome | Completed, Pending or Failed. |
| Actual cash exchanged | Whether customer cash actually changed hands. Do not assume it follows the outcome label. |
| Actual bank movement | Whether bank float actually moved. A failed operation may still have debited float. |
| Notes | Required, 10-2,000 characters, for non-completed operations or missing cash/bank movements. Optional for an otherwise completed operation; maximum 2,000 characters. |
| Initial receipt evidence | Supported private JPEG, PNG or PDF attachment; optional for initial capture. Evidence is required for monetary/bank follow-ups and historical movement corrections. Default evidence limit is 5 MB, configurable by deployment. |
| Request identity | Generated/retained by the client, not typed by the user. Repeating an unchanged command safely retrieves its existing result; changing its payload under the same identity is rejected. |

## 5. Next business day: independent of yesterday's approval

| ID / actor | User action | Inputs | Outputs / safeguards |
|---|---|---|---|
| **N1 - Agent, then Supervisor/Admin** | On the next business day, return to D2-D3 and request a new opening. | New observed wallet balances/times; the previous operational closing; normal funding and opening-review requirements. | A new approved allocation can proceed even while yesterday's bank reconciliation or approval is outstanding. A prior operationally unclosed session still blocks a new opening. |
| **N2 - Application / Admin later** | Record and subsequently investigate the change between bank closing and next opening. | Prior bank closing observation; new bank opening observation; later statement coverage for the intervening interval. | Separate opening-bank variance/bridge, preserving the old closing. Internal vault allocation is separated from the externally observed bank change. Its lack of completed bank review does not itself block the opening. |

This is a next-business-day process, not an unrestricted same-day reopening. Do not reopen an old session to record new trading.

## 6. Administrator's end-of-day reconciliation

Run this process after the teller's session is closed and the bank has published the complete daily export. It is an account-level, transaction-date control. It does **not** certify intraday cutoff balances: a row without a bank-provided time remains date-only, and Agenti never invents a time.

| Step / actor | Input | Action | Validation | Output / status | Next decision |
|---|---|---|---|---|---|
| **R1 - Admin** | Bank export, account/wallet binding, and period dates. | Download the blank Agenti CSV template and guide; convert the bank export outside Agenti. Upload only the CSV. | Exact ordered header `TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance`; dates `yyyy-MM-dd`; optional real time `HH:mm:ss`; each row has exactly one positive Debit/Credit amount; running balance chains. | Private CSV evidence and a durable import identity; schema version/hash captured. | Continue to R2 if accepted; otherwise correct the CSV outside Agenti and upload again. |
| **R2 - Admin** | Start/end date, selected Bank wallet/account, optional first-period baseline and its source/reason. | Select account and range, upload, then refresh the durable preview. | Non-future range; no open session within the range; no missing dates after the last committed range; first empty period requires a baseline and reason; later ranges carry forward the prior committed closing balance. | `Uploaded` → `Processing` → `ReadyForReview`, or explicit `Failed` diagnostics. No partial rows commit on parse failure. | Resolve every reported parser/account/balance issue before commit. |
| **R3 - Admin** | Valid multi-day preview, source rows, and any identity decisions. | Inspect each day's movements and balances, then **Commit reviewed bank entries**. | Complete date coverage, row sequence/balance chain, matching account/currency/basis, no conflicting overlap. An identical file can replay safely; two distinct identical-looking rows remain distinct unless evidence links them. | Accepted canonical bank occurrences and row provenance; no wallet/vault movement and no session certification. | Create/open an account review for the same dates. |
| **R4 - Admin** | Committed import plus teller register/customer movements and bank-run entries. | Open **Reconciliation**, choose account/date range, **Start review**, then **Match unique exact entries**. | Auto-match requires a unique one-to-one amount, direction, currency and transaction-date candidate; a bank reference is used when both sides provide one. Ambiguous candidates stay unmatched. | Per-day opening/closing balance, statement movement total, register movement total, classified bank-only amount, difference, unaccounted row count, and a distinct daily-control status. | Manually match ambiguous rows or investigate every difference. The daily result is not exact session-cutoff approval. |
| **R5 - Admin** | Selected bank row/register entry, supporting evidence, and a reason. | Manually match; classify verified Fee, Commission, Interest, Transfer, reversal or other bank-only movement; or leave it `Unknown`. | A match is one-to-one and amount/currency/direction-consistent. Bank-only classifications require evidence and a reason. | Decision and revision history are retained. `Unknown` or unclassified rows keep that date `NeedsReview`; unexplained difference keeps it `NeedsCorrection`. | Resolve or explicitly document the exception; never create a fake customer transaction to force a balance. |
| **R6 - Supervisor** | Current branch review and specific concern. | Inspect the daily result and raise a flag with a 10-2,000 character reason. | Supervisor role and branch scope checked by the service; stale review version is rejected. | Immutable flag in the review history; daily status becomes `NeedsReview`. | Admin must respond in the app. |
| **R7 - Admin** | Open Supervisor flag, current review version, and investigation outcome. | Record a reasoned response or explicitly void the flag with a reason. | Admin-only service check; response tied to the exact flag and cannot be duplicated. | Immutable response; the flag is closed while the original concern remains in history. | If facts require correction, continue to R8. |
| **R8 - Admin** | Contradictory/incorrect source evidence and reason. | Upload/reprocess a corrected CSV as a retained new revision, then review that revision. Responding to or voiding a Supervisor flag is a separate R7 decision; a committed statement import is not silently voided. | Preserve source evidence and the previous revision; revalidate coverage, balances, matches and current review. Do not silently overwrite imported rows. | Affected daily result is recalculated; prior decisions/history remain inspectable. | Re-match and resolve affected rows; do not treat a prior status as current. |
| **R9 - Agent/Admin** | Next opening request while yesterday's daily control is missing or unresolved. | Continue normal opening workflow; inspect visible warning and route the bank review issue to Admin. | Bank-control warning is not an opening authorization or cash/vault override. Existing opening approvals and funding safeguards remain enforced. | Next session is not blocked solely by an unresolved bank-statement review. | Complete the outstanding daily control without reopening the old session. |

### Daily-control exception outcomes

| Scenario | App response and required action |
|---|---|
| Invalid header order, extra/missing columns, malformed date/time, invalid debit/credit or broken balance chain | Show row/column diagnostics; do not commit any statement transactions. Correct the CSV using the template. |
| First-ever empty period | Enter the independently sourced opening balance and a reason/source. Actor, time, amount, evidence and reason are audited. |
| Empty date after a known opening | Carry forward the previous closing balance and show zero movements; do not invent a statement row. |
| Multi-day file | Split daily controls by `TransactionDate`; show each date separately, including zero-transaction dates. |
| Missing date or an open session on a selected date | Reject the range or show a coverage gap; close the session/catch up from the missing date before committing. |
| Same file replay or overlapping source | Identical file replay is idempotent. Conflicting overlap requires an explicit revision/identity decision; legitimate duplicate-looking rows are preserved. |
| Unique amount/direction/date candidate | Auto-match only if one bank occurrence maps to one register leg and vice versa. |
| Multiple candidates, missing bank reference, or different source facts | Leave unmatched for Admin selection; an explicit reason is saved. |
| Fee/interest/transfer/reversal or other bank-only movement | Classify with source evidence and reason. An `Unknown` choice is an explicit unresolved state and keeps the day open. |
| Supervisor flag | Admin records a response note or void reason. Both actions are auditable; the flag is never silently removed. |
| Corrected import | Keep both source/revision history, invalidate affected current results, then re-review. |

No action in R1-R9 fabricates transaction times, proves intraday posting order, changes wallet/vault balances, or bypasses opening dual approval.

## 7. Separate session-closing certification

The daily control in R1-R9 is not session approval. The review page retains a separate Admin closing-certification action for sessions when the bank source provides usable timestamps at the recorded opening and closing observations. This action is not part of the date-only CSV daily result.

Certification requires the latest frozen opening/closing snapshots, operational settlement, complete observation-interval coverage, current register and bank-source versions, accounted-for bank legs, reviewed wallet adjustments, and no blocking unexplained variance or correction. A date-only statement row makes the exact cutoff balance unavailable for that date; the certification action remains blocked rather than treating midnight as the transaction time. Historical approval never posts a second vault/wallet movement. Normal opening approval and vault safeguards remain unchanged.

## 8. Failed/pending operation and customer settlement

| ID / actor | Action and input | Output / outcome |
|---|---|---|
| **X1 - Agent** | At D4, select Failed/Pending or accurately record a missing actual cash/bank leg. Include amount, customer identity, reference status, time and explanation. | Original movement is retained, with a linked pending case. Actual cash held, bank exposure and customer settlement are separate facts. |
| **X2 - Agent** | Add a support update with notes and ticket number when available. | Audited case progress. A support-only update has zero monetary amount and does not change balances. |
| **X3 - Agent** | Verify what the bank actually did. Select the related movement and record reversal, observed posting, completion confirmation or supported no-posting resolution; provide amount, actual time, evidence and explanation. | Append-only bank/resolution event. Recording a reversal changes the bank side only; it does not release held cash or record a refund. |
| **X4 - Agent** | Record the customer's actual outcome in a permitted active session: retry using held cash, refund, missing cash receipt, or withdrawal cash payment. Supply linked movement, amount, evidence, actual time and explanation. | A separate customer settlement event and updated remaining obligation. A deposit retry must not record a second receipt of the same cash. Refunds cannot exceed cash held or verified available bank credit. |
| **X5 - Admin** | Once bank statement evidence is available, match the relevant return/posting and resolve the corresponding exception with a reason. | Bank reconciliation flag can clear after its required matching/exposure checks. Customer completion/refund remains separately evidenced; it is not inferred from clearing a bank flag. |

All monetary/bank follow-ups require evidence; a support-only update is the exception. Later cash payouts/refunds/retries belong to the actual active session where they occur. Bank observations can be recorded against the original case without reopening old trading; their actual timestamps govern review attribution.

### Illustrative failed deposit

| Event | Cash effect | Bank-float effect | Business outcome |
|---|---:|---:|---|
| Agent receives UGX 50,000; bank debits float but customer deposit fails | +50,000 | -50,000 | Customer cash remains held and bank case is open. |
| Bank returns the debit; agent verifies and attaches proof | 0 | +50,000 | Bank return recorded; customer still has not been refunded/completed. |
| Agent refunds in the actual active session | -50,000 | 0 | Customer refund recorded once. Admin later matches bank evidence and clears its exception. |
| Alternative to refund: successful retry with the same held cash | 0 | -50,000 | Customer deposit completed without receiving cash twice. Do not also refund the same funds. |

These are illustrative amounts, not production transactions.

## 9. Historical correction without reopening trading

| ID / actor | Input and action | Output / outcome |
|---|---|---|
| **C1 - Admin** | Choose the frozen closing and **Request historical correction**. Specify permitted movement IDs and/or permission to add missing historical transactions; provide instructions/reason. | A scoped correction grant and agent notification. The old session remains closed. |
| **C2 - Owning agent** | Open the correction request. Append the authorized replacement/void or missing receipt. Preserve the actual historical occurrence time; provide correct values, reason and evidence for movement corrections. | New linked audit history and an updated register revision. Original records remain; unauthorized movements/dates are rejected. |
| **C3 - Agent, then Admin** | Select **Resubmit corrections** after recording the requested changes. Admin refreshes, rematches/rechecks and repeats R6 if eligible. | Correction request closes for resubmission, admins are notified, and the current evidence is reassessed. Old settlement is not replayed. |

This correction route is for scoped customer-register history. It is **not** a general edit of the submitted wallet/count snapshot, nor permission to enter new trading in a past session. If the frozen observation itself is disputed, preserve it and escalate for investigation; do not present this screen as a way to rewrite it.

## 10. Normal worked example and overnight movement

Assume one cash wallet and one bank wallet, both in UGX, with no pending cases or adjustments:

| Step | Cash position | Bank position | Outcome |
|---|---:|---:|---|
| D3 approved opening | 100,000 | 1,000,000 | Opening snapshot and internal allocation. |
| D4 successful customer deposit of 500,000 | 600,000 | 500,000 | Expected positions change; customer entry itself does not mutate wallet caches. |
| D6 cash-to-bank run of 400,000 | 200,000 | 900,000 | Affected caches refresh to the projection and apply the transfer once. |
| D8 actual closing matches these positions | 200,000 | 900,000 | Reported total is 1,100,000. |
| D9 submit closing | Allocated wallet cache becomes 0 | Allocated wallet cache becomes 0 | Frozen observations remain 200,000 and 900,000; 1,100,000 is returned internally once. The external bank account is not emptied. |
| R4-R5 daily control | Frozen cash remains 200,000 | Frozen bank remains 900,000 | Statement evidence accounts for the -500,000 customer debit and +400,000 bank-run credit; daily control records account-level movement status without claiming exact cutoff certification. |

If a genuine overnight bank credit of 2,000 occurs, the next observed opening may be cash 200,000 and bank 902,000. The app preserves yesterday's bank closing of 900,000 and records a +2,000 opening bridge. The 1,102,000 opening is split into 1,100,000 internal allocation plus 2,000 externally observed bank change, subject to normal opening checks. Admin later explains the overnight credit from bank evidence. It is not an invented vault top-up.

## 11. Status, notification and retry expectations

| What the user sees | Interpretation / next action |
|---|---|
| Opening Draft / PendingApproval | Edit/save as permitted, submit and wait for an independent opening reviewer. |
| Opening Rejected | Read the reason, correct and resubmit. No trading until approved. |
| Session open | Approved opening; permitted current-session customer capture, runs and adjustments. |
| Session ended; awaiting admin approval | Trading is closed. Review is outstanding, but this alone does not block the next business day. |
| Import Uploaded / Processing | Processing is durable and server-side. Refresh/reopen the import; do not repeatedly upload it to simulate progress. |
| Import Failed | Evidence is not accepted statement data. Read row/column diagnostics, correct the CSV and upload a new revision. |
| Daily control `Reconciled` / `WithExceptions` | Date-level account movement control is complete, optionally with a documented exception. It is not exact session-cutoff certification. |
| Daily control `NeedsReview` / `NeedsCorrection` | Flag, missing coverage, unknown row, unmatched item or movement difference remains; Admin investigates. This alone does not block the next opening. |
| ReadyForReview / Committed | Reviewable universal CSV extraction versus accepted bank occurrences; neither moves funds nor automatically approves a closing. |
| NeedsCorrection | Computed review blockers. An explicit C1 request is needed to grant teller correction access. |
| Reconciled / WithExceptions | Separate session-certification result, when precise observations allow it. `WithExceptions` retains outstanding verified items. |
| NeedsReview | Later/current facts require renewed review; a retained older approval is not proof of current completeness. |
| LegacyIncomplete | Historical records keep their legacy meaning; modern transaction completeness is not claimed. |

Opening requests notify Admin/Supervisor; modern closing review and resubmitted corrections notify admins; approval/rejection/correction notices go to the owning agent. These are in-app notifications, not a claim of scheduled email/SMS or automatic nightly reconciliation.

On a timeout or interrupted save, preserve the draft and refresh status before retrying. Supported command replay prevents duplicate financial effects. Android retains protected drafts and retry identities; count drafts are business-date scoped. A draft is not proof of server acceptance, and the app is not a fully offline transaction/bank system. Stale review screens must be refreshed before a new decision.

## 12. Optional AI/MCP user journey - proposed, not implemented

This is supplementary to the human workflow. Dedicated banking/reconciliation MCP tools and authenticated production hosting have not been implemented by the banking feature.

| Step / actor | Input | Expected future output | Boundary |
|---|---|---|---|
| **M1 - Authorized user** | Ask a scoped question such as "Which closings for my branch still need approval?" with date/account scope where relevant. | AI calls authorized read-only tools and summarizes matching sessions, blockers, amounts, source record IDs and data freshness. | Statement/reconciliation detail remains restricted to authorized admins. |
| **M2 - User** | Open the cited record in Agenti and inspect supporting evidence. | Human verifies the findings and takes R5/R6 or the applicable case action in the application. | AI analysis is advisory, not a bank verification or approval. |
| **M3 - Application / MCP service** | Enforce caller permissions, branch scope, bounded queries and approved data disclosure. | Auditable, minimized read-only results. | No database writes, automatic approvals, wallet changes or arbitrary exposure of customer data. |

The current core daily workflow does not depend on MCP being present.

## 13. Implementation traceability

Paths are repository-relative. These are the inspected implementation sources, not proof of a production deployment.

| Workflow area | Representative source |
|---|---|
| Account setup and one-agent/account activation gates | `EastSeat.Agenti.Web\Features\BankStatements\BankAccountService.cs:121-162` |
| Universal CSV template, private upload, baseline audit and statement preview/commit | `EastSeat.Agenti.Web\Features\BankStatements\Schema\agenti-bank-statement-v1.json`; `EastSeat.Agenti.Web\Components\Pages\BankStatements.razor`; `EastSeat.Agenti.Web\Features\BankStatements\BankStatementService.cs` |
| Daily session, prior operational-close blocker, count inputs | `EastSeat.Agenti.Web\Features\CashCounts\BankSessionWorkflow.cs:37-147,185-308` |
| Opening submit/reject/notifications | `EastSeat.Agenti.Web\Features\CashCounts\BankSessionWorkflow.cs:309-398` |
| Exactly-once allocation/closing and frozen snapshots | `EastSeat.Agenti.Web\Shared\Services\SessionSettlementService.cs:22-190` |
| Overnight bank bridge | `EastSeat.Agenti.Web\Shared\Services\SessionSettlementService.cs:218-250` |
| Capture fields, actual effects and validation | `EastSeat.Agenti.Web\Components\Pages\CustomerTransactions.razor:49-104`; `EastSeat.Agenti.Web\Features\CustomerTransactions\CustomerTransactionService.cs:91-181,325-368` |
| Case evidence, verified credit, retries/refunds | `EastSeat.Agenti.Web\Features\CustomerTransactions\CustomerTransactionService.FollowUps.cs:10-27,99-212` |
| Bank-run available-cash validation | `EastSeat.Agenti.Web\Features\BankRuns\BankRunService.cs:23-168` |
| Independent adjustment review | `EastSeat.Agenti.Web\Features\WalletAdjustments\WalletAdjustmentService.cs:326-370` |
| Daily control, date-only cutoff protection, matching and Supervisor flag history | `EastSeat.Agenti.Web\Features\BankReconciliation\BankReconciliationService.Matching.cs`; `EastSeat.Agenti.Web\Features\BankReconciliation\BankReconciliationService.Evaluation.cs`; `EastSeat.Agenti.Web\Features\BankReconciliation\BankReconciliationService.Flags.cs` |
| Exception, correction and approval decisions | `EastSeat.Agenti.Web\Features\BankReconciliation\BankReconciliationService.Decisions.cs:12-277` |
| Correction resubmission | `EastSeat.Agenti.Web\Features\CustomerTransactions\CustomerTransactionService.Corrections.cs:179-210` |
| Web Admin/Supervisor review and Android separation | `EastSeat.Agenti.Web\Components\Pages\BankStatements.razor`; `EastSeat.Agenti.Web\Components\Pages\BankReconciliation.razor`; Android no longer includes statement/reconciliation pages |
| Workflow state names | `EastSeat.Agenti.Web\Shared\Domain\Enums\ReconciliationEnums.cs:1-110` |

The daily process is complete only when operational closure, the relevant current financial certification, and any remaining customer/bank obligations are each explicitly accounted for. A closed session, an uploaded file, or equal grand totals alone does not satisfy all three.
