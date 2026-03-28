# Agenti Database Usage Analysis

Analyze the Agenti production database to understand application usage patterns and suggest improvements.

## Prerequisites

Before running queries, you MUST:

1. **Get the current public IP** by running: `curl -s ifconfig.me`
2. **Get the Azure PostgreSQL connection details** from App Service config:
   ```bash
   az webapp config connection-string list --name agenti --resource-group agenti-rg -o json
   ```
3. **Add a temporary firewall rule** for the current IP:
   ```bash
   az postgres flexible-server firewall-rule create \
     --resource-group agenti-rg \
     --name <server-name-from-connection-string> \
     --rule-name TempAnalysisAccess \
     --start-ip-address <current-ip> \
     --end-ip-address <current-ip>
   ```
4. **Run all analysis queries** (see sections below)
5. **ALWAYS remove the firewall rule** when done:
   ```bash
   az postgres flexible-server firewall-rule delete \
     --resource-group agenti-rg \
     --name <server-name-from-connection-string> \
     --rule-name TempAnalysisAccess \
     --yes
   ```

## How to Run Queries

Use the local Docker PostgreSQL client to connect to the Azure server:
```bash
docker exec agenti-postgres psql "<connection-string-from-step-2>" -c "<SQL>"
```

## Analysis Queries

Run ALL of the following query groups. Present results in markdown tables with clear headings.

### 1. User & Role Overview

```sql
-- Active users with roles and agent status
SELECT
  u."UserName",
  u."Email",
  u."FirstName" || ' ' || u."LastName" AS "FullName",
  u."Role" AS "UserRoleEnum",
  r."Name" AS "IdentityRole",
  u."IsActive",
  u."EmailConfirmed",
  u."PhoneNumber",
  a."Code" AS "AgentCode",
  a."IsActive" AS "AgentActive",
  b."Name" AS "BranchName",
  u."CreatedAt",
  u."UpdatedAt"
FROM "AspNetUsers" u
LEFT JOIN "AspNetUserRoles" ur ON u."Id" = ur."UserId"
LEFT JOIN "AspNetRoles" r ON ur."RoleId" = r."Id"
LEFT JOIN "Agents" a ON a."UserId" = u."Id"
LEFT JOIN "Branches" b ON a."BranchId" = b."Id"
ORDER BY u."UserName";
```

### 2. Branch & Vault Summary

```sql
-- Branch and vault balances
SELECT
  b."Id" AS "BranchId",
  b."Name" AS "BranchName",
  v."Id" AS "VaultId",
  v."CurrentBalance",
  b."CreatedAt" AS "BranchCreated",
  (SELECT COUNT(*) FROM "Agents" a WHERE a."BranchId" = b."Id") AS "AgentCount"
FROM "Branches" b
LEFT JOIN "Vaults" v ON v."BranchId" = b."Id"
ORDER BY b."Name";
```

### 3. Cash Session Activity

```sql
-- Cash session summary by agent and status
SELECT
  a."Code" AS "AgentCode",
  cs."Status",
  COUNT(*) AS "SessionCount",
  MIN(cs."SessionDate") AS "FirstSession",
  MAX(cs."SessionDate") AS "LastSession",
  COUNT(CASE WHEN cs."ClosedAt" IS NOT NULL THEN 1 END) AS "ClosedSessions",
  AVG(EXTRACT(EPOCH FROM (cs."ClosedAt" - cs."OpenedAt")) / 3600)::numeric(10,2) AS "AvgDurationHours"
FROM "CashSessions" cs
JOIN "Agents" a ON a."Id" = cs."AgentId"
GROUP BY a."Code", cs."Status"
ORDER BY a."Code", cs."Status";
```

```sql
-- Daily session activity trend (last 30 days)
SELECT
  cs."SessionDate",
  COUNT(*) AS "TotalSessions",
  COUNT(CASE WHEN cs."Status" = 'Open' THEN 1 END) AS "Open",
  COUNT(CASE WHEN cs."Status" = 'Closed' THEN 1 END) AS "Closed",
  COUNT(CASE WHEN cs."Status" = 'Completed' THEN 1 END) AS "Completed",
  COUNT(CASE WHEN cs."Status" = 'DiscrepancyUnderReview' THEN 1 END) AS "Discrepancies",
  COUNT(CASE WHEN cs."Status" = 'Blocked' THEN 1 END) AS "Blocked"
FROM "CashSessions" cs
WHERE cs."SessionDate" >= CURRENT_DATE - INTERVAL '30 days'
GROUP BY cs."SessionDate"
ORDER BY cs."SessionDate" DESC;
```

### 4. Vault Transaction Analysis

