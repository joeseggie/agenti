# CI/CD Pipeline

This document describes the Agenti CI/CD pipeline, Azure infrastructure, and the settings required for deployment.

## Architecture Overview

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

**Two workflows:**

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| CI/CD Pipeline | `.github/workflows/ci-cd.yml` | Push to `main`, PRs | Build, test, deploy |
| Infrastructure Provisioning | `.github/workflows/infrastructure.yml` | Manual (`workflow_dispatch`) | Create/update Azure resources |

## Azure Infrastructure

| Resource | SKU | Name |
|----------|-----|------|
| Resource Group | — | `agenti-rg` |
| App Service Plan | B1 Linux | `agenti-plan` |
| Web App | .NET 10 | `agenti` |
| PostgreSQL Flexible Server | Burstable B1ms | `agenti-db` |
| PostgreSQL Database | — | `agenti_prod` |

**Region:** UAE North (selected for quota availability; the subscription has zero App Service VM quota in North Europe).

**App URL:** `https://agenti.azurewebsites.net`

## CI/CD Pipeline (`ci-cd.yml`)

### Pipeline Stages

```
push/PR ──► Build ──┬──► Unit Tests ─────────┐
                     ├──► Integration Tests ──┤
                     └──► E2E Tests ──────────┘
                                              │
                                    (main branch only)
                                              │
                                              ▼
                                          Deploy to
                                        Azure App Service
```

### Jobs

#### 1. Build
- Restores, builds, and publishes the .NET 10 application
- Uploads the publish output as a `webapp` artifact (retained 1 day)
- Runs on every push and PR

#### 2. Unit Tests
- Runs `EastSeat.Agenti.UnitTests` with code coverage
- No database required
- Uploads TRX test results (retained 7 days)

#### 3. Integration Tests
- Spins up a PostgreSQL 16 service container
- Runs `EastSeat.Agenti.IntegrationTests` with a real database
- Connection string injected via `ConnectionStrings__DefaultConnection` env var

#### 4. E2E Tests
- Spins up a PostgreSQL 16 service container
- Runs `EastSeat.Agenti.E2ETests` against a real database

#### 5. Deploy (main branch only)
- **Condition:** Only runs on push to `main` (not on PRs)
- **Environment:** `production` (requires reviewer approval)
- Downloads the `webapp` artifact from the Build job
- Authenticates to Azure via OIDC (no stored credentials)
- Deploys to Azure App Service using `azure/webapps-deploy@v3`

### How Deployment Works

1. The Build job runs `dotnet publish` and uploads the output as an artifact
2. The Deploy job downloads the artifact to `./publish`
3. `azure/login@v2` authenticates using OIDC workload identity federation
4. `azure/webapps-deploy@v3` deploys the folder to Azure App Service via the Azure management API
5. App Service restarts with the new code; EF Core migrations run automatically on startup

## Infrastructure Provisioning (`infrastructure.yml`)

This is a **manual workflow** (`workflow_dispatch`) for creating or updating Azure infrastructure. It is idempotent — safe to re-run.

### Inputs

| Input | Default | Description |
|-------|---------|-------------|
| `resource_group` | `agenti-rg` | Resource Group name |
| `location` | `uaenorth` | Azure region |
| `app_name` | `agenti` | Web App name |
| `postgres_server` | `agenti-pgserver` | PostgreSQL server name |
| `app_service_plan` | `agenti-plan` | App Service Plan name |

### Steps

1. **Create Resource Group**
2. **Create PostgreSQL Flexible Server** (or reuse existing — updates admin password)
3. **Create App Service Plan** (B1 Linux)
4. **Create Web App** (.NET 10 runtime, enables SCM basic auth for deployment)
5. **Configure Web App** (connection string, app settings, `SCM_DO_BUILD_DURING_DEPLOYMENT=false`)
6. **Build and Deploy App** (dotnet publish → zip → `az webapp deploy`)

### Idempotency

The workflow checks if the PostgreSQL server already exists before creating it. If the server exists, the admin password is updated to match the newly generated password, ensuring the connection string stays in sync.

## Required Settings

### GitHub Secrets

Configure these in **Settings > Secrets and variables > Actions**:

| Secret | Description | How to Get |
|--------|-------------|------------|
| `AZURE_CLIENT_ID` | Azure AD app registration client ID | `az ad app show --id <app-id> --query appId -o tsv` |
| `AZURE_TENANT_ID` | Azure AD tenant ID | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID | `az account show --query id -o tsv` |
| `TEST_DB_PASSWORD` | Password for CI test databases | Any secure password (used only in CI) |

