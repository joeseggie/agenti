<#
.SYNOPSIS
    Sets up Azure infrastructure for Agenti application.

.DESCRIPTION
    Creates all required Azure resources for hosting the Agenti application:
    - Resource Group
    - Azure Database for PostgreSQL Flexible Server
    - Azure App Service Plan (Linux B1)
    - Azure App Service Web App (.NET 10)
    Performs initial code deployment via zip deploy.

.PARAMETER ResourceGroupName
    Name of the Azure Resource Group (default: agenti-rg)

.PARAMETER Location
    Azure region for resources (default: northeurope)

.PARAMETER PostgresPassword
    Password for PostgreSQL database. If not provided, a strong password is generated automatically.

.PARAMETER AppName
    Name of the Web App (default: agenti)

.PARAMETER PostgresServerName
    Name of the PostgreSQL Flexible Server (default: agenti-pgserver)

.PARAMETER AppServicePlanName
    Name of the App Service Plan (default: agenti-plan)

.EXAMPLE
    .\setup-infrastructure.ps1
    # Runs with all defaults and auto-generates a PostgreSQL password

.EXAMPLE
    .\setup-infrastructure.ps1 -PostgresPassword "YourSecurePassword123!"
#>

param(
    [string]$ResourceGroupName = "agenti-rg",
    [string]$Location = "uaenorth",
    [string]$PostgresPassword = "",
    [string]$AppName = "agenti",
    [string]$PostgresServerName = "agenti-pgserver",
    [string]$AppServicePlanName = "agenti-plan"
)

# Note: We use "Continue" (not "Stop") because Azure CLI writes deprecation warnings
# to stderr, which PowerShell 5.1 treats as terminating errors with "Stop".
# All az CLI errors are caught via Assert-AzSuccess checking $LASTEXITCODE instead.
$ErrorActionPreference = "Continue"

# --- Helper: check az CLI exit code and fail if non-zero ---
function Assert-AzSuccess {
    param([string]$StepDescription)
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: $StepDescription failed (exit code $LASTEXITCODE)." -ForegroundColor Red
        Show-CleanupMessage
        exit 1
    }
}

# --- Helper: cleanup message on failure ---
function Show-CleanupMessage {
    Write-Host ""
    Write-Host "To clean up partial resources, run:" -ForegroundColor Red
    Write-Host "  az group delete --name $ResourceGroupName --yes --no-wait" -ForegroundColor Red
}

# --- Generate PostgreSQL password if not provided ---
if ([string]::IsNullOrWhiteSpace($PostgresPassword)) {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = [byte[]]::new(24)
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $PostgresPassword = ([Convert]::ToBase64String($bytes) -replace '[+/=]', '').Substring(0, 20) + "Ag1!"
}

# --- Determine repo root (script is at scripts/azure/) ---
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path (Join-Path $ScriptDir "..") "..")).Path

# --- PostgreSQL configuration ---
$PostgresUser = "agenti_user"
$PostgresDb = "agenti_prod"

Write-Host "=== Agenti Azure Infrastructure Setup ===" -ForegroundColor Cyan
Write-Host ""

# Check if Azure CLI is installed
try {
    az version | Out-Null
} catch {
    Write-Error "Azure CLI is not installed. Please install it from https://aka.ms/installazurecli"
    exit 1
}

# Check if logged in with a valid subscription
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Please log in to Azure..." -ForegroundColor Yellow
    az login
    $account = az account show | ConvertFrom-Json
}

# Verify the account has a real subscription (not just tenant-level)
if ($account.name -eq "N/A(tenant level account)" -or $account.id -eq $account.tenantId) {
    Write-Host ""
    Write-Host "ERROR: No valid Azure subscription found." -ForegroundColor Red
    Write-Host "Your CLI session is at the tenant level without an active subscription." -ForegroundColor Red
    Write-Host ""
    Write-Host "To fix this:" -ForegroundColor Yellow
    Write-Host "  1. Go to https://portal.azure.com and create a Pay-As-You-Go subscription" -ForegroundColor White
    Write-Host "  2. Then run: az login" -ForegroundColor White
    Write-Host "  3. If you have multiple subscriptions, set the right one:" -ForegroundColor White
    Write-Host "     az account set --subscription <subscription-name-or-id>" -ForegroundColor White
    Write-Host ""
    Write-Host "Available accounts:" -ForegroundColor Yellow
    az account list --output table
    exit 1
}

Write-Host "Using subscription: $($account.name) ($($account.id))" -ForegroundColor Green
Write-Host ""

# Register required resource providers (idempotent, no-ops if already registered)
Write-Host "Registering required resource providers..." -ForegroundColor Gray
az provider register --namespace Microsoft.DBforPostgreSQL --output none 2>$null
az provider register --namespace Microsoft.Web --output none 2>$null