```sql
-- Vault transaction summary by type and status
SELECT
  vt."Type",
  vt."Status",
  COUNT(*) AS "Count",
  SUM(vt."Amount") AS "TotalAmount",
  AVG(vt."Amount")::numeric(18,2) AS "AvgAmount",
  MIN(vt."Amount") AS "MinAmount",
  MAX(vt."Amount") AS "MaxAmount"
FROM "VaultTransactions" vt
GROUP BY vt."Type", vt."Status"
ORDER BY vt."Type", vt."Status";
```

```sql
-- Pending/expired vault transactions (potential issues)
SELECT
  vt."Id",
  vt."Type",
  vt."Status",
  vt."Amount",
  vt."CreatedAt",
  vt."ExpiresAt",
  vt."Notes",
  u."UserName" AS "CreatedBy"
FROM "VaultTransactions" vt
JOIN "AspNetUsers" u ON u."Id" = vt."CreatedByUserId"
WHERE vt."Status" IN ('Pending', 'Expired')
ORDER BY vt."CreatedAt" DESC
LIMIT 20;
```

### 5. Transaction Flow Analysis

```sql
-- Transaction volume by type
SELECT
  t."Type",
  COUNT(*) AS "Count",
  SUM(t."Amount") AS "TotalAmount",
  AVG(t."Amount")::numeric(18,2) AS "AvgAmount",
  COUNT(CASE WHEN t."ReversedAt" IS NOT NULL THEN 1 END) AS "ReversedCount"
FROM "Transactions" t
GROUP BY t."Type"
ORDER BY "Count" DESC;
```

```sql
-- Transaction volume by day (last 30 days)
SELECT
  DATE(t."CreatedAt") AS "Date",
  COUNT(*) AS "TransactionCount",
  SUM(t."Amount") AS "TotalAmount"
FROM "Transactions" t
WHERE t."CreatedAt" >= CURRENT_DATE - INTERVAL '30 days'
GROUP BY DATE(t."CreatedAt")
ORDER BY "Date" DESC;
```

### 6. Wallet Utilization

```sql
-- Wallet balances and activity per agent
SELECT
  a."Code" AS "AgentCode",
  wt."Name" AS "WalletType",
  w."Name" AS "WalletName",
  w."Balance",
  w."Currency",
  w."IsActive",
  (SELECT COUNT(*) FROM "Transactions" t WHERE t."FromWalletId" = w."Id" OR t."ToWalletId" = w."Id") AS "TransactionCount"
FROM "Wallets" w
JOIN "Agents" a ON a."Id" = w."AgentId"
JOIN "WalletTypes" wt ON wt."Id" = w."WalletTypeId"
ORDER BY a."Code", wt."Name";
```

### 7. Cash Count Analysis

```sql
-- Cash count summary
SELECT
  a."Code" AS "AgentCode",
  COUNT(*) AS "TotalCounts",
  COUNT(CASE WHEN cc."IsOpening" = true THEN 1 END) AS "OpeningCounts",
  COUNT(CASE WHEN cc."IsOpening" = false THEN 1 END) AS "ClosingCounts",
  SUM(cc."TotalAmount") AS "TotalAmountCounted",
  AVG(cc."TotalAmount")::numeric(18,2) AS "AvgCountAmount"
FROM "CashCounts" cc
JOIN "CashSessions" cs ON cs."Id" = cc."CashSessionId"
JOIN "Agents" a ON a."Id" = cs."AgentId"
GROUP BY a."Code"
ORDER BY a."Code";
```

### 8. Discrepancy Analysis

```sql
-- Discrepancy overview
SELECT
  d."Status",
  COUNT(*) AS "Count",
  SUM(ABS(d."Variance")) AS "TotalVariance",
  AVG(ABS(d."Variance"))::numeric(18,2) AS "AvgVariance",
  MAX(ABS(d."Variance")) AS "MaxVariance"
FROM "Discrepancies" d
GROUP BY d."Status"
ORDER BY d."Status";
```

```sql
-- Discrepancy details (recent, with agent info)
SELECT
  d."Id",
  a."Code" AS "AgentCode",
  d."Status",
  d."ExpectedAmount",
  d."ActualAmount",
  d."Variance",
  d."Reason",
  d."Explanation",
  d."CreatedAt"
FROM "Discrepancies" d
JOIN "CashSessions" cs ON cs."Id" = d."CashSessionId"
JOIN "Agents" a ON a."Id" = cs."AgentId"
ORDER BY d."CreatedAt" DESC
LIMIT 20;
```

### 9. Audit Trail & Security

```sql
-- User audit log summary (login activity, role changes, etc.)
SELECT
  u."UserName",
  ual."Action",
  COUNT(*) AS "Count",
  MAX(ual."PerformedAt") AS "LastOccurrence"
FROM "UserAuditLogs" ual
JOIN "AspNetUsers" u ON u."Id" = ual."UserId"
GROUP BY u."UserName", ual."Action"
ORDER BY u."UserName", ual."Action";
```

