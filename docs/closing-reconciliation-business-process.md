# Closing balance reconciliation: business process

**Status:** Implemented and locally validated on 2026-11-16.
See the [executed validation record](closing-reconciliation-validation.md) for
coverage and limitations. This is not a production-deployment confirmation.

## Objective and limits

Provide a straightforward daily check that every Bank wallet's approved opening
balance, plus statement credits and minus statement debits, agrees with the
agent's submitted closing balance.

Reconciliation is performed once for the complete closing, across all its Bank
wallets. It is not performed separately for each bank, does not match individual
customer transactions, and does not certify balances at an exact session cutoff.
Cash and Mobile Money wallets do not require statements.

**Statement waiver.** An Admin can switch statement reconciliation off for a Bank
wallet type on the Wallet Types page (table switch or Edit dialog). It is a standing
setting, applied to any closing without a saved run. Waived wallets still need their
bank-account binding, need no statement, appear in the saved report as "Not
reconciled" (frozen per run, never counted as failures), and any uploaded statement
for them is ignored. An all-waived closing is still run by the Admin and then
approved ordinarily. Each change is recorded append-only (who, when, new value).

Reconciliation and closing approval are separate decisions. A failed
reconciliation remains failed even if an Admin subsequently approves the closing
with an audited exception.

## Roles and responsibilities

| Role | Responsibility |
|---|---|
| Agent | Enter all wallet closing balances and submit the closing for approval through the normal cash-count process. See the ordinary submission/approval status, not reconciliation screens or controls. |
| Admin | Maintain bank-account bindings, upload statements on the separate statement page, run reconciliation for the whole closing, inspect the report, and perform normal or exception approval as permitted. |
| Supervisor | Receive the approval request, inspect the closing and report, and perform existing ordinary approvals after a successful reconciliation. Cannot upload statements, run reconciliation, or approve a reconciliation exception. |
| Application | Enforce branch access, date and file validation, complete Bank-wallet coverage, immutable inputs/results, approval safeguards, and persistent in-app notifications. |

Existing independent-approval, discrepancy, wallet-adjustment, session, and vault
safeguards remain in force. Reconciliation is not permission to override them.
There are no new reconciliation features in Android or MCP, and no new mobile
reconciliation API.

## Prerequisites

- Each Bank wallet is bound to its own bank account. A statement for one wallet
  cannot satisfy another wallet's requirement.
- The same agent's approved opening count for the closing business date includes
  the required wallets. Current live wallet balances are not the opening source.
- The closing has been submitted. Its recorded wallet set and amounts determine
  the reconciliation obligation; later account deactivation cannot remove a bank.
- The Admin obtains a complete statement for each Bank account for that single
  business date and prepares it using the shipped universal CSV schema.
- Cash and Mobile Money continue through their existing count and approval checks.

## Daily process

| Step | Actor and action | Required outcome |
|---|---|---|
| 1 | Agent enters the closing balances for all wallets and submits the closing. | Closing awaits approval. Existing operational settlement occurs exactly once under the applicable session safeguards. |
| 2 | Application notifies active Admins and Supervisors in the agent's branch. | A persistent in-app notification links to the submitted closing. No new email, SMS, or push channel is implied. |
| 3 | Admin/Supervisor opens the closing approval detail. | See the agent, business date, submitted wallet balances, and statement availability for every Bank wallet. Only Admin can execute **Reconcile closing**. |
| 4 | Admin separately opens **Bank statements**, selects the bank account and one business date, selects the CSV, and confirms the account and full-day coverage. | The statement upload is not placed on the agent's cash-count screen or the closing approval detail. |
| 5 | Application validates the entire upload. | Reject invalid or empty files, dates different from the selected date, and any multi-day file/range. Accept no partial set of rows. |
| 6 | Application stores the accepted statement and its rows in the database, associated with the selected bank account and business date. | Retain source evidence, schema identity, uploader/time, and any superseded import and replacement reason. |
| 7 | Admin repeats statement intake for every Bank wallet in the closing. | Cash and Mobile Money require no file. One account's file never covers all of an agent's banks. |
| 8 | Admin returns to the closing and selects **Reconcile closing**. | Preflight checks require an approved opening and an accepted statement for every captured Bank wallet on the closing date. Missing inputs block the whole operation before any result is recorded. |
| 9 | Application evaluates all Bank wallets together and stores one complete report. | Each Bank wallet has a Success or Failed result, with amounts and explanations. The operation does not move money or approve the closing. |
| 10 | Admin/Supervisor reviews a wholly successful report and separately selects **Approve closing**. | Approval proceeds only when the existing additional safeguards also pass. |
| 11 | If any Bank wallet failed, Admin investigates and may select **Approve with reconciliation exception**. | Require a reason of 10-2,000 characters and explicit acknowledgement of the failed wallets/differences. Record a separate approval decision; keep the reconciliation Failed. |
| 12 | Authorized reviewers later open the saved report. | See the original inputs, per-wallet outcome, actor/time, and any distinct normal or exception approval. There is no rerun action. |

