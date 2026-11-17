# Closing reconciliation: executed local validation

**Execution date:** 2026-11-16. **Result:** the replacement's targeted service,
PostgreSQL/API and real-browser checks passed; Web, MCP and Android built.
The PR review follow-up (below) re-ran every layer after the review fixes.
This is local validation, not a production deployment or a claim that the entire
repository test suite passed.

The approved process is documented in
[Closing balance reconciliation: business process](closing-reconciliation-business-process.md).
The implementation replaces the old workflow and preserves its records in a
read-only archive rather than deleting financial history.

## Environment and isolation

- Dedicated worktree/branch: `u/jserunjogi/closing-balance-reconciliation`, based
  on `af0dbcc`. The original validation ran before the work was committed; it was
  later committed and opened as a pull request. No deployment was performed.
- Shared Windows development machine; existing .NET SDKs/workloads were used
  without upgrading frameworks or changing global tool settings. Targets remain
  `net10.0` and `net10.0-android`; the default installed SDK was a .NET 11 preview.
- Docker Desktop supplied fresh `postgres:16-alpine` Testcontainers databases
  with generated test credentials and dynamically allocated ports. Migrations
  ran only against these disposable databases.
- `BankingApplicationFactory` explicitly supplied the container connection,
  `Testing` environment, test JWT key and empty Application Insights connection.
  It disabled unrelated expiration/audit-cleanup hosted services.
- Synthetic setup was marked complete before app startup. Users, balances,
  account identifiers, customer operations and CSV files were synthetic.
  Existing business databases and actual bank statements were not accessed.
- Playwright drove Chromium against the real application on local Kestrel using
  an ephemeral HTTP port (`UseKestrel(0)`). HTTP requests/page navigation verified
  readiness. The real shipped CSV parser was used; the old JSON fixture parser
  was removed.
- Admin used the normal login page. Agent/Supervisor and isolation scenarios
  used test-issued authenticated sessions; they were not all manual login tests.
- Test hosts and containers were disposed after execution. No local app was left
  running, and shared Docker Desktop was not stopped.

Archive-write guards remain enabled while tests execute. Integration cleanup
temporarily disables triggers only on its isolated Testcontainers cleanup
connection, restoring them in `finally`; this is not application behavior.

## Final automated results

| Layer | Passed | Failed | Skipped | Coverage |
|---|---:|---:|---:|---|
| Affected unit/regression selection | 196 | 0 | 18 | CSV/intake, reconciliation, accounts, customer operations, counts, sessions, adjustments, vault and Android banking client contracts |
| PostgreSQL/API selection | 13 | 0 | 0 | Populated/fresh migrations, archive/FKs, legacy cutover, concurrent commands, revision/run race, authorization and evidence access |
| Focused evidence-download rerun | 1 | 0 | 0 | One of the 13 tests above, additionally asserting original source bytes and CSV content type |
| Local browser selection | 5 | 0 | 0 | Actual CSV upload, whole-closing success/failure/approval, next-day funds, navigation state, account setup, role/branch isolation and cash-count regression |
| Web build | Success | 0 errors | - | ASP.NET Core/Blazor and shared contracts |
| MCP build | Success | 0 errors | - | Preserved read-only reporting |
| Android build | Success | 0 errors | - | `net10.0-android`, including updated linked DTOs |

The 18 skipped tests were already marked skipped in `VaultServiceTests` because
its in-memory provider does not support navigation projection or PostgreSQL
`FOR UPDATE`. They are not counted as passes. Existing warnings remain: NU1603
package-version resolution, NU1510 framework-provided reference, CS8524 enum
exhaustiveness, and Android CS0618/CS8622 warnings.

An earlier whole-unit-project attempt stalled and was explicitly stopped.
The bounded affected selection above completed. The entire unit, integration
and E2E projects are **not** claimed as passed.

### Commands executed

Run from the repository root with existing restored dependencies and Docker
available. Commands were serialized where they share Web build outputs.

