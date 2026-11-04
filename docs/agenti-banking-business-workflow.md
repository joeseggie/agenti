# Agenti: customer banking and bank-statement reconciliation

## Purpose and scope

This workflow describes the app-user journey for the customer-transaction capture and bank-statement reconciliation feature merged in PR #94, verified against `main` at commit `d80d172`. It covers both the web and Android workspaces.

**Scope assumption:** "the new feature" means the implemented banking/reconciliation feature. The previously discussed production MCP service is a proposed, separate read-only extension; its optional user journey is in section 12.

This is a description of the implementation and its intended operating procedure after release, not confirmation that it is deployed or enabled in production. No production data was accessed to prepare it.

**Release gate:** the generic workflows exist, but no real-bank statement profile is installed and verified in the inspected application. Representative bank exports, a verified importer, release acceptance and activation are still required. Uploading an unsupported statement retains private evidence but produces a failed import; it does not create valid bank transactions or enable enrollment.

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
| Supervisor | Independently approve or reject opening counts and wallet adjustments within the permitted branch. | Count/adjustment approvals; Android Cash Count approval actions. |
| Administrator | Account setup, activation, statement intake, reconciliation, exceptions, correction grants and closing certification. May also perform opening/adjustment review. | Web: Statements and Accounts (`/bank-statements`), Reconciliation Reviews (`/bank-reconciliation`), count approvals. Android: Bank statements, Bank reconciliation and Cash Count. |
| Application | Validate permissions and state, calculate expected positions, preserve evidence/history, process imports, enforce review gates and issue in-app notifications. | Automatic responses to user actions and the server-side import processor. |
| Bank/customer | Supply the actual transaction result, receipt, statement, reversal confirmation or customer settlement evidence. | Outside Agenti; the user records/verifies this evidence in Agenti. |
| Implementation/release team | Verify real-bank formats, release compatible backend/mobile versions and satisfy deployment gates. | Release prerequisite, not a teller action. |

An approver cannot approve their own modern opening, review their own modern wallet adjustment, or certify their own closing. If an Admin is also the teller, another authorized person must perform the relevant approval. Supervisors do not gain statement/reconciliation privileges.

## 3. One-time onboarding and activation

| ID / actor | User action | Expected inputs | Output / outcome | If requirements are not met |
|---|---|---|---|---|
| **A1 - Release team + Admin** | Validate the bank-statement format and release readiness. | Representative redacted CSV, XLSX and selectable-text PDF exports for the intended formats; account/reference conventions; timezone; balance basis; coverage and overlapping-export examples. | Installed, verified import profile for explicitly supported formats, with compatible application releases. | Workflow activation remains blocked. Generic file selection is not evidence that every bank layout is supported. Scanned PDFs/OCR are outside this release. |
| **A2 - Admin** | Open Statements and Accounts; select **Register a bank account**, then **Save account binding**. | Existing agent bank wallet; bank name; account identifier as shown on statements; statement timezone; confirmed balance basis (Ledger, Available or Float); installed statement profile. Currency comes from the wallet. | A bank-account record bound to the existing wallet. No money moves and old sessions remain unchanged. | Wrong branch, inactive wallet or duplicate wallet binding is rejected. Account identity, timezone and balance basis cannot be casually rewritten after activity. |
| **A3 - Admin** | Select the account and **Enable new-session workflow**. | Active account and wallets; verified matching profile/balance basis; completed legacy sessions; one active agent, one enrolled bank account, one active cash wallet and one active bank wallet in the branch; consistent wallet currency. | Eligible new sessions use the new workflow. Existing sessions retain their version/history. | Validation explains the unmet prerequisite. Pausing enrollment later stops new enrollment; it does not turn modern sessions back into legacy ones. |

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

Start this process after closing submission whenever the bank evidence is available. If the bank publishes the required evidence later, leave financial approval pending; do not make a substitute statement.

