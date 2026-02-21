# Azure Application Insights Setup Guide

This guide provides step-by-step instructions for configuring and deploying the Agenti application with Azure Application Insights for monitoring and telemetry.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Local Development Setup](#local-development-setup)
- [Azure Resources Setup](#azure-resources-setup)
- [Azure Container Apps Deployment](#azure-container-apps-deployment)
- [Monitoring & Dashboards](#monitoring--dashboards)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

- Azure subscription with permissions to create resources
- Azure CLI installed (`az` command)
- Docker installed (for container builds)
- .NET 10 SDK installed
- Access to the Agenti repository

---

## Local Development Setup

### Configuration

By default, Application Insights is **disabled in local development** to avoid unnecessary Azure charges.

**appsettings.Development.json:**
```json
{
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
```

**What happens locally:**
- Serilog logs to **console only** (no Azure ingestion)
- TelemetryClient is injected as `null` (no-op operations)
- All custom events and metrics are silently ignored

### Testing Locally with Application Insights (Optional)

If you need to test telemetry locally:

1. Create a test Application Insights resource in Azure
2. Copy the **Connection String** from Azure Portal
3. Add to `appsettings.Development.json`:
   ```json
   {
     "ApplicationInsights": {
       "ConnectionString": "InstrumentationKey=your-key-here;IngestionEndpoint=https://..."
     }
   }
   ```
4. Run the application:
   ```bash
   cd EastSeat.Agenti.Web
   dotnet run
   ```

---

## Azure Resources Setup

### Step 1: Create Resource Group

```bash
az group create --name agenti-rg --location uaenorth
```

### Step 2: Create Application Insights Resource

```bash
az monitor app-insights component create \
  --app agenti-insights \
  --location uaenorth \
  --resource-group agenti-rg \
  --application-type web \
  --retention-time 90
```

**Retrieve Connection String:**
```bash
az monitor app-insights component show \
  --app agenti-insights \
  --resource-group agenti-rg \
  --query connectionString -o tsv
```

Save this connection string - you'll need it for deployment.

### Step 3: Configure Sampling and Retention (Production)

**Adaptive Sampling** is already configured in `appsettings.Production.json`:
- **50% sampling** to reduce telemetry volume and costs
- **90-day retention** (configurable via Azure Portal)
- Exceptions are **never sampled** (always captured)

To adjust retention in Azure Portal:
1. Navigate to Application Insights resource
2. Go to **Usage and estimated costs**
3. Click **Data Retention**
4. Adjust retention period (30-730 days)

---

## Azure Container Apps Deployment

### Step 1: Build and Push Docker Image

**Option A: Azure Container Registry (Recommended)**

```bash
# Create ACR
az acr create --name agentiregistry --resource-group agenti-rg --sku Basic

# Log in
az acr login --name agentiregistry

# Build and push
docker build -t agentiregistry.azurecr.io/agenti:latest .
docker push agentiregistry.azurecr.io/agenti:latest
```

**Option B: Docker Hub**

```bash
docker build -t yourusername/agenti:latest .
docker push yourusername/agenti:latest
```

### Step 2: Create Azure Container Apps Environment

```bash
az containerapp env create \
  --name agenti-env \
  --resource-group agenti-rg \
  --location uaenorth
```

### Step 3: Deploy Container App with Application Insights

```bash
# Get Application Insights Connection String
AI_CONNECTION_STRING=$(az monitor app-insights component show \
  --app agenti-insights \
  --resource-group agenti-rg \
  --query connectionString -o tsv)

# Deploy Container App
az containerapp create \
  --name agenti-app \
  --resource-group agenti-rg \
  --environment agenti-env \
  --image agentiregistry.azurecr.io/agenti:latest \
  --target-port 8080 \
  --ingress external \
  --cpu 1 --memory 2Gi \
  --min-replicas 1 --max-replicas 3 \
  --env-vars \
    "ASPNETCORE_ENVIRONMENT=Production" \
    "APPLICATIONINSIGHTS__CONNECTIONSTRING=$AI_CONNECTION_STRING" \
    "ConnectionStrings__DefaultConnection=your-postgres-connection-string"
```

### Step 4: Verify Deployment

```bash
# Get app URL
az containerapp show \
  --name agenti-app \
  --resource-group agenti-rg \
  --query properties.configuration.ingress.fqdn -o tsv
```

Access the application and perform some actions (login, vault operations, cash counts).

---

## Monitoring & Dashboards

### View Telemetry in Azure Portal

1. Navigate to **Application Insights** → `agenti-insights`
2. Go to **Logs** and run queries:

**Custom Events (Vault Operations):**
```kql
customEvents
| where name in ("vault_withdrawal_completed", "vault_deposit_completed", "vault_adjustment_requested", "vault_adjustment_approved")
| project timestamp, name, customDimensions
| order by timestamp desc
| take 50
```

**Custom Metrics (Background Services):**
```kql
customMetrics
| where name in ("vault_transactions_expired", "audit_logs_cleaned")
| summarize avg(value), max(value), min(value) by name, bin(timestamp, 1h)
| order by timestamp desc
```

**Exceptions & Errors:**
```kql
exceptions
| where timestamp > ago(24h)
| project timestamp, type, outerMessage, innermostMessage, operation_Name
| order by timestamp desc
```

**Performance (Slow Queries):**
```kql
dependencies
| where type == "SQL"
| where duration > 1000  // Queries slower than 1 second
| project timestamp, name, duration, success, resultCode
| order by duration desc
| take 20
```

### Create Custom Dashboard

1. In Application Insights, click **+ New Dashboard**
2. Add tiles:
   - **Request Success Rate** (Metrics → Requests)
   - **Vault Operations Today** (Custom Events → `vault_*` events)
   - **Exception Count (24h)** (Exceptions)
   - **Background Service Health** (Custom Metrics)
   - **Database Query Performance** (Dependencies → SQL)

### Configure Alerts

**Alert 1: High Exception Rate**
```bash
az monitor metrics alert create \
  --name "Agenti High Exception Rate" \
  --resource-group agenti-rg \
  --scopes /subscriptions/{subscription-id}/resourceGroups/agenti-rg/providers/microsoft.insights/components/agenti-insights \
  --condition "count exceptions/count > 10" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action-group-id {action-group-id}
```

**Alert 2: Vault Adjustment Spike (Suspicious Activity)**
```bash
# Create via Azure Portal:
# 1. Navigate to Application Insights → Alerts
# 2. New Alert Rule
# 3. Signal: Custom Events
# 4. Condition: customEvents with name="vault_adjustment_requested" > 5 in 10 minutes
# 5. Action Group: Email/SMS to Admin
```

---

## Troubleshooting

### Telemetry Not Appearing in Azure

**Check Connection String:**
```bash
# Verify environment variable in Container App
az containerapp show \
  --name agenti-app \
  --resource-group agenti-rg \
  --query "properties.template.containers[0].env"
```

**Check Application Logs:**
```bash
az containerapp logs show \
  --name agenti-app \
  --resource-group agenti-rg \
  --tail 50
```

Look for Serilog startup messages:
- ✅ `Starting Agenti application`
- ✅ `Application Insights sink configured`

**Data Ingestion Delay:**
- Application Insights has a **1-5 minute delay** for telemetry ingestion
- Live Metrics Stream shows real-time data (Metrics → Live Metrics)

### Sampling Rate Too Aggressive

If critical events are missing, adjust sampling:

**Option 1: Adjust Sampling Percentage (appsettings.Production.json)**
```json
{
  "ApplicationInsights": {
    "SamplingPercentage": 75  // Increase from 50% to 75%
  }
}
```

**Option 2: Disable Sampling for Critical Events (Code)**

In `Program.cs`, modify telemetry configuration:
```csharp
builder.Services.Configure<TelemetryConfiguration>(config =>
{
    var excludedTypes = "Request;Exception;Event";  // Never sample Events
    config.DefaultTelemetrySink.TelemetryProcessorChainBuilder.UseAdaptiveSampling(
        maxTelemetryItemsPerSecond: 10,
        excludedTypes: excludedTypes);
});
```

### High Azure Costs

**Reduce Ingestion Volume:**
1. Lower sampling percentage to 25-30%
2. Disable verbose EF Core query logging in Production
3. Reduce retention period to 30 days
4. Use **Daily Cap** (Usage and estimated costs → Daily cap)

**Cost Breakdown:**
- **Ingestion:** $2.88 per GB after 5 GB free
- **Retention:** $0.12 per GB per month after 90 days
- **Typical Usage:** ~500MB-2GB per day for production (depends on traffic)

### Background Services Not Reporting Metrics

**Verify Service Registration:**
Check `Program.cs` has:
```csharp
builder.Services.AddHostedService<VaultExpirationService>();
builder.Services.AddHostedService<UserAuditCleanupService>();
```

**Check Logs:**
```bash
az containerapp logs show \
  --name agenti-app \
  --resource-group agenti-rg \
  --tail 100 | grep "VaultExpirationService\|UserAuditCleanupService"
```

Expected output:
```
Vault Expiration Service started.
Expired 3 pending vault transactions.
```

---

## Custom Events Tracked

| Event Name | Triggered By | Properties |
|-----------|-------------|-----------|
| `vault_withdrawal_completed` | Opening cash session | BranchId, SessionId, Amount, UserId |
| `vault_deposit_completed` | Closing cash session | BranchId, SessionId, Amount, UserId |
| `vault_adjustment_requested` | Manual vault adjustment | BranchId, Amount, Type, UserId, ExpiresAt |
| `vault_adjustment_approved` | Admin approves adjustment | TransactionId, Amount, ApprovedBy, CreatedBy |
| `vault_adjustment_rejected` | Admin rejects adjustment | TransactionId, Amount, RejectedBy, CreatedBy |
| `cash_count_submitted` | Opening/closing count | Type, AgentId, SessionId, TotalAmount |
| `session_closed` | Closing cash session | SessionId, AgentId, SessionDate, Duration |
| `vault_expiration_check` | Background service | ExpiredCount |
| `audit_cleanup_completed` | Background service | RemovedCount, CutoffDate |

---

## Custom Metrics Tracked

| Metric Name | Description | Unit |
|------------|-------------|------|
| `vault_transactions_expired` | Count of expired pending transactions | Count |
| `audit_logs_cleaned` | Count of old audit logs removed | Count |

---

## Best Practices

1. **Local Development:** Keep Application Insights disabled (`ConnectionString: ""`)
2. **Staging Environment:** Use dedicated Application Insights resource with 100% sampling
3. **Production:** Use 50% sampling with exception exclusion
4. **Sensitive Data:** Never log user passwords, tokens, or PII in custom properties
5. **Alerts:** Configure email/SMS alerts for critical failures (exception rate, vault approval spikes)
6. **Dashboards:** Pin frequently used queries to a shared dashboard for team visibility
7. **Cost Management:** Set a daily cap to prevent runaway costs from DDoS or misconfiguration

---

## Additional Resources

- [AZURE_LOGS_QUERYING.md](AZURE_LOGS_QUERYING.md) — Detailed guide for accessing, viewing, and querying Agenti logs in the Azure Portal (KQL query library)
- [Application Insights Documentation](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview)
- [Serilog Sinks for Application Insights](https://github.com/serilog-contrib/serilog-sinks-applicationinsights)
- [Azure Container Apps Monitoring](https://learn.microsoft.com/azure/container-apps/observability)
- [KQL Query Language](https://learn.microsoft.com/azure/data-explorer/kusto/query/)

---

## Support

For issues or questions:
- Check [Troubleshooting](#troubleshooting) section
- Review Application Insights logs and Live Metrics Stream
- Contact DevOps team for Azure resource permission issues
