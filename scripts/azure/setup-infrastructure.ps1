<#
.SYNOPSIS
    Sets up Azure infrastructure for Agenti application.

.DESCRIPTION
    Creates all required Azure resources for hosting the Agenti application:
    - Resource Group
    - Virtual Network for secure communication
    - Azure Container Instance for PostgreSQL
    - Azure Container Registry (ACR)
    - Azure Container Apps Environment
    - Azure Container App

.PARAMETER ResourceGroupName
    Name of the Azure Resource Group (default: agenti-rg)

.PARAMETER Location
    Azure region for resources (default: uaenorth)

.PARAMETER PostgresPassword
    Password for PostgreSQL database. If not provided, a strong password is generated automatically.

.PARAMETER AppName
    Name of the Container App (default: agenti)

.PARAMETER PostgresContainerName
    Name of the PostgreSQL container instance (default: agenti-postgres)

.PARAMETER ContainerRegistryName
    Name of the Azure Container Registry. If not provided, a unique name is generated automatically.

.PARAMETER ContainerAppEnvName
    Name of the Container Apps Environment (default: agenti-env)

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
    [string]$PostgresContainerName = "agenti-postgres",
    [string]$ContainerRegistryName = "",
    [string]$ContainerAppEnvName = "agenti-env"
)

$ErrorActionPreference = "Stop"

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
    $bytes = [byte[]]::new(24)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $PostgresPassword = ([Convert]::ToBase64String($bytes) -replace '[+/=]', '').Substring(0, 20) + "Ag1!"
}

# --- Generate container registry name if not provided ---
if ([string]::IsNullOrWhiteSpace($ContainerRegistryName)) {
    $suffix = -join ((97..122) + (48..57) | Get-Random -Count 6 | ForEach-Object { [char]$_ })
    $ContainerRegistryName = "agenticr$suffix"
}

# --- Determine repo root (script is at scripts/azure/) ---
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir ".." "..")).Path

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
az provider register --namespace Microsoft.ContainerRegistry --output none 2>$null
az provider register --namespace Microsoft.App --output none 2>$null
az provider register --namespace Microsoft.OperationalInsights --output none 2>$null

# Wait for providers to register
$requiredProviders = @("Microsoft.ContainerRegistry", "Microsoft.App", "Microsoft.OperationalInsights")
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
Write-Host "[1/7] Creating Resource Group: $ResourceGroupName..." -ForegroundColor Yellow
az group create `
    --name $ResourceGroupName `
    --location $Location `
    --output none
Assert-AzSuccess "Create Resource Group"
Write-Host "  Resource Group created." -ForegroundColor Green

# ============================================================
# 2. Create Virtual Network with delegated subnets
# ============================================================
Write-Host "[2/7] Creating Virtual Network..." -ForegroundColor Yellow
$VNetName = "agenti-vnet"
$SubnetContainerApp = "containerapp-subnet"
$SubnetContainer = "container-subnet"

# Create VNet without inline subnet
az network vnet create `
    --resource-group $ResourceGroupName `
    --name $VNetName `
    --address-prefix 10.0.0.0/16 `
    --output none
Assert-AzSuccess "Create Virtual Network"

# Create container subnet with ACI delegation
az network vnet subnet create `
    --resource-group $ResourceGroupName `
    --vnet-name $VNetName `
    --name $SubnetContainer `
    --address-prefix 10.0.1.0/24 `
    --delegations Microsoft.ContainerInstance/containerGroups `
    --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    # Subnet may already exist from a previous run; update delegation instead
    az network vnet subnet update `
        --resource-group $ResourceGroupName `
        --vnet-name $VNetName `
        --name $SubnetContainer `
        --delegations Microsoft.ContainerInstance/containerGroups `
        --output none
    Assert-AzSuccess "Update container subnet delegation"
}