```sql
-- Failed login attempts (security concern)
SELECT
  u."UserName",
  u."AccessFailedCount",
  u."LockoutEnd",
  u."LockoutEnabled",
  (SELECT COUNT(*) FROM "UserAuditLogs" l WHERE l."UserId" = u."Id" AND l."Action" = 'LoginFailed') AS "TotalFailedLogins",
  (SELECT MAX(l."PerformedAt") FROM "UserAuditLogs" l WHERE l."UserId" = u."Id" AND l."Action" = 'LoginFailed') AS "LastFailedLogin"
FROM "AspNetUsers" u
WHERE u."AccessFailedCount" > 0
   OR EXISTS (SELECT 1 FROM "UserAuditLogs" l WHERE l."UserId" = u."Id" AND l."Action" = 'LoginFailed')
ORDER BY u."AccessFailedCount" DESC;
```

### 10. Application Configuration

```sql
-- App config entries
SELECT * FROM "AppConfigs";
```

### 11. Data Integrity Checks

```sql
-- Users without roles assigned
SELECT u."UserName", u."Email", u."Role" AS "UserRoleEnum"
FROM "AspNetUsers" u
LEFT JOIN "AspNetUserRoles" ur ON u."Id" = ur."UserId"
WHERE ur."RoleId" IS NULL;
```

```sql
-- Agents without wallets
SELECT a."Code", u."UserName"
FROM "Agents" a
JOIN "AspNetUsers" u ON u."Id" = a."UserId"
LEFT JOIN "Wallets" w ON w."AgentId" = a."Id"
WHERE w."Id" IS NULL;
```

```sql
-- Orphaned records: sessions without counts
SELECT cs."Id", a."Code", cs."SessionDate", cs."Status"
FROM "CashSessions" cs
JOIN "Agents" a ON a."Id" = cs."AgentId"
LEFT JOIN "CashCounts" cc ON cc."CashSessionId" = cs."Id"
WHERE cc."Id" IS NULL;
```

```sql
-- Table row counts for overall data volume
SELECT
  'AspNetUsers' AS "Table", COUNT(*) AS "Rows" FROM "AspNetUsers"
UNION ALL SELECT 'AspNetRoles', COUNT(*) FROM "AspNetRoles"
UNION ALL SELECT 'Agents', COUNT(*) FROM "Agents"
UNION ALL SELECT 'Branches', COUNT(*) FROM "Branches"
UNION ALL SELECT 'Vaults', COUNT(*) FROM "Vaults"
UNION ALL SELECT 'VaultTransactions', COUNT(*) FROM "VaultTransactions"
UNION ALL SELECT 'WalletTypes', COUNT(*) FROM "WalletTypes"
UNION ALL SELECT 'Wallets', COUNT(*) FROM "Wallets"
UNION ALL SELECT 'CashSessions', COUNT(*) FROM "CashSessions"
UNION ALL SELECT 'CashCounts', COUNT(*) FROM "CashCounts"
UNION ALL SELECT 'CashCountDetails', COUNT(*) FROM "CashCountDetails"
UNION ALL SELECT 'Transactions', COUNT(*) FROM "Transactions"
UNION ALL SELECT 'Discrepancies', COUNT(*) FROM "Discrepancies"
UNION ALL SELECT 'AuditLogs', COUNT(*) FROM "AuditLogs"
UNION ALL SELECT 'UserAuditLogs', COUNT(*) FROM "UserAuditLogs"
UNION ALL SELECT 'AppConfigs', COUNT(*) FROM "AppConfigs"
ORDER BY "Rows" DESC;
```

## Output Format

After running all queries, present the analysis in this structure:

### 1. Executive Summary
- Total users, agents, branches
- Overall data volume (row counts)
- Date range of activity

### 2. Usage Patterns
- Which agents are most/least active
- Peak activity days and trends
- Session duration patterns
- Transaction volume trends

### 3. Financial Overview
- Current vault balances
- Total transaction volumes by type
- Average transaction sizes
- Discrepancy rates and patterns

### 4. Data Quality Issues
- Users without roles
- Agents without wallets
- Orphaned records
- Unconfirmed emails

### 5. Security Observations
- Failed login patterns
- Audit trail coverage
- Pending/expired vault transactions

### 6. Improvement Suggestions
Based on the data analysis, provide actionable recommendations for:
- **Feature improvements** (e.g., missing workflows, UX pain points inferred from data)
- **Data integrity** (e.g., missing constraints, orphaned data cleanup)
- **Security hardening** (e.g., unassigned roles, unconfirmed emails)
- **Performance** (e.g., tables growing fast that may need archiving)
- **Business process** (e.g., high discrepancy rates, unused wallet types)
- **Mobile app adoption** (e.g., comparing web vs API usage if audit data shows this)

Each suggestion should include:
- **What**: The specific issue or opportunity
- **Why**: Evidence from the data
- **How**: Concrete implementation recommendation
- **Priority**: High / Medium / Low

$ARGUMENTS
