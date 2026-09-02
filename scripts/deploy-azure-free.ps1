[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{1,58}[a-z0-9]$')]
    [string]$AppName,

    [string]$ResourceGroup = 'plate-free-rg',

    [string]$PlanName,

    [string]$Location = 'uksouth',

    [string]$SettingsFile
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$packageScript = Join-Path $PSScriptRoot 'package-azure.ps1'
$packagePath = Join-Path $projectRoot '.artifacts\azure\plate.zip'

if ([string]::IsNullOrWhiteSpace($PlanName)) {
    $planBaseName = if ($AppName.Length -gt 30) { $AppName.Substring(0, 30).TrimEnd('-') } else { $AppName }
    $PlanName = "$planBaseName-free-plan"
}

if ([string]::IsNullOrWhiteSpace($SettingsFile)) {
    $SettingsFile = Join-Path $projectRoot 'azure\appsettings.production.json'
}
elseif (-not [IO.Path]::IsPathRooted($SettingsFile)) {
    $SettingsFile = Join-Path $projectRoot $SettingsFile
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI was not found. Install it, run az login, and retry.'
}

& az account show --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI is not signed in. Run az login and retry.'
}

& $packageScript -OutputPath $packagePath

Write-Host "Creating or updating Azure resources in '$ResourceGroup'..."
& az group create --name $ResourceGroup --location $Location --only-show-errors --output none
if ($LASTEXITCODE -ne 0) {
    throw 'Could not create the Azure resource group.'
}

$existingPlanSku = & az appservice plan show `
    --name $PlanName `
    --resource-group $ResourceGroup `
    --query 'sku.name' `
    --output tsv `
    --only-show-errors 2>$null

if ($LASTEXITCODE -ne 0) {
    & az appservice plan create `
        --name $PlanName `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku F1 `
        --is-linux `
        --only-show-errors `
        --output none

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the free F1 App Service plan. Try a region where F1 is available.'
    }
}
elseif ($existingPlanSku -ne 'F1') {
    throw "The existing plan '$PlanName' uses SKU '$existingPlanSku', not the free F1 SKU. Choose another plan name to avoid unexpected charges."
}

$existingAppPlan = & az webapp show `
    --name $AppName `
    --resource-group $ResourceGroup `
    --query 'serverFarmId' `
    --output tsv `
    --only-show-errors 2>$null

if ($LASTEXITCODE -ne 0) {
    & az webapp create `
        --name $AppName `
        --resource-group $ResourceGroup `
        --plan $PlanName `
        --runtime 'DOTNETCORE:10.0' `
        --https-only true `
        --only-show-errors `
        --output none

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the web app. The app name may already be used globally, or .NET 10/F1 may be unavailable in this region.'
    }
}
elseif (-not $existingAppPlan.EndsWith("/serverfarms/$PlanName", [StringComparison]::OrdinalIgnoreCase)) {
    throw "The existing app '$AppName' is attached to another App Service plan. Choose another app name to avoid changing it."
}

& az webapp config appsettings set `
    --name $AppName `
    --resource-group $ResourceGroup `
    --settings ASPNETCORE_ENVIRONMENT=Production WEBSITE_RUN_FROM_PACKAGE=1 `
    --only-show-errors `
    --output none

if ($LASTEXITCODE -ne 0) {
    throw 'Could not configure the web app.'
}

if (Test-Path -LiteralPath $SettingsFile) {
    Write-Host "Applying private provider settings from '$SettingsFile'..."
    & az webapp config appsettings set `
        --name $AppName `
        --resource-group $ResourceGroup `
        --settings "@$SettingsFile" `
        --only-show-errors `
        --output none

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not apply the provider settings file.'
    }
}
else {
    Write-Host 'No private settings file found; the deployed app will start in credential-free demo mode.'
}

Write-Host 'Deploying the application package...'
& az webapp deploy `
    --name $AppName `
    --resource-group $ResourceGroup `
    --src-path $packagePath `
    --type zip `
    --clean true `
    --restart true `
    --only-show-errors `
    --output none

if ($LASTEXITCODE -ne 0) {
    throw 'Azure App Service ZIP deployment failed.'
}

$hostName = & az webapp show `
    --name $AppName `
    --resource-group $ResourceGroup `
    --query 'defaultHostName' `
    --output tsv `
    --only-show-errors

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($hostName)) {
    throw 'Deployment completed, but the public hostname could not be read.'
}

$publicUrl = "https://$hostName"
$ready = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    try {
        $status = Invoke-RestMethod -Uri "$publicUrl/api/status" -TimeoutSec 8
        if ($status.status -eq 'ok') {
            $ready = $true
            break
        }
    }
    catch {
        Start-Sleep -Seconds 3
    }
}

if ($ready) {
    Write-Host "PLATE is live without Dapr: $publicUrl"
}
else {
    Write-Warning "Azure accepted the deployment, but its F1 instance is still starting. Check $publicUrl in a minute."
}
