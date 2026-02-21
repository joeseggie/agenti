# Azure Deployment Setup Guide

A complete, step-by-step guide to set up the Agenti application for CI/CD deployment to Azure using GitHub Actions. This guide covers **both the Azure Portal (GUI) and Azure CLI approaches**.

---

## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Part 1: Get Azure Credentials](#part-1-get-azure-credentials)
  - [GUI: Azure Portal](#gui-azure-portal)
  - [CLI: Azure CLI](#cli-azure-cli)
- [Part 2: Create Azure AD App Registration](#part-2-create-azure-ad-app-registration)
  - [GUI: Azure Portal](#gui-azure-portal-1)
  - [CLI: Azure CLI](#cli-azure-cli-1)
- [Part 3: Configure Federated Credentials (OIDC)](#part-3-configure-federated-credentials-oidc)
  - [GUI: Azure Portal](#gui-azure-portal-2)
  - [CLI: Azure CLI](#cli-azure-cli-2)
- [Part 4: Grant Azure Permissions](#part-4-grant-azure-permissions)
  - [GUI: Azure Portal](#gui-azure-portal-3)
  - [CLI: Azure CLI](#cli-azure-cli-3)
- [Part 5: Add Secrets to GitHub](#part-5-add-secrets-to-github)
- [Part 6: Configure GitHub Environments](#part-6-configure-github-environments)
- [Part 7: Provision Azure Infrastructure](#part-7-provision-azure-infrastructure)
  - [Option A: GitHub Actions Workflow](#option-a-github-actions-workflow)
  - [Option B: Local PowerShell Script](#option-b-local-powershell-script)
- [Part 8: Run the CI/CD Pipeline](#part-8-run-the-cicd-pipeline)
- [Architecture Reference](#architecture-reference)
- [Azure Resources Created](#azure-resources-created)
- [Cost Management](#cost-management)
- [Troubleshooting](#troubleshooting)
- [Security Best Practices](#security-best-practices)
- [Quick Reference Summary](#quick-reference-summary)

---

## Overview

The Agenti CI/CD pipeline uses **OIDC (OpenID Connect) workload identity federation** to authenticate GitHub Actions with Azure. This is the recommended approach because:

- No long-lived secrets stored in GitHub (Azure trusts GitHub's identity tokens directly)
- No credential expiration or rotation required
- Access is scoped per workflow/environment

**What you'll set up:**

| Credential | What It Is |
|------------|------------|
| `AZURE_SUBSCRIPTION_ID` | Identifies your Azure subscription (where resources live) |
| `AZURE_TENANT_ID` | Identifies your Microsoft Entra ID (Azure AD) tenant |
| `AZURE_CLIENT_ID` | Identifies the app registration GitHub Actions uses to authenticate |
| `TEST_DB_PASSWORD` | Password for PostgreSQL test databases in CI (integration & E2E tests) |

**Two GitHub Actions workflows:**

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| CI/CD Pipeline | `.github/workflows/ci-cd.yml` | Push to `main`, PRs | Build, test, deploy |
| Infrastructure Provisioning | `.github/workflows/infrastructure.yml` | Manual | Create/update Azure resources |

---

## Prerequisites

Before starting, ensure you have:

- [ ] **Azure subscription** with Owner role **OR** (Contributor + User Access Administrator) roles
- [ ] **GitHub repository admin** access (to configure secrets and environments)
- [ ] **Azure CLI** installed (for CLI approach): [Install Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- [ ] **.NET 10 SDK** installed (for local infrastructure provisioning)

---

## Part 1: Get Azure Credentials

You need your **Subscription ID** and **Tenant ID**. These already exist in your Azure account.

### GUI: Azure Portal

#### Get Subscription ID

1. Go to [portal.azure.com](https://portal.azure.com)
2. In the top search bar, type **"Subscriptions"** and click on it
3. Click on your subscription name from the list
4. Copy the **Subscription ID** (a 36-character GUID, e.g., `12345678-1234-1234-1234-123456789abc`)
5. **Save this value** — it will become your `AZURE_SUBSCRIPTION_ID`

#### Get Tenant ID

1. In the top search bar, type **"Microsoft Entra ID"** (formerly "Azure Active Directory") and click on it
2. On the **Overview** page, find and copy the **Tenant ID** (a 36-character GUID)
3. **Save this value** — it will become your `AZURE_TENANT_ID`

### CLI: Azure CLI

```bash
# Login to Azure (opens browser for authentication)
az login

# Get Subscription ID
az account show --query id -o tsv
# Save this as AZURE_SUBSCRIPTION_ID

# Get Tenant ID
az account show --query tenantId -o tsv
# Save this as AZURE_TENANT_ID

# If you have multiple subscriptions, list them and set the right one:
az account list --output table
az account set --subscription "<subscription-name-or-id>"
```

---

## Part 2: Create Azure AD App Registration

This creates the identity that GitHub Actions will use to authenticate with Azure.

### GUI: Azure Portal

1. In the top search bar, type **"Microsoft Entra ID"** and click on it
2. In the left sidebar, click **App registrations**
3. Click **+ New registration** (top of the page)
4. Fill in the form:
   - **Name**: `agenti-github-actions`
   - **Supported account types**: Select **"Accounts in this organizational directory only"** (single tenant)
   - **Redirect URI**: Leave blank (not needed for OIDC)
5. Click **Register**
6. On the app's **Overview** page, copy the **Application (client) ID**
7. **Save this value** — it will become your `AZURE_CLIENT_ID`
8. Also note the **Directory (tenant) ID** shown here (same as your Tenant ID from Part 1)

> **Note:** A service principal is automatically created when you register the app.

### CLI: Azure CLI

```bash
# Create the app registration
az ad app create --display-name "agenti-github-actions"
```

From the output, copy the `appId` field. **Save this as `AZURE_CLIENT_ID`**.

```bash
# Create the service principal for the app
az ad sp create --id <AZURE_CLIENT_ID>
```

Replace `<AZURE_CLIENT_ID>` with the `appId` you just copied.

---

## Part 3: Configure Federated Credentials (OIDC)

Federated credentials tell Azure which GitHub Actions contexts are allowed to authenticate using this app registration. You need **three credentials** — one for the main branch, one for the `production` environment, and one for the `infrastructure` environment.

### GUI: Azure Portal

1. In your app registration (from Part 2), click **Certificates & secrets** in the left sidebar
2. Click the **Federated credentials** tab
3. Click **+ Add credential**

#### Credential 1: Main Branch

4. Fill in the form:
   - **Federated credential scenario**: Select **"GitHub Actions deploying Azure resources"**
   - **Organization**: `joeseggie` (your GitHub username)
   - **Repository**: `agenti`
   - **Entity type**: Select **"Branch"**
   - **GitHub branch name**: `main`
   - **Name**: `github-main-branch`
5. Click **Add**

#### Credential 2: Production Environment

6. Click **+ Add credential** again
7. Fill in the form:
   - **Federated credential scenario**: Select **"GitHub Actions deploying Azure resources"**
   - **Organization**: `joeseggie`
   - **Repository**: `agenti`
   - **Entity type**: Select **"Environment"**
   - **GitHub environment name**: `production`
   - **Name**: `github-production-env`
8. Click **Add**

#### Credential 3: Infrastructure Environment

9. Click **+ Add credential** again
10. Fill in the form:
    - **Federated credential scenario**: Select **"GitHub Actions deploying Azure resources"**
    - **Organization**: `joeseggie`
    - **Repository**: `agenti`
    - **Entity type**: Select **"Environment"**
    - **GitHub environment name**: `infrastructure`
    - **Name**: `github-infrastructure-env`
11. Click **Add**

**Verification:** You should see all three credentials listed under the **Federated credentials** tab.

### CLI: Azure CLI

First, get the app's **Object ID** (different from the Client ID):

```bash
az ad app show --id <AZURE_CLIENT_ID> --query id -o tsv
# Save this as APP_OBJECT_ID (only needed for the commands below)
```

Now create the three federated credentials:

```bash
# Credential 1: Main branch
az ad app federated-credential create \
    --id <APP_OBJECT_ID> \
    --parameters '{
        "name": "github-main-branch",
        "issuer": "https://token.actions.githubusercontent.com",
        "subject": "repo:joeseggie/agenti:ref:refs/heads/main",
        "audiences": ["api://AzureADTokenExchange"]
    }'

# Credential 2: Production environment
az ad app federated-credential create \
    --id <APP_OBJECT_ID> \
    --parameters '{
        "name": "github-production-env",
        "issuer": "https://token.actions.githubusercontent.com",
        "subject": "repo:joeseggie/agenti:environment:production",
        "audiences": ["api://AzureADTokenExchange"]
    }'

# Credential 3: Infrastructure environment
az ad app federated-credential create \
    --id <APP_OBJECT_ID> \
    --parameters '{
        "name": "github-infrastructure-env",
        "issuer": "https://token.actions.githubusercontent.com",
        "subject": "repo:joeseggie/agenti:environment:infrastructure",
        "audiences": ["api://AzureADTokenExchange"]
    }'
```

**Verification:**

```bash
az ad app federated-credential list --id <APP_OBJECT_ID> --output table
```

---

## Part 4: Grant Azure Permissions

The app registration needs the **Contributor** role on your Azure subscription so it can create and manage resources.

### GUI: Azure Portal

1. In the top search bar, type **"Subscriptions"** and click on it
2. Click on your subscription name
3. In the left sidebar, click **Access control (IAM)**
4. Click **+ Add** (top of the page) → **Add role assignment**
5. **Role tab:**
   - In the **search box** at the top of the role list, type: **`Contributor`**
   - If the search box isn't visible, look under the **"Privileged administrator roles"** tab/section
   - Select **Contributor** from the results (description: "Grants full access to manage all resources, but does not allow you to assign roles...")
   - Click **Next**
6. **Members tab:**
   - Ensure **"User, group, or service principal"** is selected under "Assign access to"
   - Click **+ Select members**
   - In the search panel that opens on the right, type: **`agenti-github-actions`**
   - Click on **agenti-github-actions** when it appears
   - Click **Select** at the bottom of the panel
   - You should see `agenti-github-actions` listed under "Selected members"
   - Click **Next**
7. **Review + assign tab:**
   - Verify the summary:
     - **Role:** Contributor
     - **Scope:** Your subscription
     - **Members:** agenti-github-actions
   - Click **Review + assign**

**Verification:**
- On the Access control (IAM) page, click **Role assignments** tab
- Search for `agenti-github-actions`
- Confirm it has **Contributor** role

> **Troubleshooting:** If you can't find the Contributor role in the search, try:
> - Look under the **"Privileged administrator roles"** tab (some portal versions organize roles into tabs)
> - Clear any active filters
> - If you still can't find it, try the CLI approach below

### CLI: Azure CLI

```bash
# Grant Contributor role at subscription level
az role assignment create \
    --assignee <AZURE_CLIENT_ID> \
    --role Contributor \
    --scope /subscriptions/<AZURE_SUBSCRIPTION_ID>
```

**Verification:**

```bash
az role assignment list --assignee <AZURE_CLIENT_ID> --output table
```

You should see an entry with Role = `Contributor`.

---

## Part 5: Add Secrets to GitHub

Now add the credentials you've gathered to your GitHub repository.

1. Go to your GitHub repository: `https://github.com/joeseggie/agenti`
2. Click **Settings** (top menu bar)
3. In the left sidebar, click **Secrets and variables** → **Actions**
4. Click **New repository secret**

**Add these four secrets (one at a time):**

| Secret Name | Value | Source |
|-------------|-------|--------|
| `AZURE_CLIENT_ID` | Application (client) ID | From Part 2 |
| `AZURE_TENANT_ID` | Tenant / Directory ID | From Part 1 |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID | From Part 1 |
| `TEST_DB_PASSWORD` | Any secure password | Generate one (e.g., `MyTestDb@2026!SecurePass`) |

For each secret:
1. Click **New repository secret**
2. Enter the **Name** exactly as shown above
3. Paste the **Secret** value
4. Click **Add secret**

**Verification:** After adding all four, you should see them listed on the Actions secrets page (values are hidden).

---

## Part 6: Configure GitHub Environments

The workflows use GitHub environments to enforce deployment approval gates.

### Create `infrastructure` Environment

1. In your repository, go to **Settings** → **Environments**
2. Click **New environment**
3. Name: `infrastructure`
4. Click **Configure environment**
5. Under **Deployment protection rules**:
   - Check **Required reviewers**
   - Add yourself (and any team members who should approve infrastructure changes)
6. Click **Save protection rules**

### Create `production` Environment

1. Click **New environment** again
2. Name: `production`
3. Click **Configure environment**
4. Under **Deployment protection rules**:
   - Check **Required reviewers**
   - Add yourself (and any team members who should approve production deployments)
   - Optionally set a **Wait timer** (e.g., 5 minutes) for an extra safety delay
5. Click **Save protection rules**

---

## Part 7: Provision Azure Infrastructure

Before the CI/CD pipeline can deploy your app, the Azure resources must exist. This is a **one-time setup step**.

### Option A: GitHub Actions Workflow

1. Go to the **Actions** tab in your GitHub repository
2. In the left sidebar, click **Provision Azure Infrastructure**
3. Click the **Run workflow** dropdown (top right)
4. Leave all inputs at their defaults:
   - Resource Group: `agenti-rg`
   - Location: `uaenorth`
   - App name: `agenti`
   - PostgreSQL server: `agenti-pgserver`
   - App Service Plan: `agenti-plan`
5. Click the green **Run workflow** button
6. **Approve the workflow** when prompted (since `infrastructure` environment has required reviewers)
7. Wait for the workflow to complete (approximately 10–15 minutes)

**What this creates:**

| Step | Resource | Details |
|------|----------|---------|
| 1 | Resource Group | `agenti-rg` in UAE North |
| 2 | PostgreSQL Flexible Server | `agenti-pgserver` (Burstable B1ms, PostgreSQL 16) |
| 3 | App Service Plan | `agenti-plan` (B1 Linux) |
| 4 | Web App | `agenti` (.NET 10 runtime) |
| 5 | Configuration | Connection string + app settings |
| 6 | Initial Deployment | Builds and deploys the app |

### Option B: Local PowerShell Script

If you prefer to provision from your local machine:

```powershell
# Ensure you're logged in to Azure
az login

# Navigate to the repo root
cd c:\repos\Agenti

# Run the setup script (uses defaults)
.\scripts\azure\setup-infrastructure.ps1

# Or with custom parameters
.\scripts\azure\setup-infrastructure.ps1 `
    -ResourceGroupName "agenti-rg" `
    -Location "uaenorth" `
    -AppName "agenti" `
    -PostgresServerName "agenti-pgserver" `
    -AppServicePlanName "agenti-plan"
```

The script will:
1. Create all Azure resources
2. Build and deploy the application
3. Display the PostgreSQL credentials (save these securely!)
4. Print the App URL

**Prerequisites for local provisioning:**
- Azure CLI installed and logged in (`az login`)
- PowerShell 5.1+ or PowerShell 7+
- .NET 10 SDK

---

## Part 8: Run the CI/CD Pipeline

After infrastructure is provisioned, the CI/CD pipeline runs automatically on every push to `main`.

### Complete the App Setup (First Time Only)

1. Open the app URL: `https://agenti.azurewebsites.net`
2. Complete the first-time setup wizard:
   - Create the default branch
   - Create the admin user
3. The app is now ready for use

### Trigger the CI/CD Pipeline

**Automatic (on push):**

```bash
git checkout main
git add .
git commit -m "feat: your changes"
git push origin main
```

**Manual (from GitHub):**

1. Go to the **Actions** tab
2. Click **CI/CD Pipeline** in the left sidebar
3. Click **Run workflow** (if available) or push a commit to `main`

### Pipeline Flow

```
push to main ──► Build ──┬──► Unit Tests ─────────┐
                          ├──► Integration Tests ──┤
                          └──► E2E Tests ──────────┘
                                                   │
                                         (all tests pass)
                                                   │
                                                   ▼
                                       ⏸ Approval Required
                                       (production environment)
                                                   │
                                                   ▼
                                           Deploy to Azure
                                         App Service (agenti)
```

**What happens:**

1. **Build** — Restores, builds, publishes the .NET 10 application
2. **Unit Tests** — Runs unit tests with code coverage (no database needed)
3. **Integration Tests** — Spins up PostgreSQL container, runs integration tests
4. **E2E Tests** — Spins up PostgreSQL container, runs end-to-end tests
5. **Deploy** — After you approve, deploys to Azure App Service via OIDC

> **Note:** On pull requests, only Build + Tests run (no deployment). Deployment only happens on push to `main`.

### Approve the Deployment

1. When all tests pass, the Deploy job will show as **"Waiting"**
2. Click on the workflow run
3. Click **Review deployments**
4. Check **production**
5. Click **Approve and deploy**

---

## Architecture Reference

### Pipeline Architecture

```
                    ┌───────────────┐
                    │  GitHub Repo  │
                    │    (main)     │
                    └──────┬────────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
         push to main   pull request   manual trigger
              │            │            │
              ▼            ▼            ▼
        ┌──────────┐ ┌──────────┐ ┌──────────────────┐
        │  CI/CD   │ │  CI/CD   │ │  Infrastructure   │
        │ Pipeline │ │ Pipeline │ │  Provisioning     │
        │ (deploy) │ │ (no dep) │ │  (workflow_dispatch)
        └──────────┘ └──────────┘ └──────────────────┘
```

### How OIDC Authentication Works

```
GitHub Actions                          Azure
┌─────────────┐                  ┌──────────────────┐
│  Workflow    │                  │  Microsoft       │
│  requests    │── OIDC token ──►│  Entra ID        │
│  id-token    │                  │                  │
│              │◄── Azure token ─│  Validates       │
│  uses token  │                  │  federated       │
│  to deploy   │                  │  credential      │
└─────────────┘                  └──────────────────┘
```

No secrets are exchanged. GitHub proves its identity via a signed JWT token, and Azure validates it against the federated credential configuration.

---

## Azure Resources Created

| Resource | SKU / Tier | Name | Purpose |
|----------|-----------|------|---------|
| Resource Group | — | `agenti-rg` | Container for all resources |
| App Service Plan | B1 Linux | `agenti-plan` | Compute for the web app |
| Web App | .NET 10 | `agenti` | Hosts the Blazor Server app |
| PostgreSQL Flexible Server | Burstable B1ms | `agenti-pgserver` | Production database |
| PostgreSQL Database | — | `agenti_prod` | Application database |

**Region:** UAE North

**App URL:** `https://agenti.azurewebsites.net`

---

## Cost Management

| Resource | Running Cost | Stopped Cost |
|----------|-------------|-------------|
| App Service Plan (B1) | ~$15–18/month | Cannot be stopped |
| PostgreSQL Flexible Server (B1ms) | ~$12–14/month | ~$3.5–4/month (storage only) |
| **Total** | **~$29–32/month** | **~$19–22/month** |

### Stop PostgreSQL (When Not in Use)

```bash
az postgres flexible-server stop \
    --resource-group agenti-rg \
    --name agenti-pgserver
```

### Restart PostgreSQL

```bash
az postgres flexible-server start \
    --resource-group agenti-rg \
    --name agenti-pgserver
```

### Tear Down All Resources

```bash
# WARNING: This deletes everything — database data included!
az group delete --name agenti-rg --yes --no-wait
```

---

## Troubleshooting

### OIDC / Authentication Issues

| Error | Cause | Fix |
|-------|-------|-----|
| `Unable to get ACTIONS_ID_TOKEN_REQUEST_URL` | Workflow missing id-token permission | Ensure `permissions: id-token: write` is in the workflow YAML |
| `AADSTS70021: No matching federated identity record found` | Federated credential subject mismatch | Verify the subject matches exactly (e.g., `repo:joeseggie/agenti:environment:production`) |
| `The subscription could not be found` | Service principal lacks access | Re-run the role assignment (Part 4) |
| `Deployment failed - insufficient permissions` | Missing Contributor role | Assign Contributor role to the app registration |

### Deployment Issues

| Error | Cause | Fix |
|-------|-------|-----|
| 400 or 401 on deployment | SCM basic auth disabled | Re-enable: `az resource update --resource-group agenti-rg --name scm --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/agenti --set properties.allow=true --output none` |
| Static files (CSS/JS) return 404 | Zip file has backslash paths (Windows) | Use the infrastructure workflow or setup script (they handle path conversion) |
| App doesn't start | Various | Check logs: `az webapp log tail --name agenti --resource-group agenti-rg` |

### Database Issues

```bash
# Verify connection string
az webapp config connection-string list --name agenti --resource-group agenti-rg

# Check PostgreSQL server status
az postgres flexible-server show --resource-group agenti-rg --name agenti-pgserver --query state -o tsv

# Restart the app
az webapp restart --name agenti --resource-group agenti-rg
```

### Role Assignment Issues (Part 4)

**Can't find the Contributor role in Azure Portal:**
- Use the **search box** at the top of the role list
- Check under the **"Privileged administrator roles"** tab (some portal layouts organize roles into tabs)
- Clear any active filters on the page
- If still not visible, use the CLI approach instead

**Can't find `agenti-github-actions` in Members search:**
- Wait 30–60 seconds after creating the app registration (propagation delay)
- Try searching by the Application (client) ID instead of the name
- Refresh the page (Ctrl+F5 / Cmd+Shift+R)

**"You do not have permission to add role assignment" error:**
- You need **Owner** role OR **User Access Administrator** role on the subscription
- Contact your Azure administrator to grant you the required permissions

---

## Security Best Practices

1. **No long-lived secrets** — OIDC federation means no client secrets to expire or leak
2. **Minimal permissions** — Contributor role scoped to the subscription (or resource group)
3. **Branch protection** — Require PR reviews before merging to `main`
4. **Environment protection** — Require approvals for infrastructure and production deployments
5. **Separate environments** — `infrastructure` and `production` environments with distinct approvers
6. **Audit regularly** — Review app registration and role assignments annually
7. **Connection string security** — Database credentials stored in Azure App Service Configuration (encrypted at rest), not in GitHub

---

## Quick Reference Summary

### Credentials to Add to GitHub Secrets

| Secret | Where to Get It |
|--------|-----------------|
| `AZURE_SUBSCRIPTION_ID` | Azure Portal → **Subscriptions** → Your subscription → Subscription ID |
| `AZURE_TENANT_ID` | Azure Portal → **Microsoft Entra ID** → Overview → Tenant ID |
| `AZURE_CLIENT_ID` | Azure Portal → **Microsoft Entra ID** → App registrations → `agenti-github-actions` → Application (client) ID |
| `TEST_DB_PASSWORD` | Generate any secure password (used only in CI tests) |

### CLI Quick Commands

```bash
# Get all three Azure IDs
az account show --query id -o tsv          # AZURE_SUBSCRIPTION_ID
az account show --query tenantId -o tsv    # AZURE_TENANT_ID
az ad app show --id <app-id> --query appId -o tsv  # AZURE_CLIENT_ID
```

### Setup Checklist

- [ ] Azure subscription with Owner (or Contributor + User Access Admin) role
- [ ] App registration created (`agenti-github-actions`)
- [ ] 3 federated credentials added (main branch, production env, infrastructure env)
- [ ] Contributor role assigned to app registration
- [ ] 4 GitHub secrets configured (Client ID, Tenant ID, Subscription ID, Test DB Password)
- [ ] 2 GitHub environments created with required reviewers (`production`, `infrastructure`)
- [ ] Azure infrastructure provisioned (resource group, PostgreSQL, App Service)
- [ ] First-time app setup completed (admin user + branch created)
- [ ] CI/CD pipeline tested successfully

### Ongoing Deployment Workflow

1. Create a feature branch and make changes
2. Open a PR → CI runs build + tests (no deployment)
3. Merge PR to `main` → CI runs build + tests + deploys (after approval)
4. Approve the deployment in GitHub Actions
5. App is live at `https://agenti.azurewebsites.net`

---

## Related Documentation

- [CI/CD Pipeline Details](CI_CD_PIPELINE.md) — Pipeline architecture, jobs, and configuration
- [OIDC Setup Details](../scripts/azure/setup-secrets.md) — Detailed OIDC and secrets configuration
- [Infrastructure Script](../scripts/azure/setup-infrastructure.ps1) — Local provisioning script
- [Development Guide](DEVELOPMENT_GUIDE.md) — Local development setup
