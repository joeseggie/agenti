# GitHub Secrets & OIDC Setup Guide

This guide explains how to configure Azure OIDC authentication and GitHub secrets for the Agenti CI/CD and infrastructure provisioning workflows.

## Required GitHub Secrets

| Secret Name | Description | Used By |
|-------------|-------------|---------|
| `AZURE_CLIENT_ID` | Azure AD application (client) ID | Infrastructure & CI/CD workflows |
| `AZURE_TENANT_ID` | Azure AD tenant ID | Infrastructure & CI/CD workflows |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID | Infrastructure & CI/CD workflows |
| `TEST_DB_PASSWORD` | Password for PostgreSQL test database in CI | Integration & E2E test jobs |

## Required GitHub Variables

| Variable Name | Description | Set After |
|---------------|-------------|-----------|
| `AZURE_CONTAINER_REGISTRY` | ACR name (e.g. `agenticrabc123`) | First infrastructure provisioning run |

## Step-by-Step OIDC Setup

OIDC (OpenID Connect) workload identity federation allows GitHub Actions to authenticate with Azure without storing long-lived credentials. This is the recommended approach over service principal JSON secrets.

### 1. Create an Azure AD App Registration

```bash
az ad app create --display-name "agenti-github-actions"
```

Note the `appId` (client ID) from the output.

### 2. Create a Service Principal

```bash
az ad sp create --id <app-client-id>
```

### 3. Add Federated Credentials

Create federated credentials for each context that needs to authenticate:

**For the main branch (CI/CD deployments):**

```bash
az ad app federated-credential create \
    --id <app-object-id> \
    --parameters '{
        "name": "github-main-branch",
        "issuer": "https://token.actions.githubusercontent.com",
        "subject": "repo:<owner>/<repo>:ref:refs/heads/main",
        "audiences": ["api://AzureADTokenExchange"]
    }'
```

**For the `production` environment (CI/CD deploy job):**

```bash
az ad app federated-credential create \
    --id <app-object-id> \
    --parameters '{
        "name": "github-production-env",
        "issuer": "https://token.actions.githubusercontent.com",
        "subject": "repo:<owner>/<repo>:environment:production",
        "audiences": ["api://AzureADTokenExchange"]
    }'
```

**For the `infrastructure` environment (infrastructure provisioning):**

```bash
az ad app federated-credential create \
    --id <app-object-id> \
    --parameters '{
        "name": "github-infrastructure-env",
        "issuer": "https://token.actions.githubusercontent.com",
        "subject": "repo:<owner>/<repo>:environment:infrastructure",
        "audiences": ["api://AzureADTokenExchange"]
    }'
```

Replace `<owner>/<repo>` with your GitHub repository path (e.g. `joeseggie/agenti`).

To find the `<app-object-id>`:

```bash
az ad app show --id <app-client-id> --query id -o tsv
```

### 4. Grant Azure Permissions

Assign the Contributor role at the subscription level (needed for infrastructure provisioning to create resource groups):

```bash
az role assignment create \
    --assignee <app-client-id> \
    --role Contributor \
    --scope /subscriptions/<subscription-id>
```

### 5. Add Secrets to GitHub

1. Go to your GitHub repository
2. Navigate to **Settings** > **Secrets and variables** > **Actions**
3. Click **New repository secret** and add each:
   - `AZURE_CLIENT_ID` — The `appId` from step 1
   - `AZURE_TENANT_ID` — Your Azure AD tenant ID (`az account show --query tenantId -o tsv`)
   - `AZURE_SUBSCRIPTION_ID` — Your subscription ID (`az account show --query id -o tsv`)
   - `TEST_DB_PASSWORD` — A secure password for CI test databases

### 6. Configure GitHub Environments

Create two environments with protection rules:

**`infrastructure` environment:**
1. Go to **Settings** > **Environments** > **New environment**
2. Name: `infrastructure`
3. Add protection rules:
   - **Required reviewers**: Add team members who must approve infrastructure changes

**`production` environment:**
1. Go to **Settings** > **Environments** > **New environment**
2. Name: `production`
3. Add protection rules:
   - **Required reviewers**: Add team members who must approve production deployments
   - **Wait timer**: Optional delay before deployment (e.g. 5 minutes)

### 7. Set the ACR Variable (After First Infrastructure Run)

After running the infrastructure provisioning workflow for the first time:

1. Check the workflow summary for the ACR name
2. Go to **Settings** > **Secrets and variables** > **Actions** > **Variables** tab
3. Click **New repository variable**
4. Name: `AZURE_CONTAINER_REGISTRY`
5. Value: The ACR name from the infrastructure summary (e.g. `agenticrabc123`)

## Connection String

The database connection string is configured directly in the Azure Container App as a secret by the infrastructure workflow. It is **not** stored as a GitHub secret.

To retrieve or update it manually:

```bash
# View the connection string
az containerapp secret show \
    --name agenti \
    --resource-group agenti-rg \
    --secret-name db-conn

# Update the connection string
az containerapp secret set \
    --name agenti \
    --resource-group agenti-rg \
    --secrets "db-conn=Server=<postgres-ip>;Port=5432;Database=agenti_prod;User Id=agenti_user;Password=<password>;"
```

## Troubleshooting

### "Login failed with Error: Unable to get ACTIONS_ID_TOKEN_REQUEST_URL"

The OIDC token request failed. Verify:
- The workflow has `permissions: id-token: write` set
- The federated credential `subject` matches the workflow context (branch or environment)

### "AADSTS70021: No matching federated identity record found"

The federated credential subject doesn't match. Check:
- Repository path in the `subject` field matches exactly (`repo:owner/repo:...`)
- The correct subject type is used (`ref:refs/heads/main` for branch, `environment:production` for environment)

### "The subscription could not be found"

The service principal lacks access. Verify:
```bash
az role assignment list --assignee <client-id> --output table
```

### "Deployment failed - insufficient permissions"

The service principal needs Contributor role. Re-run:
```bash
az role assignment create \
    --assignee <client-id> \
    --role Contributor \
    --scope /subscriptions/<subscription-id>
```

## Security Best Practices

1. **No long-lived secrets**: OIDC federation eliminates the need for client secrets that can expire or leak
2. **Use minimal permissions**: Contributor role scoped to the subscription (or resource group if infrastructure already exists)
3. **Enable branch protection**: Require PR reviews before merging to `main`
4. **Use environment protection rules**: Require approvals for infrastructure and production changes
5. **Rotate app registration**: Review and rotate the app registration annually
