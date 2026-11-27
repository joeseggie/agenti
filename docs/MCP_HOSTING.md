# Authenticated MCP hosting

Agenti MCP is a separate .NET 10 ASP.NET Core application exposing stateless
Streamable HTTP at `/mcp`. The existing six reporting tools remain read-only.
**Stdio configurations no longer work.** No Entra login or OAuth authorization
server is provided; use an MCP client supporting a manually configured bearer header.

## Accounts and clients

Sign into the existing Agenti web application and open **Administration > MCP Access**
(`/mcp-access`). Active, non-deleted Admins and Supervisors can create tokens for
themselves. Agents cannot issue or use them. Admins can also view metadata and revoke
other users' tokens, but cannot issue on another user's behalf or retrieve secrets.

Give each client a descriptive name. Tokens expire after seven days by default;
the selectable lifetime is one through 30 days, with no automatic renewal.
Copy the secret once into the client's protected credential facility, then dismiss
it and clear the clipboard. Do not put tokens in source control, URLs, browser
storage, transcripts, screenshots, request logs or issue reports.

Configure the client with these values (the client's configuration format varies):

| Setting | Value |
| --- | --- |
| Transport | Streamable HTTP |
| Endpoint | `https://<mcp-app-hostname>/mcp` |
| Header name | `Authorization` |
| Header value | `Bearer <your MCP token>` |

This token does not authorize the Web application's banking APIs. Conversely,
Android JWTs, passwords, Basic authentication and Web cookies do not authenticate
MCP requests. OAuth-only clients are not supported in this release.

Admins can query all branches or supply a positive `branchId`. Supervisors are
restricted to their currently assigned branch; explicitly requesting another branch
returns a tool error, not an empty successful report.

Every request rechecks the database account, role, branch, lockout, expiry,
revocation and Identity security stamp. Password changes/resets invalidate old
tokens. Revocation is permanent. Other account eligibility follows current state:
reactivation may restore an otherwise valid token if its stamp is unchanged.
Already admitted queries are not retroactively cancelled.

Rotate by creating a replacement, updating the client and revoking the old token.
If creation succeeds but the secret is lost, refresh metadata, revoke that token
and create another. Retrying the same submission cannot issue a second credential.

## Configuration

Set secrets through protected App Service settings or an existing approved secret
store. Do not add a connection string to checked-in JSON or workflow output.

| MCP setting | Default / requirement |
| --- | --- |
| `AGENTI__ConnectionString` | Required dedicated read-only PostgreSQL connection |
| `AllowedHosts` | Required explicit hostnames separated by semicolons; never `*` |
| `AGENTI__MaxRows` | 1000; valid range 1-10000 |
| `AGENTI__CommandTimeoutSeconds` | 30; valid range 1-300 |
| `AGENTI__RequestsPerMinute` | 30 per user, shared across their tokens |
| `AGENTI__MaxConcurrentRequests` | 2 concurrent MCP HTTP requests per instance, no waiting queue |

The concurrency limit covers metadata/initialization as well as tool requests, so
at most two tools execute simultaneously. Limits are instance-local, not a
distributed quota. Clients must handle HTTP 429 and honor `Retry-After`; do not
automatically retry arbitrary operations without understanding their semantics.
The existing reporting tools are read-only.

The Web app optionally uses `McpAccess__ServerUrl=https://<mcp-hostname>/mcp` for
onboarding instructions. Process-wide MCP `UserRole`, `BranchId` and
`CanQueryAllBranches` settings have been removed; environment variables cannot
grant user permissions.

The host loads its configuration relative to the executable, not the shell's
working directory. It does not run Web setup, migrations or background services.
It does not trust forwarded host/IP headers, use cookie sharing, or require shared
Identity data-protection keys. Azure terminates TLS: **HTTPS-only must remain
enabled at App Service**, and only that trusted ingress may expose the process.
For other hosting environments, enforce HTTPS at the trusted reverse proxy.
Do not enable broad CORS. Requests carrying an untrusted browser Origin are denied.

`/health/live` is anonymous and returns no operational data. `/health/ready` checks
the required tables and SELECT privileges using a zero-row query; database/schema/
permission failures return a generic failure, without exposing connection details.
Missing/invalid credentials return 401; authenticated but ineligible roles or
Supervisors without a branch return 403.

## Least-privilege database setup

Perform these actions through the approved administrator/migration path, not MCP.
Deploy the additive `AddMcpAccessTokens` migration through the Web app first.
Review backup arrangements before any production schema rollout.

Create a **new dedicated login**, using `psql`'s interactive password prompt so
its password is not embedded in a script or command history:

```sql
CREATE ROLE agenti_mcp_readonly LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
\password agenti_mcp_readonly
ALTER ROLE agenti_mcp_readonly SET default_transaction_read_only = on;
```

Apply the versioned grants, with `ON_ERROR_STOP` enabled:

```powershell
psql -h <database-host> -U <approved-administrator> -d agenti_prod `
  -v ON_ERROR_STOP=1 -v database_name=agenti_prod -v mcp_role=agenti_mcp_readonly `
  -f .\scripts\azure\mcp-readonly-grants.sql
```

The script grants CONNECT, schema USAGE, SELECT on the eleven required reporting/
credential tables, and only ten required columns of `AspNetUsers`. It does not
grant access to password hashes, recovery/authenticator tokens, statement evidence,
writes, schema ownership or future tables. New tool/schema requirements must
explicitly update and review this grant list.

Review actual privileges and role memberships, including grants inherited from
PUBLIC; do not assume the role name or `default_transaction_read_only` is sufficient.
The integration suite checks real SELECT access and denied writes/persistent DDL
using this grant file in a disposable database. Never probe production by attempting
financial mutations. Review `\du`, `\dp`, column privileges and the network path
with the database owner before enabling clients.

Use verified TLS, for example these connection-string fields:
`Host=<database-host>;Database=agenti_prod;Username=agenti_mcp_readonly;Password=<secret>;SSL Mode=VerifyFull;Maximum Pool Size=10;Timeout=15`.
Keep the actual string private. Do not add `Trust Server Certificate=true`.
Approve the new app's outbound network access explicitly; prefer an existing
private route. Do not broadly open the database firewall to all Azure services.

## Azure rollout on the existing plan

Documented resource names are `agenti-rg`, Linux B1 `agenti-plan`, banking app
`agenti`, and database `agenti-pgserver` / `agenti_prod`. These are inputs to verify,
not a claim about live resource state.

1. Authenticate with the approved Azure subscription. Check the plan's OS, SKU,
   instance count, peak CPU/memory, database CPU/credits/connections, networking
   and deployment permissions. Protect banking headroom; do not resize automatically.
2. Deploy the Web change and confirm its additive token migration, then apply and
   verify the dedicated grants and network access.
3. Preview the narrowly scoped script, then approve configuration of the new app:

```powershell
.\scripts\azure\setup-mcp.ps1 -SubscriptionId <subscription-guid> -McpAppName <unique-mcp-app> -WhatIf
.\scripts\azure\setup-mcp.ps1 -SubscriptionId <subscription-guid> -McpAppName <unique-mcp-app> -CapacityApproved
```

The script reuses the plan without scaling it and refuses an existing untagged
target. It configures HTTPS-only, TLS 1.2+, Always On, explicit hosts, readiness,
.NET 10 and the MCP entry point. It does not deploy code, set credentials, modify
database firewalls/passwords or touch the banking app.
**Do not rerun `infrastructure.yml` or the broad `setup-infrastructure.ps1`**:
those paths are not the MCP addition path and may change existing database credentials.

4. Set `AGENTI__ConnectionString` privately on the MCP app and optionally
   `McpAccess__ServerUrl` on the Web app.
5. Review production environment protection and the deployment identity. CI
   publishes separate `webapp` and `mcp-webapp` artifacts. Set repository variable
   `AZURE_MCP_WEBAPP_NAME` to the new app. Only after schema/grants/configuration
   approval, set repository variable `MCP_DEPLOY_ENABLED=true`.
   The MCP deployment runs after the Web deployment and its test prerequisites,
   verifies the tagged shared-plan target, and checks readiness and anonymous 401.
   Existing Azure deployment identity is reused; this is not Entra-based MCP login.
6. Verify initialize/list/call with an Admin token and a Supervisor token, including
   an explicit denied cross-branch query. Monitor banking latency, plan memory/CPU,
   database connections and 429/5xx rates before broader onboarding.

The deployment workflow's health checks are necessary but not proof of authenticated
client interoperability or adequate capacity. Actual production access, capacity and
client onboarding are separate release gates.

### Budget

Sharing an unchanged existing B1 plan adds **$0/month in App Service compute**.
Allow approximately **$0-$5/month** for modest incremental logging/ancillary usage;
database load, transfer, tax and any required scaling are additional and unmeasured.
The prior UAE North public retail estimate was $16.06/month for Linux B1
($0.022/hour x 730 hours), or $32.12/month for B2. A B1-to-B2 increase would therefore
add about $16.06/month; it is not automatically approved. Confirm current prices
and the subscription's actual offer before committing spend.

## Rollback and operational safety

Disable `MCP_DEPLOY_ENABLED`, stop or revert **only the MCP app**, and revoke
affected credentials in the Web app. Do not stop/delete the shared App Service
plan or database. Keep additive token tables and audit history; do not reverse
the migration destructively. Do not downgrade to the unauthenticated stdio host
as an HTTP rollback.

Keep request/response bodies, bearer headers and query results out of production
telemetry. Current logging defaults to Warning. There are no database usage/last-used
writes from MCP. Investigate 401 (credential/account), 403 (permissions), 429
(request/concurrency limits), and readiness/5xx failures separately.

## Local validation

Use synthetic data and the existing test projects:

```powershell
dotnet test .\tests\EastSeat.Agenti.UnitTests --filter "FullyQualifiedName~McpAccessServiceTests|FullyQualifiedName~AuthEndpointsTests|FullyQualifiedName~UserServiceTests"
dotnet test .\tests\EastSeat.Agenti.IntegrationTests --filter "FullyQualifiedName~McpHttpTests"
dotnet test .\tests\EastSeat.Agenti.E2ETests --filter "FullyQualifiedName~McpAccessBrowserTests"
dotnet publish .\EastSeat.Agenti.Mcp -c Release -o <local-output-directory>
```

PostgreSQL tests use disposable Testcontainers; browser tests use the repository's
Playwright setup. Browser artifacts are optional via `AGENTI_MCP_SCREENSHOT_DIR`
and are captured only after the one-time credential is dismissed. No trace/video
or secret-bearing screenshots should be enabled. There is no pre-change screenshot
baseline for this new page.
