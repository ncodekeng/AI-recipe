[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$clientDirectory = Join-Path $projectRoot 'client'
$apiProject = Join-Path $projectRoot 'server\Recipe.Api\Recipe.Api.csproj'
$artifactDirectory = Join-Path $projectRoot '.artifacts\azure'
$clientBuildDirectory = Join-Path $artifactDirectory 'client'
$publishDirectory = Join-Path $artifactDirectory 'publish'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $artifactDirectory 'plate.zip'
}
elseif (-not [IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $projectRoot $OutputPath
}

foreach ($commandName in @('node', 'npm', 'dotnet')) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$commandName' was not found."
    }
}

foreach ($temporaryDirectory in @($clientBuildDirectory, $publishDirectory)) {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $clientBuildDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

foreach ($clientFile in @('package.json', 'package-lock.json', 'index.html', 'vite.config.js')) {
    Copy-Item `
        -LiteralPath (Join-Path $clientDirectory $clientFile) `
        -Destination $clientBuildDirectory
}

Copy-Item `
    -LiteralPath (Join-Path $clientDirectory 'src') `
    -Destination $clientBuildDirectory `
    -Recurse

$publicDirectory = Join-Path $clientDirectory 'public'
if (Test-Path -LiteralPath $publicDirectory) {
    Copy-Item -LiteralPath $publicDirectory -Destination $clientBuildDirectory -Recurse
}

Write-Host 'Building the React client...'
& npm ci --prefix $clientBuildDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'npm ci failed.'
}

& npm run build --prefix $clientBuildDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'The React build failed.'
}

Write-Host 'Publishing the ASP.NET Core API...'
& dotnet publish $apiProject --configuration Release --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

$webRoot = Join-Path $publishDirectory 'wwwroot'
New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
Copy-Item -Path (Join-Path $clientBuildDirectory 'dist\*') -Destination $webRoot -Recurse -Force

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

Write-Host 'Creating the Azure App Service package...'
Push-Location $publishDirectory
try {
    Compress-Archive -Path * -DestinationPath $OutputPath -CompressionLevel Optimal
}
finally {
    Pop-Location
}

Write-Host "Package ready: $OutputPath"