| ID / actor | User action | Expected inputs | Output / outcome | Decision / next step |
|---|---|---|---|---|
| **R1 - Admin** | Download the statement from the bank outside Agenti. Select the account and **Upload and prepare preview**. | Actual source file in a format supported by the verified profile; correct bank account/currency/balance basis; evidence covering the session and relevant overnight interval. | Private original evidence and durable import identity. Usable imports progress from `Uploaded` to `Processing`. | Unsupported/unverified formats fail explicitly. Keep the error visible; obtain valid evidence or a verified adapter rather than treating an empty import as success. |
| **R2 - Admin** | Refresh/reopen the preview and review extraction, balances and coverage. | Extracted times, references, identifiers, signed amounts, running balances and source locations. For corrections: replacement normalized values and evidence-based reason. For ambiguous overlaps: new occurrence vs existing occurrence/revision decision and applicable existing ID. | Source-linked reviewable rows; corrections preserve the original extraction and create normalization revisions. | Resolve errors, contradictory coverage and occurrence identity decisions before committing. Two genuinely separate identical-looking transactions must not be silently deduplicated. |
| **R3 - Admin** | Select **Commit reviewed bank entries**. | Valid current import revision and all required row decisions. | Accepted canonical bank occurrences with source provenance; import becomes `Committed`. Commit does not move vault/wallet funds or approve a closing. | An identical file can be reused without duplicating money/evidence effects. Conflicts or incomplete decisions block acceptance instead of producing a partial successful ledger. |
| **R4 - Admin** | Open Reconciliation Reviews; choose account and dates; **Start review**; select **Match unique exact entries**. | Committed statement evidence; approved opening and submitted closing snapshots; customer movements; bank runs; reviewed adjustments; date range. | Per-session wallet comparisons, bank/register matches, unmatched items, overnight issues, customer cases and explicit approval blockers. | Only uniquely exact candidates are automatically matched. Ambiguous/missing references or competing candidates require review; matching is not closing approval. |
| **R5 - Admin** | Investigate every blocker; manually match, classify genuine bank-only activity, document a permitted exception, or request historical correction. | Selected bank occurrence and register source; amount/currency/direction agreement; supporting evidence and 10-2,000-character reason; affected case/session/amount; permitted correction scope. | Attributable decisions and a recalculated review. Eligibility is `Reconciled` or `WithExceptions` only when no approval blockers remain. | Use the decision table below. Refresh if concurrent changes make the screen stale. A verified exception is not a blanket waiver for missing customer transactions or unexplained debits. |
| **R6 - Independent Admin** | For each eligible session, select **Approve against this evidence**. | Current review version/evidence basis, frozen closing and all required explanations. The UI supplies the technical version/request identity. | Closing becomes Approved; an immutable, versioned result records the snapshot/evidence reviewed, reviewer and time; agent receives an in-app approval notification. | Self-certification, stale evidence or unresolved blockers is rejected. No vault deposit, wallet zeroing or later-session fund movement is repeated. |
| **R7 - Admin, on later evidence** | Reopen the affected review if a source changes, late evidence appears, or a historical correction changes the facts. | New accepted source/revision, linked later movement or correction; original result/history. | Affected current results require renewed review; a new certification preserves/supersedes the prior result without rewriting old count observations or current balances. | A previously approved count does not prove that the current evidence is still reconciled. Investigate `NeedsReview` and repeat R4-R6 as needed. |

### R5 decision table

| Finding | Required user input/action | Outcome |
|---|---|---|
| Correct bank row and register leg exist but did not auto-match | Select both and provide a reason; amount, currency and direction must agree. | One active allocation links that bank occurrence to that register source. |
| Genuine bank fee, commission or other verified bank-origin movement | Select allowed classification, source/supporting evidence and explanation. | Bank-only adjustment is accounted for in review; no fabricated customer sale/receipt. |
| Bank reversal belongs to a customer case | Link the actual bank return to the correct related case movement/expected settlement. | The return is accounted for once; it is not also counted as a separate generic adjustment. |
| Failed bank operation with a documented outstanding case | Record BankFailure with verified bank explanation, evidence, linked case/occurrence and affected amount. | Approval may become eligible with `WithExceptions`, if all other conditions are met. The unresolved obligation remains visible. |
| Posting delay | Record evidence and the exact affected source/case/amount; explicitly say whether the reported closing already includes the delayed posting. | The explained timing difference is evaluated against the actual balance basis; unrelated gaps remain blockers. |
| Omitted or incorrect customer record | Send a scoped historical correction request; follow C1-C3. | Approval waits for resubmission and reevaluation. |
| Missing statement coverage, unexplained debit, unexplained wallet variance or unverified explanation | Obtain/investigate evidence; leave the closing pending. | Approval remains unavailable. A matching grand total or a generic note is insufficient. |
| Pending wallet adjustment | Obtain separate authorized adjustment review. | Reconciliation then uses the reviewed disposition; it does not bypass that approval. |

## 7. Approval checklist

Before R6 can succeed, the review must establish the following, not merely equal opening and closing totals:

1. Approved opening and immutable submitted closing snapshots exist for the modern session, including its recorded operational settlement.
2. Statement coverage is complete and consistent for the required observation intervals; bank balances can be established at the cutoffs.
3. Each relevant bank occurrence and register bank leg is matched or accounted for by a specifically permitted, evidenced decision.
4. Wallet positions, opening/closing observations and the overnight bridge have no unexplained differences.
5. Wallet adjustments have their separate review; requested historical corrections are resubmitted.
6. Customer cases are completed or covered by valid verified exceptions; inconsistent settlement/refund amounts are not excused.
7. No unresolved unexplained/unverified bank exception remains, and the admin is reviewing the latest evidence version.

