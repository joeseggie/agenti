# Agenti – Azure Logs: Access, View, and Query Guide

This guide explains how to access, view, and query the logs produced by the Agenti application in the Azure Portal using Application Insights and Log Analytics.

## Table of Contents

- [How Agenti Logs Work](#how-agenti-logs-work)
- [Accessing the Logs in Azure Portal](#accessing-the-logs-in-azure-portal)
- [Log Tables Reference](#log-tables-reference)
- [Viewing Logs – Quick Start](#viewing-logs--quick-start)
- [KQL Queries by Feature](#kql-queries-by-feature)
  - [Application Traces](#application-traces)
  - [Vault Operations](#vault-operations)
  - [Cash Count & Session Operations](#cash-count--session-operations)
  - [Background Services](#background-services)
  - [HTTP Requests & API](#http-requests--api)
  - [Exceptions & Errors](#exceptions--errors)
  - [Database (SQL) Performance](#database-sql-performance)
  - [Authentication & Login](#authentication--login)
  - [User Activity](#user-activity)
- [Live Metrics Stream](#live-metrics-stream)
- [Creating a Custom Dashboard](#creating-a-custom-dashboard)
- [Useful Tips](#useful-tips)

---

## How Agenti Logs Work

Agenti writes logs through two mechanisms that both land in Application Insights:

| Mechanism | Source | Azure Table |
|---|---|---|
| **Serilog** (`ILogger<T>`) | All service classes, `Program.cs`, background services | `traces` |
| **TelemetryClient** (`TrackEvent`) | Vault, CashCount, CashSession services, background services | `customEvents` |
| **TelemetryClient** (`TrackMetric`) | Background services | `customMetrics` |
| **ASP.NET Core middleware** | Every HTTP request (automatic) | `requests` |
| **EF Core dependency tracking** | Every SQL query (automatic) | `dependencies` |
| **Unhandled exceptions** | Global exception handler (automatic) | `exceptions` |

Logging is **only active when** `ApplicationInsights:ConnectionString` is set in configuration. In local development the connection string is empty, so only console output is produced.

---

## Accessing the Logs in Azure Portal

### Step 1 – Open Application Insights

1. Sign in to the [Azure Portal](https://portal.azure.com).
2. In the search bar at the top, type **Application Insights** and select the service.
3. Click the Application Insights resource named **agenti-insights** (or the name you chose during setup).

### Step 2 – Open the Logs blade

1. In the left-hand menu under **Monitoring**, click **Logs**.
2. A query editor opens. Close the example query pop-up if it appears.
3. The left panel shows the available tables (traces, requests, exceptions, customEvents, etc.).

> **Tip:** Queries here use [Kusto Query Language (KQL)](https://learn.microsoft.com/azure/data-explorer/kusto/query/). Every query example in this guide can be pasted directly into this editor.

### Step 3 – Set a time range

Use the **Time range** picker at the top of the query editor (default is *Last 24 hours*). You can choose:
- Preset ranges: Last 30 minutes / 1 hour / 4 hours / 24 hours / 7 days / 30 days
- Custom range: specify exact start and end timestamps

---

## Log Tables Reference

| Table | Contents |
|---|---|
| `traces` | All structured log messages from Serilog/ILogger |
| `customEvents` | Domain events explicitly tracked with `TelemetryClient.TrackEvent` |
| `customMetrics` | Numeric metrics tracked with `TelemetryClient.TrackMetric` |
| `requests` | Every HTTP request handled by the application |
| `dependencies` | Outgoing calls: SQL queries, external HTTP calls |
| `exceptions` | Unhandled and logged exceptions |
| `performanceCounters` | CPU, memory, request rate counters |

---

## Viewing Logs – Quick Start

Paste any of the following queries into the **Logs** editor and click **Run**.

### All recent log messages (last hour)

```kql
traces
| where timestamp > ago(1h)
| project timestamp, severityLevel, message, customDimensions
| order by timestamp desc
```

### All recent custom events (last hour)

```kql
customEvents
| where timestamp > ago(1h)
| project timestamp, name, customDimensions
| order by timestamp desc
```

### All recent errors and warnings (last 24 hours)

```kql
traces
| where timestamp > ago(24h)
| where severityLevel >= 2  // 2=Warning, 3=Error, 4=Critical
| project timestamp, severityLevel, message, customDimensions
| order by timestamp desc
```

---

## KQL Queries by Feature

### Application Traces

These are Serilog log entries written by `ILogger<T>`. The `customDimensions` column contains structured properties.

#### Startup and migration messages

```kql
traces
| where timestamp > ago(1h)
| where message has_any ("Starting Agenti", "migrations", "Setup")
| project timestamp, message, customDimensions
| order by timestamp asc
```

#### Setup wizard activity

```kql
traces
| where timestamp > ago(7d)
| where message has_any ("Setup", "admin", "branch", "cleanup")
| project timestamp, message, customDimensions
| order by timestamp desc
```

#### Warning-level messages

```kql
traces
| where timestamp > ago(24h)
| where severityLevel == 2
| project timestamp, message, customDimensions
| order by timestamp desc
```

#### Error-level messages

```kql
traces
| where timestamp > ago(24h)
| where severityLevel >= 3
| project timestamp, severityLevel, message, customDimensions
| order by timestamp desc
```

---

### Vault Operations

These are custom events emitted by `VaultService` and `CashCountService`.

#### All vault operations (last 24 hours)

```kql
customEvents
| where timestamp > ago(24h)
| where name in (
    "vault_withdrawal_completed",
    "vault_deposit_completed",
    "vault_adjustment_requested",
    "vault_adjustment_approved",
    "vault_adjustment_rejected"
  )
| project
    timestamp,
    name,
    BranchId     = tostring(customDimensions["BranchId"]),
    Amount       = tostring(customDimensions["Amount"]),
    UserId       = tostring(customDimensions["UserId"]),
    TransactionId = tostring(customDimensions["TransactionId"])
| order by timestamp desc
```

#### Vault withdrawals (cash session openings)

```kql
customEvents
| where timestamp > ago(7d)
| where name == "vault_withdrawal_completed"
| project
    timestamp,
    BranchId     = tostring(customDimensions["BranchId"]),
    SessionId    = tostring(customDimensions["SessionId"]),
    Amount       = todouble(customDimensions["Amount"]),
    UserId       = tostring(customDimensions["UserId"]),
    TransactionId = tostring(customDimensions["TransactionId"])
| order by timestamp desc
```

#### Vault deposits (cash session closings)

```kql
customEvents
| where timestamp > ago(7d)
| where name == "vault_deposit_completed"
| project
    timestamp,
    BranchId     = tostring(customDimensions["BranchId"]),
    SessionId    = tostring(customDimensions["SessionId"]),
    Amount       = todouble(customDimensions["Amount"]),
    UserId       = tostring(customDimensions["UserId"]),
    TransactionId = tostring(customDimensions["TransactionId"])
| order by timestamp desc
```

#### Pending vault adjustment requests

```kql
customEvents
| where timestamp > ago(7d)
| where name == "vault_adjustment_requested"
| project
    timestamp,
    BranchId      = tostring(customDimensions["BranchId"]),
    Amount        = todouble(customDimensions["Amount"]),
    Type          = tostring(customDimensions["Type"]),
    RequestedBy   = tostring(customDimensions["UserId"]),
    TransactionId = tostring(customDimensions["TransactionId"]),
    ExpiresAt     = tostring(customDimensions["ExpiresAt"])
| order by timestamp desc
```

#### Approved vault adjustments

```kql
customEvents
| where timestamp > ago(30d)
| where name == "vault_adjustment_approved"
| project
    timestamp,
    TransactionId = tostring(customDimensions["TransactionId"]),
    Amount        = todouble(customDimensions["Amount"]),
    Type          = tostring(customDimensions["Type"]),
    ApprovedBy    = tostring(customDimensions["ApprovedBy"]),
    CreatedBy     = tostring(customDimensions["CreatedBy"]),
    BalanceAfter  = todouble(customDimensions["BalanceAfter"])
| order by timestamp desc
```

#### Rejected vault adjustments

```kql
customEvents
| where timestamp > ago(30d)
| where name == "vault_adjustment_rejected"
| project
    timestamp,
    TransactionId = tostring(customDimensions["TransactionId"]),
    Amount        = todouble(customDimensions["Amount"]),
    Type          = tostring(customDimensions["Type"]),
    RejectedBy    = tostring(customDimensions["RejectedBy"]),
    CreatedBy     = tostring(customDimensions["CreatedBy"])
| order by timestamp desc
```

#### Daily vault movement summary

```kql
customEvents
| where timestamp > ago(30d)
| where name in ("vault_withdrawal_completed", "vault_deposit_completed")
| summarize
    TotalWithdrawals = countif(name == "vault_withdrawal_completed"),
    TotalDeposits    = countif(name == "vault_deposit_completed"),
    WithdrawnAmount  = sumif(todouble(customDimensions["Amount"]), name == "vault_withdrawal_completed"),
    DepositedAmount  = sumif(todouble(customDimensions["Amount"]), name == "vault_deposit_completed")
    by bin(timestamp, 1d)
| order by timestamp desc
```

#### Suspicious adjustment volume (more than 3 requests in 10 minutes)

```kql
customEvents
| where timestamp > ago(1d)
| where name == "vault_adjustment_requested"
| summarize count() by bin(timestamp, 10m)
| where count_ > 3
| order by timestamp desc
```

---

### Cash Count & Session Operations

#### All cash count submissions (last 7 days)

```kql
customEvents
| where timestamp > ago(7d)
| where name == "cash_count_submitted"
| project
    timestamp,
    Type        = tostring(customDimensions["Type"]),
    AgentId     = tostring(customDimensions["AgentId"]),
    SessionId   = tostring(customDimensions["SessionId"]),
    TotalAmount = todouble(customDimensions["TotalAmount"]),
    WalletCount = toint(customDimensions["WalletCount"])
| order by timestamp desc
```

#### Opening counts only

```kql
customEvents
| where timestamp > ago(7d)
| where name == "cash_count_submitted"
    and tostring(customDimensions["Type"]) == "Opening"
| project
    timestamp,
    AgentId     = tostring(customDimensions["AgentId"]),
    SessionId   = tostring(customDimensions["SessionId"]),
    TotalAmount = todouble(customDimensions["TotalAmount"])
| order by timestamp desc
```

#### Closing counts only

```kql
customEvents
| where timestamp > ago(7d)
| where name == "cash_count_submitted"
    and tostring(customDimensions["Type"]) == "Closing"
| project
    timestamp,
    AgentId     = tostring(customDimensions["AgentId"]),
    SessionId   = tostring(customDimensions["SessionId"]),
    TotalAmount = todouble(customDimensions["TotalAmount"])
| order by timestamp desc
```

#### Closed sessions with duration

```kql
customEvents
| where timestamp > ago(7d)
| where name == "session_closed"
| project
    timestamp,
    SessionId   = tostring(customDimensions["SessionId"]),
    AgentId     = tostring(customDimensions["AgentId"]),
    SessionDate = tostring(customDimensions["SessionDate"]),
    Duration    = tostring(customDimensions["Duration"])
| order by timestamp desc
```

#### Sessions per agent per day

```kql
customEvents
| where timestamp > ago(30d)
| where name == "session_closed"
| summarize
    SessionCount = count()
    by AgentId = tostring(customDimensions["AgentId"]),
       SessionDate = tostring(customDimensions["SessionDate"])
| order by SessionDate desc, SessionCount desc
```

---

### Background Services

#### Vault expiration checks (when transactions were expired)

```kql
customEvents
| where timestamp > ago(7d)
| where name == "vault_expiration_check"
| project
    timestamp,
    ExpiredCount = toint(customDimensions["ExpiredCount"])
| order by timestamp desc
```

#### Vault expiration metric over time

```kql
customMetrics
| where timestamp > ago(7d)
| where name == "vault_transactions_expired"
| summarize TotalExpired = sum(value) by bin(timestamp, 1d)
| order by timestamp desc
```

#### Audit log cleanup events

```kql
customEvents
| where timestamp > ago(90d)
| where name == "audit_cleanup_completed"
| project
    timestamp,
    RemovedCount = toint(customDimensions["RemovedCount"]),
    CutoffDate   = tostring(customDimensions["CutoffDate"])
| order by timestamp desc
```

#### Audit cleanup metric over time

```kql
customMetrics
| where timestamp > ago(90d)
| where name == "audit_logs_cleaned"
| summarize TotalCleaned = sum(value) by bin(timestamp, 7d)
| order by timestamp desc
```

#### All background service log messages

```kql
traces
| where timestamp > ago(24h)
| where customDimensions["SourceContext"] has_any (
    "VaultExpirationService",
    "UserAuditCleanupService"
  )
| project timestamp, severityLevel, message, customDimensions
| order by timestamp desc
```

---

### HTTP Requests & API

These come from ASP.NET Core's automatic request tracking.

#### All requests in the last hour

```kql
requests
| where timestamp > ago(1h)
| project timestamp, name, url, resultCode, duration, success
| order by timestamp desc
```

#### Failed requests (non-2xx status)

```kql
requests
| where timestamp > ago(24h)
| where success == false
| project timestamp, name, url, resultCode, duration
| order by timestamp desc
```

#### Slowest requests (top 20)

```kql
requests
| where timestamp > ago(24h)
| top 20 by duration desc
| project timestamp, name, url, resultCode, duration
```

#### Mobile API requests only (`/api/*`)

```kql
requests
| where timestamp > ago(24h)
| where url has "/api/"
| project timestamp, name, url, resultCode, duration, success
| order by timestamp desc
```

#### Request volume per hour

```kql
requests
| where timestamp > ago(7d)
| summarize RequestCount = count(), FailureCount = countif(success == false)
    by bin(timestamp, 1h)
| order by timestamp desc
```

#### Login attempts (API auth endpoint)

```kql
requests
| where timestamp > ago(24h)
| where url has "/api/auth"
| project timestamp, url, resultCode, duration, success
| order by timestamp desc
```

---

### Exceptions & Errors

#### All exceptions in the last 24 hours

```kql
exceptions
| where timestamp > ago(24h)
| project
    timestamp,
    type,
    outerMessage    = outerMessage,
    innermostMessage = innermostMessage,
    operation_Name,
    assembly
| order by timestamp desc
```

#### Exceptions grouped by type

```kql
exceptions
| where timestamp > ago(7d)
| summarize Count = count() by type
| order by Count desc
```

#### Vault-related exceptions

```kql
exceptions
| where timestamp > ago(7d)
| where operation_Name has_any ("Vault", "vault")
    or outerMessage has_any ("vault", "adjustment", "balance")
| project timestamp, type, outerMessage, innermostMessage, operation_Name
| order by timestamp desc
```

#### Database exceptions

```kql
exceptions
| where timestamp > ago(7d)
| where type has_any ("Postgres", "Npgsql", "DbUpdate", "SqlException")
| project timestamp, type, outerMessage, innermostMessage
| order by timestamp desc
```

---

### Database (SQL) Performance

These come from EF Core dependency tracking.

#### All SQL queries (last hour)

```kql
dependencies
| where timestamp > ago(1h)
| where type == "SQL"
| project timestamp, name, duration, success, resultCode, data
| order by timestamp desc
```

#### Slow SQL queries (over 500 ms)

```kql
dependencies
| where timestamp > ago(24h)
| where type == "SQL"
| where duration > 500
| project timestamp, name, duration, success, data
| order by duration desc
| take 20
```

#### Failed SQL queries

```kql
dependencies
| where timestamp > ago(24h)
| where type == "SQL"
| where success == false
| project timestamp, name, duration, resultCode, data
| order by timestamp desc
```

#### Average SQL query duration by operation

```kql
dependencies
| where timestamp > ago(24h)
| where type == "SQL"
| summarize AvgDuration = avg(duration), MaxDuration = max(duration), Count = count()
    by name
| order by AvgDuration desc
| take 20
```

---

### Authentication & Login

These are custom events emitted by `LoginTelemetryService` for both Blazor web and API sign-in flows.

#### All login events (success + failure, last 24 hours)

```kql
customEvents
| where timestamp > ago(24h)
| where name in ("login_succeeded", "login_failed")
| project
    timestamp,
    name,
    Email       = tostring(customDimensions["Email"]),
    LoginMethod = tostring(customDimensions["LoginMethod"]),
    IpAddress   = tostring(customDimensions["IpAddress"]),
    UserAgent   = tostring(customDimensions["UserAgent"])
| order by timestamp desc
```

#### Successful logins with user details

```kql
customEvents
| where timestamp > ago(7d)
| where name == "login_succeeded"
| project
    timestamp,
    UserId      = tostring(customDimensions["UserId"]),
    Email       = tostring(customDimensions["Email"]),
    Role        = tostring(customDimensions["Role"]),
    BranchId    = tostring(customDimensions["BranchId"]),
    LoginMethod = tostring(customDimensions["LoginMethod"]),
    IpAddress   = tostring(customDimensions["IpAddress"])
| order by timestamp desc
```

#### Failed logins by failure reason

```kql
customEvents
| where timestamp > ago(7d)
| where name == "login_failed"
| summarize Count = count() by
    FailureReason = tostring(customDimensions["FailureReason"])
| order by Count desc
```

#### Failed logins grouped by IP address (brute-force detection)

```kql
customEvents
| where timestamp > ago(24h)
| where name == "login_failed"
| summarize
    FailureCount = count(),
    Emails = make_set(tostring(customDimensions["Email"]))
    by IpAddress = tostring(customDimensions["IpAddress"])
| where FailureCount > 5
| order by FailureCount desc
```

#### Suspicious: multiple failed logins for the same email in 10 minutes

```kql
customEvents
| where timestamp > ago(1d)
| where name == "login_failed"
| summarize count() by
    bin(timestamp, 10m),
    Email = tostring(customDimensions["Email"])
| where count_ > 3
| order by timestamp desc
```

#### Login latency percentiles (p50, p95, p99)

```kql
customMetrics
| where timestamp > ago(24h)
| where name == "login_duration_ms"
| summarize
    P50 = percentile(value, 50),
    P95 = percentile(value, 95),
    P99 = percentile(value, 99),
    Avg = avg(value),
    Count = count()
    by bin(timestamp, 1h)
| order by timestamp desc
```

#### Logins by method (Blazor Web vs API)

```kql
customEvents
| where timestamp > ago(7d)
| where name in ("login_succeeded", "login_failed")
| summarize
    SuccessCount = countif(name == "login_succeeded"),
    FailureCount = countif(name == "login_failed")
    by LoginMethod = tostring(customDimensions["LoginMethod"])
```

#### JWT tokens issued per day

```kql
customEvents
| where timestamp > ago(30d)
| where name == "jwt_token_issued"
| summarize TokenCount = count() by bin(timestamp, 1d)
| order by timestamp desc
```

#### Login activity by branch

```kql
customEvents
| where timestamp > ago(7d)
| where name == "login_succeeded"
| summarize LoginCount = count() by
    BranchId = tostring(customDimensions["BranchId"]),
    LoginMethod = tostring(customDimensions["LoginMethod"])
| order by LoginCount desc
```

---

### User Activity

#### Login warning messages (inactive accounts)

```kql
traces
| where timestamp > ago(7d)
| where message has "Login attempt for inactive account"
| project timestamp, message, customDimensions
| order by timestamp desc
```

#### Setup redirect events (unauthenticated access before setup)

```kql
traces
| where timestamp > ago(7d)
| where message has "Setup incomplete"
| project timestamp, message, customDimensions
| order by timestamp desc
```

#### Operations performed by a specific user ID

Replace `<user-id>` with the actual user ID string.

```kql
customEvents
| where timestamp > ago(30d)
| where customDimensions["UserId"] == "<user-id>"
    or customDimensions["ApprovedBy"] == "<user-id>"
    or customDimensions["RejectedBy"] == "<user-id>"
    or customDimensions["CreatedBy"] == "<user-id>"
| project timestamp, name, customDimensions
| order by timestamp desc
```

#### All vault operations grouped by user

```kql
customEvents
| where timestamp > ago(30d)
| where name in (
    "vault_withdrawal_completed",
    "vault_deposit_completed",
    "vault_adjustment_requested",
    "vault_adjustment_approved",
    "vault_adjustment_rejected"
  )
| summarize OperationCount = count() by
    UserId = coalesce(
        tostring(customDimensions["UserId"]),
        tostring(customDimensions["ApprovedBy"]),
        tostring(customDimensions["RejectedBy"])
    ),
    name
| order by OperationCount desc
```

---

## Live Metrics Stream

For real-time monitoring during an incident or deployment:

1. In your Application Insights resource, click **Live Metrics** (under **Investigate** in the left menu).
2. You will see:
   - **Incoming Requests** – requests per second and failure rate
   - **Outgoing Requests** – dependency calls per second
   - **Overall Health** – CPU and memory
   - **Sample telemetry** – live stream of individual log entries, requests, and exceptions as they happen

> Live Metrics incur minimal cost because data is streamed but not permanently ingested.

---

## Creating a Custom Dashboard

To pin queries as tiles on an Azure Dashboard:

1. Run a query in the **Logs** blade.
2. Click **Pin to dashboard** (the push-pin icon above the results).
3. Choose an existing dashboard or create a new one named **Agenti Operations**.

**Recommended tiles:**

| Tile | Query hint |
|---|---|
| Vault operations today | `customEvents` filtered to `vault_*` events, `count()` grouped by `name` |
| Request failure rate (24h) | `requests` success rate over time |
| Slow SQL queries | `dependencies` where `duration > 500` |
| Exception count by type (7d) | `exceptions` grouped by `type` |
| Background service activity | `customEvents` for `vault_expiration_check` and `audit_cleanup_completed` |
| Active sessions per agent | `customEvents` for `cash_count_submitted` with `Type == "Opening"`, count by AgentId |

---

## Useful Tips

- **Data delay:** Application Insights has a 1–5 minute ingestion delay. Use **Live Metrics** for real-time data.
- **Case sensitivity:** KQL string comparisons are case-sensitive by default. Use `=~` for case-insensitive matches, e.g. `where name =~ "Vault_Withdrawal_Completed"`.
- **Export results:** Click **Export** above the query results to download as CSV or open in Excel.
- **Save queries:** Click **Save** above the editor to save a query for reuse. Saved queries appear under **Queries** in the left panel.
- **Share queries:** Use the **Copy link** button to share a query URL with teammates.
- **Time zones:** All timestamps in Application Insights are stored in UTC. Use `| extend localTime = timestamp + 3h` to convert to EAT (East Africa Time, UTC+3).

### EAT time conversion example

```kql
customEvents
| where timestamp > ago(24h)
| where name in ("vault_withdrawal_completed", "vault_deposit_completed")
| extend localTime = timestamp + 3h
| project localTime, name, customDimensions
| order by localTime desc
```

---

## Related Documentation

- [APPLICATION_INSIGHTS_SETUP.md](APPLICATION_INSIGHTS_SETUP.md) — How to create and configure the Application Insights resource and deploy the app
- [AZURE_DEPLOYMENT_SETUP.md](AZURE_DEPLOYMENT_SETUP.md) — Full Azure deployment guide
- [KQL Reference](https://learn.microsoft.com/azure/data-explorer/kusto/query/) — Official Kusto Query Language documentation
- [Application Insights Overview](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview) — Azure Monitor documentation