# Wait for providers to register
$requiredProviders = @("Microsoft.DBforPostgreSQL", "Microsoft.Web")
foreach ($provider in $requiredProviders) {
    $maxWait = 120
    $waited = 0
    while ($waited -lt $maxWait) {
        $state = az provider show --namespace $provider --query "registrationState" -o tsv 2>$null
        if ($state -eq "Registered") { break }
        Start-Sleep -Seconds 5
        $waited += 5
    }
    if ($state -ne "Registered") {
        Write-Host "WARNING: Provider $provider is still in state '$state' after ${maxWait}s." -ForegroundColor Yellow
    }
}
Write-Host "  Resource providers ready." -ForegroundColor Green
Write-Host ""

# ============================================================
# 1. Create Resource Group
# ============================================================
Write-Host "[1/6] Creating Resource Group: $ResourceGroupName..." -ForegroundColor Yellow
az group create `
    --name $ResourceGroupName `
    --location $Location `
    --output none
Assert-AzSuccess "Create Resource Group"
Write-Host "  Resource Group created." -ForegroundColor Green

# ============================================================
# 2. Create PostgreSQL Flexible Server + Database
# ============================================================
Write-Host "[2/6] Creating PostgreSQL Flexible Server: $PostgresServerName..." -ForegroundColor Yellow
Write-Host "  This may take a few minutes..." -ForegroundColor Gray

az postgres flexible-server create `
    --resource-group $ResourceGroupName `
    --name $PostgresServerName `
    --location $Location `
    --admin-user $PostgresUser `
    --admin-password $PostgresPassword `
    --sku-name Standard_B1ms `
    --tier Burstable `
    --storage-size 32 `
    --version 16 `
    --yes `
    --output none
Assert-AzSuccess "Create PostgreSQL Flexible Server"