# Create Container Apps subnet
az network vnet subnet create `
    --resource-group $ResourceGroupName `
    --vnet-name $VNetName `
    --name $SubnetContainerApp `
    --address-prefix 10.0.2.0/24 `
    --delegations Microsoft.App/environments `
    --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    # Subnet may already exist; update delegation
    az network vnet subnet update `
        --resource-group $ResourceGroupName `
        --vnet-name $VNetName `
        --name $SubnetContainerApp `
        --delegations Microsoft.App/environments `
        --output none
    Assert-AzSuccess "Update Container Apps subnet delegation"
}

Write-Host "  Virtual Network created with delegated subnets." -ForegroundColor Green

# ============================================================
# 3. Create PostgreSQL Container Instance (YAML deployment)
# ============================================================
Write-Host "[3/7] Creating PostgreSQL Container Instance..." -ForegroundColor Yellow
Write-Host "  NOTE: PostgreSQL data is ephemeral (lost on container restart)." -ForegroundColor Gray
Write-Host "  For production persistence, consider Azure Database for PostgreSQL." -ForegroundColor Gray

# Get subnet ID for container
$ContainerSubnetId = az network vnet subnet show `
    --resource-group $ResourceGroupName `
    --vnet-name $VNetName `
    --name $SubnetContainer `
    --query id -o tsv
Assert-AzSuccess "Get container subnet ID"

# Generate ACI deployment YAML
# YAML format is required for VNet integration with private IP
$aciYaml = @"
apiVersion: '2021-10-01'
location: $Location
name: $PostgresContainerName
properties:
  containers:
  - name: postgres
    properties:
      image: postgres:16-alpine
      resources:
        requests:
          cpu: 1.0
          memoryInGB: 1.5
      ports:
      - port: 5432
        protocol: TCP
      environmentVariables:
      - name: POSTGRES_USER
        value: agenti_user
      - name: POSTGRES_DB
        value: agenti_prod
      - name: POSTGRES_PASSWORD
        secureValue: '$PostgresPassword'
  osType: Linux
  ipAddress:
    type: Private
    ports:
    - port: 5432
      protocol: TCP
  subnetIds:
  - id: $ContainerSubnetId
type: Microsoft.ContainerInstance/containerGroups
"@

# Write YAML to a temporary file
$yamlPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "aci-postgres.yaml")
$aciYaml | Out-File -FilePath $yamlPath -Encoding utf8NoBOM -Force

try {
    az container create `
        --resource-group $ResourceGroupName `
        --file $yamlPath `
        --output none
    Assert-AzSuccess "Create PostgreSQL container"
} finally {
    Remove-Item -Path $yamlPath -Force -ErrorAction SilentlyContinue
}

# Wait for container to get a private IP
$maxRetries = 12
$retryCount = 0
$PostgresIP = $null

while ($retryCount -lt $maxRetries) {
    $containerState = az container show `
        --resource-group $ResourceGroupName `
        --name $PostgresContainerName `
        --query "instanceView.state" -o tsv 2>$null

    $PostgresIP = az container show `
        --resource-group $ResourceGroupName `
        --name $PostgresContainerName `
        --query "ipAddress.ip" -o tsv 2>$null

    if ($PostgresIP -and $containerState -eq "Running") {
        break
    }

    $retryCount++
    Write-Host "  Waiting for container to start... ($retryCount/$maxRetries)" -ForegroundColor Gray
    Start-Sleep -Seconds 10
}

if (-not $PostgresIP) {
    Write-Host "ERROR: Failed to get PostgreSQL container IP after $maxRetries attempts." -ForegroundColor Red
    Write-Host "  Check logs: az container logs --resource-group $ResourceGroupName --name $PostgresContainerName" -ForegroundColor Yellow
    Show-CleanupMessage
    exit 1
}

Write-Host "  PostgreSQL Container created at IP: $PostgresIP" -ForegroundColor Green

# ============================================================
# 4. Create Azure Container Registry
# ============================================================
Write-Host "[4/7] Creating Azure Container Registry..." -ForegroundColor Yellow
az acr create `
    --resource-group $ResourceGroupName `
    --name $ContainerRegistryName `
    --sku Basic `
    --admin-enabled true `
    --output none
Assert-AzSuccess "Create Container Registry"

$AcrLoginServer = az acr show `
    --resource-group $ResourceGroupName `
    --name $ContainerRegistryName `
    --query loginServer -o tsv
Assert-AzSuccess "Get ACR login server"

Write-Host "  Container Registry created: $AcrLoginServer" -ForegroundColor Green

# ============================================================
# 5. Build and push Docker image to ACR
# ============================================================
Write-Host "[5/7] Building Docker image in ACR (this may take a few minutes)..." -ForegroundColor Yellow

# Use git remote URL for the build context to avoid Windows NTFS tar issues.
# Falls back to local directory if no git remote is found.
$BuildSource = $RepoRoot
try {
    Push-Location $RepoRoot
    $GitRemote = git remote get-url origin 2>$null
    Pop-Location
    if ($GitRemote -match "github\.com[:/](.+?)(?:\.git)?$") {
        $BuildSource = "https://github.com/$($Matches[1]).git"
        Write-Host "  Building from GitHub: $BuildSource" -ForegroundColor Gray
    }
} catch {
    # No git remote; use local directory
}