The review uses actual observation intervals, generally after the opening observation through the closing observation, and after the preceding closing through the next opening. A calendar date filter alone is not proof of coverage. A zero-transaction day still requires explicit coverage with consistent balances.

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
| R4-R6 review | Frozen cash remains 200,000 | Frozen bank remains 900,000 | Statement evidence accounts for the -500,000 customer debit and +400,000 bank-run credit; independent admin certifies without moving funds again. |

If a genuine overnight bank credit of 2,000 occurs, the next observed opening may be cash 200,000 and bank 902,000. The app preserves yesterday's bank closing of 900,000 and records a +2,000 opening bridge. The 1,102,000 opening is split into 1,100,000 internal allocation plus 2,000 externally observed bank change, subject to normal opening checks. Admin later explains the overnight credit from bank evidence. It is not an invented vault top-up.

## 11. Status, notification and retry expectations

| What the user sees | Interpretation / next action |
|---|---|
| Opening Draft / PendingApproval | Edit/save as permitted, submit and wait for an independent opening reviewer. |
| Opening Rejected | Read the reason, correct and resubmit. No trading until approved. |
| Session open | Approved opening; permitted current-session customer capture, runs and adjustments. |
| Session ended; awaiting admin approval | Trading is closed. Review is outstanding, but this alone does not block the next business day. |
| Import Uploaded / Processing | Processing is durable and server-side. Refresh/reopen the import; do not repeatedly upload it to simulate progress. |
| Import Failed | Evidence is not accepted statement data. Read the error; fix the source/profile and reprocess as appropriate. |
| ReadyForReview / Committed | Reviewable extraction versus accepted bank occurrences; neither automatically approves a closing. |
| NeedsCorrection | Computed review blockers. An explicit C1 request is needed to grant teller correction access. |
| Reconciled / WithExceptions | Review eligibility/outcome; an independent admin still explicitly certifies the closing. WithExceptions retains outstanding verified items. |
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
| Profile warning and statement preview/commit controls | `EastSeat.Agenti.Web\Components\Pages\BankStatements.razor:22-168`; `EastSeat.Agenti.Web\Features\BankStatements\BankStatementService.cs:49-58` |
| Daily session, prior operational-close blocker, count inputs | `EastSeat.Agenti.Web\Features\CashCounts\BankSessionWorkflow.cs:37-147,185-308` |
| Opening submit/reject/notifications | `EastSeat.Agenti.Web\Features\CashCounts\BankSessionWorkflow.cs:309-398` |
| Exactly-once allocation/closing and frozen snapshots | `EastSeat.Agenti.Web\Shared\Services\SessionSettlementService.cs:22-190` |
| Overnight bank bridge | `EastSeat.Agenti.Web\Shared\Services\SessionSettlementService.cs:218-250` |
| Capture fields, actual effects and validation | `EastSeat.Agenti.Web\Components\Pages\CustomerTransactions.razor:49-104`; `EastSeat.Agenti.Web\Features\CustomerTransactions\CustomerTransactionService.cs:91-181,325-368` |
| Case evidence, verified credit, retries/refunds | `EastSeat.Agenti.Web\Features\CustomerTransactions\CustomerTransactionService.FollowUps.cs:10-27,99-212` |
| Bank-run available-cash validation | `EastSeat.Agenti.Web\Features\BankRuns\BankRunService.cs:23-168` |
| Independent adjustment review | `EastSeat.Agenti.Web\Features\WalletAdjustments\WalletAdjustmentService.cs:326-370` |
| Matching and review gates | `EastSeat.Agenti.Web\Features\BankReconciliation\BankReconciliationService.Matching.cs:12-56`; `EastSeat.Agenti.Web\Features\BankReconciliation\BankReconciliationService.Evaluation.cs:57-240` |
| Exception, correction and approval decisions | `EastSeat.Agenti.Web\Features\BankReconciliation\BankReconciliationService.Decisions.cs:12-277` |
| Correction resubmission | `EastSeat.Agenti.Web\Features\CustomerTransactions\CustomerTransactionService.Corrections.cs:179-210` |
| Web admin actions and mobile navigation | `EastSeat.Agenti.Web\Components\Pages\BankReconciliation.razor:20-185`; `EastSeat.Agenti.Android\AppShell.xaml:16-35` |
| Workflow state names | `EastSeat.Agenti.Web\Shared\Domain\Enums\ReconciliationEnums.cs:1-110` |

The daily process is complete only when operational closure, the relevant current financial certification, and any remaining customer/bank obligations are each explicitly accounted for. A closed session, an uploaded file, or equal grand totals alone does not satisfy all three.