### GitHub Environments

Configure these in **Settings > Environments**:

| Environment | Used By | Recommended Protection |
|-------------|---------|----------------------|
| `production` | CI/CD deploy job | Required reviewers |
| `infrastructure` | Infrastructure provisioning | Required reviewers |

### Azure AD App Registration (OIDC)

The workflows authenticate to Azure using OIDC workload identity federation. This requires:

1. **Azure AD app registration** with a service principal
2. **Federated credentials** for each workflow context:
   - `repo:<owner>/<repo>:environment:production` (CI/CD deploy)
   - `repo:<owner>/<repo>:environment:infrastructure` (infrastructure provisioning)
3. **Contributor role** assigned at the subscription level

See [setup-secrets.md](../scripts/azure/setup-secrets.md) for detailed setup instructions.

### Azure App Service Settings

These are configured automatically by the infrastructure workflow/script:

| Setting | Value | Purpose |
|---------|-------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | .NET environment |
| `SCM_DO_BUILD_DURING_DEPLOYMENT` | `false` | Prevents Kudu from rebuilding pre-built binaries |
| Connection string `DefaultConnection` | PostgreSQL connection string | Database connection (encrypted at rest) |

**SCM Basic Auth** is enabled on the App Service SCM site. This is required for `az webapp deploy` to work.

## Local Setup Script

For initial provisioning from a local machine (without GitHub Actions), use:

```powershell
.\scripts\azure\setup-infrastructure.ps1
```

This performs the same 6 steps as the infrastructure workflow. See the script's inline documentation for parameters.

**Prerequisites:**
- Azure CLI installed and logged in (`az login`)
- PowerShell 5.1+ (Windows default) or PowerShell 7+
- .NET 10 SDK

## Deployment Flow Summary

### First-Time Setup

1. Run `.\scripts\azure\setup-infrastructure.ps1` (or trigger the Infrastructure workflow)
2. Open the app URL and complete the setup wizard (create admin user and branch)
3. Configure GitHub secrets and environments (see [setup-secrets.md](../scripts/azure/setup-secrets.md))
4. Push to `main` to trigger the CI/CD pipeline

### Ongoing Deployments

1. Create a PR with your changes
2. CI/CD runs build + tests on the PR (no deployment)
3. Merge PR to `main`
4. CI/CD runs build + tests + deploys to Azure (after `production` environment approval)

## Troubleshooting

### Deployment returns 400 or 401

**SCM basic auth is disabled.** Re-enable it:
```bash
az resource update \
    --resource-group agenti-rg \
    --name scm \
    --namespace Microsoft.Web \
    --resource-type basicPublishingCredentialsPolicies \
    --parent sites/agenti \
    --set properties.allow=true \
    --output none
```

### Static files (CSS/JS) return 404 after deployment

**Zip file has backslash paths.** This happens when creating the zip on Windows with `Compress-Archive`. The setup script uses `System.IO.Compression` with forward-slash conversion to avoid this. If deploying manually from Windows, ensure zip entries use forward slashes (`wwwroot/_content/...` not `wwwroot\_content\...`).

### App doesn't start after deployment

Check the application logs:
```bash
az webapp log tail --name agenti --resource-group agenti-rg
```

Check the Docker container logs (App Service runs the app in a container):
```bash
az webapp log download --name agenti --resource-group agenti-rg --log-file logs.zip
```

### OIDC authentication fails

See the troubleshooting section in [setup-secrets.md](../scripts/azure/setup-secrets.md).

### Database connection fails

Verify the connection string:
```bash
az webapp config connection-string list --name agenti --resource-group agenti-rg
```

Verify the PostgreSQL server is running:
```bash
az postgres flexible-server show --resource-group agenti-rg --name agenti-db --query state -o tsv
```

If the server was stopped (cost-saving), start it:
```bash
az postgres flexible-server start --resource-group agenti-rg --name agenti-db
```

## Cost Management

| Resource | Running Cost | Stopped Cost |
|----------|-------------|-------------|
| App Service Plan (B1) | ~$15–18/month | Cannot be stopped |
| PostgreSQL Flexible Server (B1ms) | ~$12–14/month | ~$3.5–4/month (storage only) |
| **Total** | **~$29–32/month** | **~$19–22/month** |

To stop PostgreSQL when not in use:
```bash
az postgres flexible-server stop --resource-group agenti-rg --name agenti-db
```

To restart:
```bash
az postgres flexible-server start --resource-group agenti-rg --name agenti-db
```