# Wait for server to be fully provisioned before creating dependent resources
Write-Host "  Waiting for server to be fully available..." -ForegroundColor Gray
$maxRetries = 12
$retryCount = 0
while ($retryCount -lt $maxRetries) {
    $serverState = az postgres flexible-server show `
        --resource-group $ResourceGroupName `
        --name $PostgresServerName `
        --query "state" -o tsv 2>$null
    if ($serverState -eq "Ready") { break }
    $retryCount++
    Start-Sleep -Seconds 10
}

# Allow Azure services to access the server (required for App Service)
az postgres flexible-server firewall-rule create `
    --resource-group $ResourceGroupName `
    --name $PostgresServerName `
    --rule-name AllowAzureServices `
    --start-ip-address 0.0.0.0 `
    --end-ip-address 0.0.0.0 `
    --output none
Assert-AzSuccess "Create PostgreSQL firewall rule"

# Create the application database (retry once if server propagation is slow)
az postgres flexible-server db create `
    --resource-group $ResourceGroupName `
    --server-name $PostgresServerName `
    --database-name $PostgresDb `
    --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Retrying database creation..." -ForegroundColor Gray
    Start-Sleep -Seconds 15
    az postgres flexible-server db create `
        --resource-group $ResourceGroupName `
        --server-name $PostgresServerName `
        --database-name $PostgresDb `
        --output none
    Assert-AzSuccess "Create PostgreSQL database"
}

# Get the server FQDN
$PostgresFqdn = az postgres flexible-server show `
    --resource-group $ResourceGroupName `
    --name $PostgresServerName `
    --query "fullyQualifiedDomainName" -o tsv
Assert-AzSuccess "Get PostgreSQL FQDN"

Write-Host "  PostgreSQL Flexible Server created: $PostgresFqdn" -ForegroundColor Green

# ============================================================
# 3. Create App Service Plan
# ============================================================
Write-Host "[3/6] Creating App Service Plan: $AppServicePlanName..." -ForegroundColor Yellow
az appservice plan create `
    --resource-group $ResourceGroupName `
    --name $AppServicePlanName `
    --location $Location `
    --sku B1 `
    --is-linux `
    --output none
Assert-AzSuccess "Create App Service Plan"
Write-Host "  App Service Plan created (B1 Linux)." -ForegroundColor Green

# ============================================================
# 4. Create Web App
# ============================================================
Write-Host "[4/6] Creating Web App: $AppName..." -ForegroundColor Yellow
az webapp create `
    --resource-group $ResourceGroupName `
    --plan $AppServicePlanName `
    --name $AppName `
    --runtime "DOTNETCORE:10.0" `
    --output none
Assert-AzSuccess "Create Web App"

# Enable basic auth on SCM site (required for zip deployment via az CLI)
az resource update `
    --resource-group $ResourceGroupName `
    --name scm `
    --namespace Microsoft.Web `
    --resource-type basicPublishingCredentialsPolicies `
    --parent "sites/$AppName" `
    --set properties.allow=true `
    --output none
Assert-AzSuccess "Enable SCM basic auth"

Write-Host "  Web App created." -ForegroundColor Green

# ============================================================
# 5. Configure Web App
# ============================================================
Write-Host "[5/6] Configuring Web App..." -ForegroundColor Yellow

$ConnectionString = "Server=$PostgresFqdn;Port=5432;Database=$PostgresDb;User Id=$PostgresUser;Password=$PostgresPassword;Ssl Mode=Require;"

# Set the connection string (encrypted at rest in App Service Configuration)
az webapp config connection-string set `
    --resource-group $ResourceGroupName `
    --name $AppName `
    --connection-string-type PostgreSQL `
    --settings DefaultConnection="$ConnectionString" `
    --output none
Assert-AzSuccess "Set connection string"

# Set app settings (disable Oryx build since we deploy pre-built binaries)
az webapp config appsettings set `
    --resource-group $ResourceGroupName `
    --name $AppName `
    --settings ASPNETCORE_ENVIRONMENT=Production SCM_DO_BUILD_DURING_DEPLOYMENT=false `
    --output none
Assert-AzSuccess "Set app settings"

Write-Host "  Web App configured." -ForegroundColor Green

# ============================================================
# 6. Build and Deploy App
# ============================================================
Write-Host "[6/6] Building and deploying application..." -ForegroundColor Yellow

$PublishDir = Join-Path $RepoRoot "publish"
$ZipPath = Join-Path $RepoRoot "publish.zip"

# Clean previous publish output
if (Test-Path $PublishDir) { Remove-Item -Path $PublishDir -Recurse -Force }
if (Test-Path $ZipPath) { Remove-Item -Path $ZipPath -Force }

# Build and publish
Write-Host "  Building project..." -ForegroundColor Gray
dotnet publish (Join-Path (Join-Path $RepoRoot "EastSeat.Agenti.Web") "EastSeat.Agenti.Web.csproj") `
    --configuration Release `
    --output $PublishDir
Assert-AzSuccess "dotnet publish"

# Create zip for deployment (using System.IO.Compression to ensure forward-slash
# paths, which is required for Linux App Service to extract directories correctly)
Write-Host "  Creating deployment package..." -ForegroundColor Gray
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$PublishDirFull = (Resolve-Path $PublishDir).Path
$archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
Get-ChildItem -Path $PublishDirFull -Recurse -File | ForEach-Object {
    $entryName = $_.FullName.Substring($PublishDirFull.Length + 1).Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $archive, $_.FullName, $entryName,
        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
$archive.Dispose()

# Deploy via zip deploy
Write-Host "  Deploying to Azure App Service..." -ForegroundColor Gray
az webapp deploy `
    --resource-group $ResourceGroupName `
    --name $AppName `
    --src-path $ZipPath `
    --type zip `
    --output none
Assert-AzSuccess "Deploy to App Service"

# Clean up local artifacts
Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $ZipPath -Force -ErrorAction SilentlyContinue

# Get the Web App URL
$AppUrl = az webapp show `
    --resource-group $ResourceGroupName `
    --name $AppName `
    --query "defaultHostName" -o tsv
Assert-AzSuccess "Get Web App URL"

Write-Host "  Application deployed." -ForegroundColor Green

# ============================================================
# Summary
# ============================================================
Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Resources Created:" -ForegroundColor White
Write-Host "  - Resource Group: $ResourceGroupName"
Write-Host "  - PostgreSQL Server: $PostgresFqdn"
Write-Host "  - PostgreSQL Database: $PostgresDb"
Write-Host "  - App Service Plan: $AppServicePlanName (B1 Linux)"
Write-Host "  - Web App: $AppName"
Write-Host ""
Write-Host "App URL: https://$AppUrl" -ForegroundColor Green
Write-Host ""
Write-Host "=== Credentials ===" -ForegroundColor Red
Write-Host "  PostgreSQL Server: $PostgresFqdn" -ForegroundColor Red
Write-Host "  PostgreSQL User: $PostgresUser" -ForegroundColor Red
Write-Host "  PostgreSQL Password: $PostgresPassword" -ForegroundColor Red
Write-Host "  PostgreSQL Database: $PostgresDb" -ForegroundColor Red
Write-Host "  IMPORTANT: Save these credentials securely. They will not be shown again." -ForegroundColor Red
Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Yellow
Write-Host "1. Set up OIDC authentication for GitHub Actions:"
Write-Host "   See scripts/azure/setup-secrets.md for detailed instructions"
Write-Host ""
Write-Host "2. Set these GitHub Secrets:"
Write-Host "   - AZURE_CLIENT_ID"
Write-Host "   - AZURE_TENANT_ID"
Write-Host "   - AZURE_SUBSCRIPTION_ID"
Write-Host "   - TEST_DB_PASSWORD"
Write-Host ""
Write-Host "3. Open https://$AppUrl to complete initial setup (create admin user and branch)"
Write-Host ""
Write-Host "=== Cost-Saving Tip ===" -ForegroundColor Yellow
Write-Host "To stop PostgreSQL when not in use (saves ~`$12-14/month on compute):" -ForegroundColor Yellow
Write-Host "  az postgres flexible-server stop --resource-group $ResourceGroupName --name $PostgresServerName" -ForegroundColor White
Write-Host "To restart:" -ForegroundColor Yellow
Write-Host "  az postgres flexible-server start --resource-group $ResourceGroupName --name $PostgresServerName" -ForegroundColor White