If there are no Bank wallets, display **Bank reconciliation not required** and
retain the existing closing-approval process:

- A modern-workflow Cash/Mobile-only closing is approved with the ordinary
  **Approve closing** action by an Admin or Supervisor other than the agent. No
  reconciliation run or exception decision exists; the approval audit note states
  that bank reconciliation was not required. An exception approval is refused.
  If the agent's approved opening held a Bank wallet, the closing is still treated
  as bank-bearing even when the closing form omitted it.
- A legacy-workflow Cash/Mobile-only closing keeps its existing approve/reject
  buttons in the cash-count approvals list. Only a closing that is bank-bearing,
  or on the modern workflow, links to the closing review.
- A legacy closing must count every wallet from the approved opening, including
  each Bank wallet, exactly once. A submitted closing is frozen (no edits) once
  its snapshot or settlement exists, even if a stale view still shows it as a draft.

### Variances, settlement and session closure

- Closing a modern session with a Cash/Mobile Money count that differs from the
  recorded movements requires the agent's explanation at submission. Submission
  then raises one pending-review discrepancy (expected, actual and variance for
  the non-bank wallets) which the reviewer approves together with the closing.
  A bank reconciliation exception cannot override a Cash/Mobile variance that has
  no explained discrepancy. A Bank-only variance is handled by the reconciliation
  report, not by a discrepancy.
- A submitted closing whose float has been settled is treated as submitted for
  the next-day opening comparison, the opening bridge and session completion.
  The session closes (and is operationally locked) only when every agent with an
  approved opening has submitted a settled closing. Financial approval of a
  settled closing does not gate closure and never repeats settlement.
- The recorded input fingerprint uses only stable facts (wallet id/type/currency,
  submitted amount, bound account, opening, closing and date). Renaming a wallet
  after reconciliation does not invalidate the saved report or its approval.
- Submitting a legacy closing retries up to three times on a database
  serialization conflict, each time in a fresh transaction, then reports a
  conflict instead of a partial save.

## Statement intake rules

Use the release-shipped `agenti-bank-statement-v1` CSV template:

```csv
TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance
```

| Rule | Expected behavior |
|---|---|
| One selected date | Provide a single date, not a start/end range. UI defaults use the Uganda business clock. |
| Exact date agreement | Every row's `TransactionDate` must equal the selected date. Do not silently filter out other dates. |
| Multi-day input | Reject the entire file or request, even if some rows match the requested date. |
| Empty statement | Reject header-only and otherwise empty statements in this release. Do not invent a zero-value transaction. |
| Valid amounts | Follow the shipped schema: exactly one positive Debit or Credit per row, amounts written with exactly two decimals, and a valid running-balance chain. |
| Actual times only | `TransactionTime` is optional. Preserve real supplied times; never invent midnight or a closing time for date-only rows. |
| Complete statement | Upload one complete statement per bank account/date, not partial files to be merged. |
| Account confirmation | Show the wallet, bank, and account identity. Admin must confirm that the file belongs to this account and covers the complete date. |
| Duplicate/replacement | An unchanged replay must not duplicate accepted data. A different replacement before reconciliation requires a reason and retains the earlier version. |
| Used statement | Once a recorded reconciliation references a statement, it cannot be replaced for that run or silently change its result. |

The CSV has no account identifier. Account confirmation is an Admin attestation,
not automatic proof of bank identity or statement completeness. Original XLSX/PDF
exports must be converted to the universal CSV outside Agenti; do not send
customer statements to external conversion services.