```powershell
dotnet test .\tests\EastSeat.Agenti.UnitTests\EastSeat.Agenti.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~BankReconciliation|FullyQualifiedName~CashCountServiceTests|FullyQualifiedName~CashSessionServiceTests|FullyQualifiedName~WalletAdjustmentServiceTests|FullyQualifiedName~VaultServiceTests|FullyQualifiedName~BankingApiClientTests' --blame-hang --blame-hang-timeout 90s --blame-hang-dump-type none --verbosity quiet --logger 'trx;LogFileName=closing-reconciliation-regressions.trx'

dotnet test .\tests\EastSeat.Agenti.IntegrationTests\EastSeat.Agenti.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~ReconciliationPostgresTests|FullyQualifiedName~ReconciliationApiTests' --verbosity quiet --logger 'trx;LogFileName=closing-reconciliation-postgres.trx'

dotnet test .\tests\EastSeat.Agenti.E2ETests\EastSeat.Agenti.E2ETests.csproj --no-restore --filter 'FullyQualifiedName~ReconciliationBrowserTests|FullyQualifiedName~CashCountBrowserTests' --verbosity quiet --logger 'trx;LogFileName=closing-reconciliation-browser.trx'

dotnet test .\tests\EastSeat.Agenti.IntegrationTests\EastSeat.Agenti.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~SupervisorCanReadStatementEvidence_ButAgentsAndForeignBranchCannot' --verbosity quiet --logger 'trx;LogFileName=closing-reconciliation-evidence.trx'

dotnet build .\EastSeat.Agenti.Web\EastSeat.Agenti.Web.csproj --no-restore --verbosity quiet
dotnet build .\EastSeat.Agenti.Mcp\EastSeat.Agenti.Mcp.csproj --no-restore --verbosity quiet
dotnet build .\EastSeat.Agenti.Android\EastSeat.Agenti.Android.csproj --no-restore --verbosity quiet

git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol --no-pager diff --check
```

The whitespace check passed with Windows CRLF recognized; local documentation
links resolved. Source inspection
confirmed that retired service/processor registrations, review pages, correction
actions and mobile links are removed. Historical entities, enums and applied
migrations remain deliberately for compatibility and archive metadata.

## Executed browser procedure

The principal test is
`ReconciliationBrowserTests.Closing_ReconcilesAllBanksWithRealCsv_AndPreservesNextDayBalances`.
It performed these actions against the running local app, with database checks
afterward:

1. Seed an approved synthetic opening with Cash `100,000`, Bank `1,000,000`,
   second Bank `200,000` and Mobile Money `300,000`.
2. As Agent, capture a customer deposit of `50,000` through the browser.
   Prepare a closing draft through the preserved API, enter non-cash closing
   observations in the browser, and submit the closing there.
3. Verify operational closure and closing status. Start/approve the next day's
   opening before reconciling yesterday; outstanding review did not introduce
   an opening gate.
4. Sign in normally as Admin. Open the closing and click **Reconcile closing**
   without statements. Observe the missing-evidence block and absence of an
   upload input on the closing page.
5. On the separate statement page, upload an actual CSV whose date differs from
   the selected date. Observe rejection. Upload the correct first Bank CSV.
6. Retry whole-closing reconciliation with the second Bank missing. Observe the
   specific ABSA missing-statement block; no partial report is recorded.
7. Upload the second Bank's correct-date CSV. Run reconciliation once. Observe
   overall Success and both wallet results, while the closing remains
   `PendingApproval`. The rerun button disappears.
8. As Supervisor, separately approve the closing. Verify the four next-day
   wallet balances remain `150,000`, `950,000`, `200,000`, `300,000`; vault remains
   `0`; the earlier closing has exactly one settlement. Verify separate pending
   notifications addressed to Admin and Supervisor with the closing link.
9. Submit a subsequent closing. Upload one Bank statement with a `10` difference
   and one agreeing statement. Run once and observe overall Failed with one
   successful and one failed wallet.
10. As Supervisor, confirm neither ordinary approval nor exception approval is
    available on that failed report.
11. As Admin, acknowledge failures but submit a short exception reason. Observe
    validation rejection. Navigate between closings within the Blazor circuit:
    the previous reason and acknowledgement clear rather than carrying over.
