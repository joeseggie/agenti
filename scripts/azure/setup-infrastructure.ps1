<#
.SYNOPSIS
    Sets up Azure infrastructure for Agenti application.

.DESCRIPTION
    Creates all required Azure resources for hosting the Agenti application:
    - Resource Group
    - Azure Container Instance for PostgreSQL
    - Azure App Service Plan (Basic B1)
    - Azure Web App
    - Virtual Network for secure communication

.PARAMETER ResourceGroupName
    Name of the Azure Resource Group (default: agenti-rg)

.PARAMETER Location
    Azure region for resources (default: eastus)

.PARAMETER PostgresPassword
    Password for PostgreSQL database

.PARAMETER AppServicePlanName
    Name of the App Service Plan (default: agenti-plan)

.PARAMETER WebAppName
    Name of the Web App (default: agenti-web)

.EXAMPLE
    .\setup-infrastructure.ps1 -PostgresPassword "YourSecurePassword123!"
#>

param(
    [string]$ResourceGroupName = "agenti-rg",
    [string]$Location = "uaenorth",
    [Parameter(Mandatory=$true)]
    [string]$PostgresPassword,
    [string]$AppServicePlanName = "agenti-plan",
    [string]$WebAppName = "agenti",
    [string]$PostgresContainerName = "agenti-postgres"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Agenti Azure Infrastructure Setup ===" -ForegroundColor Cyan
Write-Host ""

# Check if Azure CLI is installed
try {
    az version | Out-Null
} catch {
    Write-Error "Azure CLI is not installed. Please install it from https://aka.ms/installazurecli"
    exit 1
}

# Check if logged in
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Please log in to Azure..." -ForegroundColor Yellow
    az login
}

Write-Host "Using subscription: $($account.name)" -ForegroundColor Green
Write-Host ""

# 1. Create Resource Group
Write-Host "[1/6] Creating Resource Group: $ResourceGroupName..." -ForegroundColor Yellow
az group create `
    --name $ResourceGroupName `
    --location $Location `
    --output none
Write-Host "  Resource Group created." -ForegroundColor Green

# 2. Create Virtual Network for secure communication
Write-Host "[2/6] Creating Virtual Network..." -ForegroundColor Yellow
$VNetName = "agenti-vnet"
$SubnetAppService = "appservice-subnet"
$SubnetContainer = "container-subnet"

az network vnet create `
    --resource-group $ResourceGroupName `
    --name $VNetName `
    --address-prefix 10.0.0.0/16 `
    --subnet-name $SubnetContainer `
    --subnet-prefix 10.0.1.0/24 `
    --output none

az network vnet subnet create `
    --resource-group $ResourceGroupName `
    --vnet-name $VNetName `
    --name $SubnetAppService `
    --address-prefix 10.0.2.0/24 `
    --delegations Microsoft.Web/serverFarms `
    --output none

Write-Host "  Virtual Network created." -ForegroundColor Green

# 3. Create Azure Container Instance for PostgreSQL
Write-Host "[3/6] Creating PostgreSQL Container Instance..." -ForegroundColor Yellow

# Get subnet ID for container
$ContainerSubnetId = az network vnet subnet show `
    --resource-group $ResourceGroupName `
    --vnet-name $VNetName `
    --name $SubnetContainer `
    --query id -o tsv

az container create `
    --resource-group $ResourceGroupName `
    --name $PostgresContainerName `
    --image postgres:16-alpine `
    --cpu 1 `
    --memory 1.5 `
    --ports 5432 `
    --environment-variables `
        POSTGRES_USER=agenti_user `
        POSTGRES_DB=agenti_prod `
    --secure-environment-variables `
        POSTGRES_PASSWORD=$PostgresPassword `
    --ip-address Private `
    --subnet $ContainerSubnetId `
    --output none

# Get the private IP of the container
$PostgresIP = az container show `
    --resource-group $ResourceGroupName `
    --name $PostgresContainerName `
    --query ipAddress.ip -o tsv

Write-Host "  PostgreSQL Container created at IP: $PostgresIP" -ForegroundColor Green

# 4. Create App Service Plan
Write-Host "[4/6] Creating App Service Plan (Basic B1)..." -ForegroundColor Yellow
az appservice plan create `
    --resource-group $ResourceGroupName `
    --name $AppServicePlanName `
    --sku B1 `
    --is-linux `
    --output none
Write-Host "  App Service Plan created." -ForegroundColor Green

# 5. Create Web App
Write-Host "[5/6] Creating Web App..." -ForegroundColor Yellow
az webapp create `
    --resource-group $ResourceGroupName `
    --plan $AppServicePlanName `
    --name $WebAppName `
    --runtime "DOTNETCORE:10.0" `
    --output none

# Configure VNet integration
$AppServiceSubnetId = az network vnet subnet show `
    --resource-group $ResourceGroupName `
    --vnet-name $VNetName `
    --name $SubnetAppService `
    --query id -o tsv

az webapp vnet-integration add `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --vnet $VNetName `
    --subnet $SubnetAppService `
    --output none

Write-Host "  Web App created with VNet integration." -ForegroundColor Green

# 6. Configure App Settings
Write-Host "[6/6] Configuring App Settings..." -ForegroundColor Yellow

$ConnectionString = "Server=$PostgresIP;Port=5432;Database=agenti_prod;User Id=agenti_user;Password=$PostgresPassword;"

az webapp config connection-string set `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --connection-string-type PostgreSQL `
    --settings DefaultConnection="$ConnectionString" `
    --output none

az webapp config appsettings set `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --settings `
        ASPNETCORE_ENVIRONMENT=Production `
        WEBSITE_RUN_FROM_PACKAGE=1 `
    --output none

Write-Host "  App Settings configured." -ForegroundColor Green

Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Resources Created:" -ForegroundColor White
Write-Host "  - Resource Group: $ResourceGroupName"
Write-Host "  - Virtual Network: $VNetName"
Write-Host "  - PostgreSQL Container: $PostgresContainerName (IP: $PostgresIP)"
Write-Host "  - App Service Plan: $AppServicePlanName (B1)"
Write-Host "  - Web App: $WebAppName"
Write-Host ""
Write-Host "Web App URL: https://$WebAppName.azurewebsites.net" -ForegroundColor Green
Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Yellow
Write-Host "1. Create a Service Principal for GitHub Actions:"
Write-Host "   az ad sp create-for-rbac --name 'agenti-github-actions' --role contributor \"
Write-Host "       --scopes /subscriptions/<subscription-id>/resourceGroups/$ResourceGroupName \"
Write-Host "       --json-auth"
Write-Host ""
Write-Host "2. Add the JSON output as a GitHub secret named 'AZURE_CREDENTIALS'"
Write-Host ""
Write-Host "3. See scripts/azure/setup-secrets.md for detailed instructions"