**Known release limitation:** a Bank wallet with genuinely no transactions cannot
complete this workflow using an empty statement. No-activity declarations and
balance-only evidence are not supported in this release. Exception approval
cannot bypass a missing accepted statement.

## Calculation and report

For each Bank wallet:

```text
Expected closing = Approved opening + Total credits - Total debits
Difference       = Submitted closing - Expected closing
```

Use the persisted approved opening and submitted closing for the same agent,
wallet, and business date. Never use live balances, which may have been zeroed by
settlement or changed by a later session.

A successful result requires exact agreement at two decimal places. Validate
the statement's running balances and anchor its implied opening and ending
balances to the approved opening and submitted closing. A consistent but
offset running-balance chain must not produce a successful result.

The report includes the closing identity, agent, business date, run actor/time,
and, for each Bank wallet:

- Wallet and bank-account identity, currency, and statement revision/reference.
- Approved opening, total debits, total credits, and calculated closing.
- Statement opening/ending balances, submitted closing, and difference.
- Success or Failed, with a specific explanation for failure.

The overall result succeeds only when every Bank wallet succeeds. A positive
difference in one wallet cannot cancel a negative difference in another.
Cash/Mobile Money balances remain visible in the closing, but are not given a
bank-statement reconciliation result.

Arithmetic agreement does not prove that every transaction is legitimate,
resolve a customer case, explain an unknown movement, establish the bank
account's identity, or certify an exact session cutoff. These claims must not
appear in report labels or approval messages.

## Approval, failure, and retries

| State | Permitted action |
|---|---|
| No accepted statement for any required Bank wallet | Upload the missing statement, then attempt the whole-closing run. No report has yet been recorded. |
| No approved opening or missing account binding | Correct the prerequisite through its authorized existing workflow. Do not substitute zero or a statement-derived opening for the required approved count. |
| All Bank wallets succeeded | Admin/Supervisor may perform ordinary approval, subject to all other safeguards. |
| No Bank wallet required (modern workflow) | Admin/Supervisor performs ordinary approval without a run; exception approval is refused. |
| At least one Bank wallet failed | Ordinary approval is blocked. Admin may record a separately audited exception approval with the required reason/acknowledgement. |
| Exception-approved closing | Closing approval and Failed reconciliation remain distinct facts. The failed report is immutable and remains available to reviewers. |
| Previously recorded run | Retrieve the same saved report. Do not run only failed wallets or recalculate the whole closing. |
| Technical error before atomic persistence completes | Roll back incomplete work and show the error. A safe retry is allowed because no recorded business run exists. |

Double-clicks, concurrent requests, and interrupted responses must not create
duplicate runs, approvals, notifications, or settlement movements. Results must
remain tied to the exact opening, closing, and statement inputs used.

An outstanding daily reconciliation must not introduce a new next-day opening
gate. Existing operational-session, opening-approval, and funding safeguards
remain; financial approval must never replay closing settlement.

## Replacement and historical records

The old account-by-account review, multi-day import, matching, classification,
exact-cutoff certification, and reconciliation-driven correction/rerun workflows
are retired rather than kept alongside the replacement.

Retired runtime tables leave the active application schema. Historical imports,
rows, decisions, flags/responses, and other audit evidence are retained in a
read-only legacy archive through a new migration. Applied migrations are retained.
Shared financial records and their evidence/relationships are not deleted.

Already-approved closings keep their historical status. Pending closings use
the new process and require fresh single-day uploads; legacy reviews are not
silently treated as new successful reconciliations.

Wallet/account bindings, customer transactions and case follow-ups, bank runs,
opening/count workflows, notifications, and vault controls remain. Android
continues its unrelated banking operations. MCP remains read-only and gains no
new reconciliation workflow.

## Validation and release evidence

Focused service/regression tests, PostgreSQL migration/concurrency/API tests and
real local browser workflows passed using synthetic data and the actual universal
CSV parser. Web, MCP and Android builds succeeded.

The [validation record](closing-reconciliation-validation.md) contains executed
commands, setup, browser steps, expected/actual outcomes, artifacts, skipped tests
and unexecuted checks. No full-repository test pass or production deployment is
implied, and no existing business database was accessed.