12. Enter a valid reason, acknowledge again, and exception-approve. Reload the
    page; observe approved closing, retained Failed report and recorded exception.
    Query PostgreSQL to confirm both per-wallet results and the separate decision.

The other four browser tests passed:

| Test | Observed result |
|---|---|
| `BankAccounts_FirstRegistration_ManagesWorkflowWithoutMovingMoney` | Registration/configuration and enrollment controls work without changing balances. |
| `BankAccounts_LinksAndSelection_PreserveAccountContextAndClearUploadDrafts` | Account/date links preserve context; switching selections clears stale file/confirmation state. |
| `BankAccounts_ManagementAndStatementLinks_RespectRoleAndBranchBoundaries` | Unauthorized and other-branch access remains restricted. |
| `CashCountBrowserTests.Closing_OffsettingWalletVariances_CanEnterExplanationAndSubmit` | Existing per-wallet discrepancy/explanation submission remains usable. |

These are automated browser checks, not a separate manual user-acceptance sign-off.

## Additional acceptance evidence

| Requirement | Executed evidence and outcome |
|---|---|
| One-day-only intake | Parser and service tests reject selected-date mismatches, multi-day rows/ranges, empty files, invalid formats and broken balance chains atomically. Date mismatch is also covered in-browser. |
| All Bank wallets, not Cash/Mobile | Unit/browser tests use two Banks plus Cash/Mobile; missing one Bank blocks the entire run. |
| Historical opening, not live balances | Service tests change live balances and pause an account after submission; captured obligations/calculation remain intact. Missing approved opening blocks both run and attempted exception approval. |
| Exact independent arithmetic | Agreeing, mixed, equal/opposite differences and internally consistent but offset statement anchors are tested. Banks never net each other's failures. |
| Immutable revisions and no reruns | Identical replay returns the same statement; reasoned replacement retains earlier revisions; used inputs reject replacement; repeated reconciliation returns the saved report. |
| Concurrent replacement/run | PostgreSQL race test verifies that the one saved report references exactly the accepted final revision, whichever command wins; no approval is implicitly created. |
| Concurrent run/approval | Two competing commands yield one report, two wallet results, one approval/audit/notification and no repeated money movement. |
| Independent approval controls | Tests reject self-approval and both ordinary/exception approval when non-bank balances disagree with recorded movements. Supervisor upload/run/exception attempts are denied. |
| Legacy transition | Draft and pre-cutover Pending legacy Bank closings require the whole report and settle exactly once. Agent still sees Pending status after operational closure. |
| Archive preservation | Populated migration retains source evidence, balances, approved status and correction-history foreign keys across schemas; writes/truncation and destructive rollback are rejected. |
| Existing operations | Concurrent capture/retry, closing submission, cash-wallet assignment, case reservations and retained client/API regressions pass. |
| Private evidence | Reviewer download succeeds; Agent and other-branch access fail. Legacy statement-upload API is rejected. A focused download rerun also verifies original CSV bytes and content type. |
| Operational next day | Actual browser/API workflow opens the next day before earlier reconciliation, then verifies later approval did not alter next-day funds. |

The new migration is forward-only: its `Down` refuses to discard evidence.
Any future deployment needs its own reviewed backup/cutover procedure; this task
did not apply it to a business database.

## Evidence files and defects resolved

Local test artifacts are ignored build output, not committed bank data. They can
be regenerated using the commands above:

- `tests\EastSeat.Agenti.UnitTests\TestResults\closing-reconciliation-regressions.trx`
- `tests\EastSeat.Agenti.IntegrationTests\TestResults\closing-reconciliation-postgres.trx`
- `tests\EastSeat.Agenti.IntegrationTests\TestResults\closing-reconciliation-evidence.trx`
- `tests\EastSeat.Agenti.E2ETests\TestResults\closing-reconciliation-browser.trx`
- `tests\EastSeat.Agenti.E2ETests\bin\Debug\net10.0\TestResults\closing-success.png`
- `tests\EastSeat.Agenti.E2ETests\bin\Debug\net10.0\TestResults\closing-exception.png`

Earlier failing iterations exposed and led to fixes for stale tracked session/
account versions in long-lived Blazor scopes, statement-page parameter/file
reset races, and test cleanup conflicting with archive guards. Browser assertions
were also corrected to tolerate rendered whitespace rather than falsely reporting
a missing successful report. The final runs above passed after those changes.