az acr build `
    --registry $ContainerRegistryName `
    --image agenti-web:latest `
    --file EastSeat.Agenti.Web/Dockerfile `
    $BuildSource
Assert-AzSuccess "Build Docker image"
Write-Host "  Docker image built and pushed to $AcrLoginServer/agenti-web:latest" -ForegroundColor Green

# ============================================================
# 6. Create Container Apps Environment
# ============================================================
Write-Host "[6/7] Creating Container Apps Environment..." -ForegroundColor Yellow

$ContainerAppSubnetId = az network vnet subnet show `
    --resource-group $ResourceGroupName `
    --vnet-name $VNetName `
    --name $SubnetContainerApp `
    --query id -o tsv
Assert-AzSuccess "Get Container Apps subnet ID"

az containerapp env create `
    --name $ContainerAppEnvName `
    --resource-group $ResourceGroupName `
    --location $Location `
    --infrastructure-subnet-resource-id $ContainerAppSubnetId `
    --output none
Assert-AzSuccess "Create Container Apps Environment"
Write-Host "  Container Apps Environment created." -ForegroundColor Green

# ============================================================
# 7. Create Container App
# ============================================================
Write-Host "[7/7] Creating Container App..." -ForegroundColor Yellow

$ConnectionString = "Server=$PostgresIP;Port=5432;Database=agenti_prod;User Id=agenti_user;Password=$PostgresPassword;"

# Get ACR credentials
$AcrUsername = az acr credential show `
    --resource-group $ResourceGroupName `
    --name $ContainerRegistryName `
    --query username -o tsv
Assert-AzSuccess "Get ACR username"

$AcrPassword = az acr credential show `
    --resource-group $ResourceGroupName `
    --name $ContainerRegistryName `
    --query "passwords[0].value" -o tsv
Assert-AzSuccess "Get ACR password"

az containerapp create `
    --name $AppName `
    --resource-group $ResourceGroupName `
    --environment $ContainerAppEnvName `
    --image "$AcrLoginServer/agenti-web:latest" `
    --registry-server $AcrLoginServer `
    --registry-username $AcrUsername `
    --registry-password $AcrPassword `
    --target-port 8080 `
    --ingress external `
    --secrets "db-conn=$ConnectionString" `
    --env-vars "ConnectionStrings__DefaultConnection=secretref:db-conn" "ASPNETCORE_ENVIRONMENT=Production" `
    --min-replicas 0 `
    --max-replicas 1 `
    --output none
Assert-AzSuccess "Create Container App"

# Get the Container App URL
$AppUrl = az containerapp show `
    --name $AppName `
    --resource-group $ResourceGroupName `
    --query "properties.configuration.ingress.fqdn" -o tsv
Assert-AzSuccess "Get Container App URL"

Write-Host "  Container App created." -ForegroundColor Green

# ============================================================
# Summary
# ============================================================
Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Resources Created:" -ForegroundColor White
Write-Host "  - Resource Group: $ResourceGroupName"
Write-Host "  - Virtual Network: $VNetName"
Write-Host "  - PostgreSQL Container: $PostgresContainerName (IP: $PostgresIP)"
Write-Host "  - Container Registry: $AcrLoginServer"
Write-Host "  - Container Apps Environment: $ContainerAppEnvName"
Write-Host "  - Container App: $AppName"
Write-Host ""
Write-Host "App URL: https://$AppUrl" -ForegroundColor Green
Write-Host ""
Write-Host "=== Credentials ===" -ForegroundColor Red
Write-Host "  PostgreSQL Password: $PostgresPassword" -ForegroundColor Red
Write-Host "  ACR Login Server: $AcrLoginServer" -ForegroundColor Red
Write-Host "  IMPORTANT: Save these credentials securely. They will not be shown again." -ForegroundColor Red
Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Yellow
Write-Host "1. Create a Service Principal for GitHub Actions:"
Write-Host "   az ad sp create-for-rbac --name 'agenti-github-actions' --role contributor ``"
Write-Host "       --scopes /subscriptions/$($account.id)/resourceGroups/$ResourceGroupName ``"
Write-Host "       --json-auth"
Write-Host ""
Write-Host "2. Add the JSON output as a GitHub secret named 'AZURE_CREDENTIALS'"
Write-Host ""
Write-Host "3. See scripts/azure/setup-secrets.md for detailed instructions"
Write-Host ""
Write-Host "=== Important ===" -ForegroundColor Yellow
Write-Host "PostgreSQL data is ephemeral and will be lost if the container restarts." -ForegroundColor Yellow
Write-Host "For production use, migrate to Azure Database for PostgreSQL Flexible Server." -ForegroundColor Yellow
