<#
.SYNOPSIS
Creates only a dedicated MCP web app on an existing Linux App Service plan.
.DESCRIPTION
Requires explicit subscription and capacity approval. Does not create a plan or
database, change database passwords/firewalls, set secrets, or deploy code.
Use -WhatIf for read-only target checks; configure the protected connection string
and review docs/MCP_HOSTING.md before enabling deployment.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][guid]$SubscriptionId,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9-]{1,58}[a-zA-Z0-9]$')][string]$McpAppName,
    [string]$ResourceGroupName = 'agenti-rg',
    [string]$AppServicePlanName = 'agenti-plan',
    [string]$BankingAppName = 'agenti',
    [switch]$CapacityApproved
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    param([string[]]$Arguments)
    $result = & az @Arguments --subscription $SubscriptionId --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "Azure operation failed (exit code $LASTEXITCODE). No shared-resource cleanup will be attempted." }
    return $result
}

if ($McpAppName -eq $BankingAppName) { throw 'MCP must use a separate web app.' }
if (!$CapacityApproved -and !$WhatIfPreference) { throw 'Review live CPU, memory and database headroom, then explicitly pass -CapacityApproved.' }

$plan = Invoke-Az @('appservice', 'plan', 'show', '-g', $ResourceGroupName, '-n', $AppServicePlanName, '-o', 'json') | ConvertFrom-Json
if (!$plan.reserved -or $plan.sku.tier -in @('Free', 'Shared')) { throw 'An existing paid Linux plan supporting Always On is required.' }
if ($plan.sku.capacity -ne 1) { throw 'This release uses instance-local quotas. Review concurrency and quotas before deploying on a multi-instance plan.' }
$apps = Invoke-Az @('webapp', 'list', '-g', $ResourceGroupName, '--query',
    "[?name=='$McpAppName'].{name:name,plan:serverFarmId,tags:tags}", '-o', 'json') | ConvertFrom-Json
if ($apps.Count -gt 0 -and ($apps[0].plan -ne $plan.id -or $apps[0].tags.'agenti-component' -ne 'mcp')) {
    throw 'The existing target is not the tagged MCP app on the approved plan. Refusing to modify it.'
}

if (!$PSCmdlet.ShouldProcess("$ResourceGroupName/$McpAppName", "Configure MCP on existing plan $AppServicePlanName without scaling")) { return }
if ($apps.Count -eq 0) {
    Invoke-Az @('webapp', 'create', '-g', $ResourceGroupName, '-p', $plan.id, '-n', $McpAppName,
        '--runtime', 'DOTNETCORE:10.0', '--tags', 'agenti-component=mcp', '-o', 'none')
}
Invoke-Az @('webapp', 'update', '-g', $ResourceGroupName, '-n', $McpAppName, '--https-only', 'true', '-o', 'none')
Invoke-Az @('webapp', 'config', 'set', '-g', $ResourceGroupName, '-n', $McpAppName,
    '--always-on', 'true', '--min-tls-version', '1.2', '--ftps-state', 'Disabled',
    '--startup-file', 'dotnet /home/site/wwwroot/EastSeat.Agenti.Mcp.dll',
    '--generic-configurations', "@$(Join-Path $PSScriptRoot 'mcp-site-config.json')", '-o', 'none')
$hostName = Invoke-Az @('webapp', 'show', '-g', $ResourceGroupName, '-n', $McpAppName, '--query', 'defaultHostName', '-o', 'tsv')
Invoke-Az @('webapp', 'config', 'appsettings', 'set', '-g', $ResourceGroupName, '-n', $McpAppName,
    '--settings', 'ASPNETCORE_ENVIRONMENT=Production', "AllowedHosts=$hostName",
    'AGENTI__MaxRows=1000', 'AGENTI__CommandTimeoutSeconds=30',
    'AGENTI__RequestsPerMinute=30', 'AGENTI__MaxConcurrentRequests=2', '-o', 'none')

Write-Host "MCP app configured: https://$hostName/mcp"
Write-Host 'Set AGENTI__ConnectionString privately, verify schema/grants/networking, then approve the separate MCP deployment.'