## PR review follow-up

Ten review findings on the pull request were fixed in code and re-validated.
Each row lists the behavior now enforced and the test that protects it.

| # | Finding | Fix | Protecting test |
|---|---|---|---|
| 1 | Modern Cash/Mobile-only closing could not be approved | Ordinary approval without a run/decision row; exception refused; session closes | `ClosingReconciliationTests.CashOnlyModernClosing_IsApprovedWithoutRunOrExceptionAndClosesSession`, `..._CannotBeSelfApproved` |
| 2 | Wallet adjustment touched archived review tables | Only advances the session register version | `WalletAdjustmentServiceTests.ApproveAdjustmentAsync_ModernSession_AdvancesRegisterWithoutTouchingArchivedReviews` |
| 3 | Previous-closing/bridge queries ignored settled-but-unapproved closings | Settled submitted closings count as the previous closing and bridge | `SessionSettlementTests.Opening_BridgesFromLegacySubmittedClosingOnlyOnceItsFloatIsSettled`, `CashCountServiceTests.GetPendingApprovalsAsync_OpeningComparesAgainstSubmittedPreviousClosingOnlyOnceSettled` |
| 4 | Stale cached status allowed editing a submitted legacy closing | Freeze guard on snapshot/settlement/pending-bank state | `CashCountServiceTests.SaveCashCountAsync_SubmittedClosingWithSnapshot_IsFrozenEvenWhenStatusIsStale` |
| 5 | Bank review detection ignored the approved opening | Closing must count every opening wallet exactly once | `CashCountServiceTests.SaveCashCountAsync_ClosingOmittingAnOpenedBankWallet_IsRejected` |
| 6 | Explained Cash/Mobile variance blocked approval | Submission raises one reviewable discrepancy; approved with the closing | `SessionSettlementTests.Closing_ExplainedCashVarianceCreatesDiscrepancyForReview`, `..._ExplainedBankOnlyVarianceDoesNotCreateDiscrepancy`, `ClosingReconciliationTests.ExplainedCashVariance_IsReviewedAndApprovedWithTheClosing` |
| 7 | Input hash included mutable wallet name/null account id | Hash uses stable identifiers only | `ClosingReconciliationTests.RenamingABankWalletAfterReconciliation_DoesNotInvalidateApproval` |
| 8 | Session closed on first closing, not when all agents submitted | Shared settlement-aware completion check | `SessionSettlementTests.Session_StaysOpenUntilEveryOpenedAgentHasSubmittedAClosing` |
| 9 | Legacy Cash/Mobile closings lost their Reject action | Review link only for bank-bearing or modern closings | Razor change without a dedicated test; `CashCountBrowserTests` still passes |
| 10 | No retry on serialization conflicts | Up to three fresh Serializable attempts, then a conflict | Covered by the existing legacy save tests; conflict injection is not unit-tested |

### Results after the review fixes

| Layer | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Affected unit/regression selection (same filter as above) | 210 | 0 | 18 |
| Full integration project (PostgreSQL/API) | 13 | 0 | 0 |
| Local browser selection | 5 | 0 | 0 |
| Web, MCP and Android (`net10.0-android`) builds | Success | 0 errors | - |

The whitespace check passed. The 18 skipped unit tests are the same
`VaultServiceTests` skips described above. The retry in finding 10 is not
exercised by an injected conflict; this is a documented gap. The entire unit
and E2E projects remain unclaimed.

## Explicit limitations

- Empty/no-activity Bank statements remain unsupported by the agreed scope.
  Missing evidence is not an exception-approval bypass.
- Arithmetic does not certify transaction legitimacy, exact cutoff time or CSV
  account ownership/completeness. Account/full-day confirmation is an attestation.
- No production deployment, data migration, load test, full-repository test pass,
  Android device/emulator exercise or interactive MCP-client session is claimed.
  Android and MCP were built; retained client/API contracts were covered as noted.
- Concurrency checks exercise selected interleavings, not an exhaustive stress
  test of every possible scheduler ordering.
