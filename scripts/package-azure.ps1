[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$FrontendPath,
    [switch]$CleanInstall
)

$ErrorActionPreference = 'Stop'

# Builds the Azure App Service package: the .NET API with the PLATE frontend served from its wwwroot.
#
# This script used to build 'client/' -- the old "Mise - Make something wonderful" prototype, which is still
# in the repo and still builds cleanly. The live site has served the Base44 app for months, so every package
# this produced was the wrong application, and nothing failed to say so. The frontend is now named once,
# below, and the build is checked before it is packaged.
#
# The frontend lives in .artifacts/, which is gitignored, because it is a separate Base44-managed repository
# rather than part of this one. That is why it is reached by path instead of being a subdirectory.

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'server\Recipe.Api\Recipe.Api.csproj'
$artifactDirectory = Join-Path $projectRoot '.artifacts\azure'
$publishDirectory = Join-Path $artifactDirectory 'publish'

if ([string]::IsNullOrWhiteSpace($FrontendPath)) {
    $FrontendPath = Join-Path $projectRoot '.artifacts\plate-base44-fullstack'
}
elseif (-not [IO.Path]::IsPathRooted($FrontendPath)) {
    $FrontendPath = Join-Path $projectRoot $FrontendPath
}

# Dated by default. The previous default was a bare 'plate.zip', so a new package silently replaced the last
# one and there was no way to tell from the filename which application or which day was inside it.
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $artifactDirectory ('plate-base44-' + (Get-Date -Format 'yyyyMMdd') + '.zip')
}
elseif (-not [IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $projectRoot $OutputPath
}

foreach ($commandName in @('node', 'npm', 'dotnet')) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$commandName' was not found."
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $FrontendPath 'package.json'))) {
    throw "No frontend found at '$FrontendPath'. Pass -FrontendPath to point at the PLATE frontend repository."
}

# Guards against being pointed back at the Mise prototype, or at any other Vite app that happens to be there.
if (-not (Test-Path -LiteralPath (Join-Path $FrontendPath 'base44\config.jsonc'))) {
    throw "'$FrontendPath' has no base44/config.jsonc, so it is not the PLATE frontend. Check -FrontendPath."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

# Built in place rather than staged into a scratch copy. The Base44 app carries a dozen config files
# (base44/, tailwind, postcss, jsconfig, components.json, .env.production) and a staged copy that misses one
# of them still builds -- just differently, which is the whole class of bug this script already shipped once.
$nodeModules = Join-Path $FrontendPath 'node_modules'
if ($CleanInstall -or -not (Test-Path -LiteralPath $nodeModules)) {
    Write-Host 'Installing frontend dependencies...'
    if (Test-Path -LiteralPath (Join-Path $FrontendPath 'package-lock.json')) {
        & npm ci --prefix $FrontendPath
    }
    else {
        & npm install --prefix $FrontendPath
    }
    if ($LASTEXITCODE -ne 0) {
        throw 'Installing frontend dependencies failed.'
    }
}

Write-Host 'Building the PLATE frontend...'
& npm run build --prefix $FrontendPath
if ($LASTEXITCODE -ne 0) {
    throw 'The frontend build failed.'
}

$distDirectory = Join-Path $FrontendPath 'dist'
if (-not (Test-Path -LiteralPath (Join-Path $distDirectory 'index.html'))) {
    throw "The build produced no dist/index.html in '$distDirectory'."
}

# What the bundle has to contain, checked before it is packaged rather than after it is live.
#
# The login host is the important one. A build that never saw VITE_BASE44_APP_BASE_URL still succeeds: the
# Base44 SDK normalises the missing value to an empty string and builds a bare '/login', which resolves
# against whatever host serves the page and 404s on Azure. That shipped on 17 November 2026 and took sign-in
# down. .env.production holds the value, and this proves it was actually applied.
$bundleText = (Get-ChildItem -LiteralPath (Join-Path $distDirectory 'assets') -Filter '*.js' |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"

if ($bundleText -notmatch 'plate-app\.co\.uk') {
    throw 'The bundle does not contain the Base44 login host, so sign-in would 404. Check that .env.production exists in the frontend and sets VITE_BASE44_APP_BASE_URL.'
}
if ($bundleText -match 'Make something wonderful') {
    throw 'The bundle is the old Mise prototype, not PLATE. Check -FrontendPath.'
}

Write-Host 'Publishing the ASP.NET Core API...'
& dotnet publish $apiProject --configuration Release --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

$webRoot = Join-Path $publishDirectory 'wwwroot'
New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
Copy-Item -Path (Join-Path $distDirectory '*') -Destination $webRoot -Recurse -Force

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

Write-Host 'Creating the Azure App Service package...'

# Written entry by entry rather than with Compress-Archive, which on Windows PowerShell 5.1 records paths
# with backslashes. The ZIP specification requires forward slashes (APPNOTE 4.4.17.1), and a reader that
# follows it treats "wwwroot\index.html" as a file whose name contains a backslash rather than as
# wwwroot/index.html -- so the site deploys as a handful of oddly named files at the root and serves
# nothing. Azure's own extractor happens to tolerate it; not every tool in the chain will.
Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$publishRoot = (Resolve-Path -LiteralPath $publishDirectory).Path.TrimEnd('\') + '\'
$zipStream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File) {
            $entryName = $file.FullName.Substring($publishRoot.Length).Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            try {
                $fileStream = [IO.File]::OpenRead($file.FullName)
                try { $fileStream.CopyTo($entryStream) }
                finally { $fileStream.Dispose() }
            }
            finally { $entryStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }

# Named so the deploy can be checked against what is actually live:
#   curl -s https://<host>/ | Select-String -Pattern 'assets/index-.*\.js'
$mainBundle = (Get-Content -LiteralPath (Join-Path $distDirectory 'index.html') -Raw |
    Select-String -Pattern 'assets/index-[^"]+\.js' -AllMatches).Matches |
    ForEach-Object { $_.Value } | Select-Object -First 1

Write-Host "Package ready: $OutputPath"
Write-Host "Frontend bundle: $mainBundle"
Write-Host ''
Write-Host 'Deploy by uploading the zip:'
Write-Host '  Portal > the App Service > Advanced Tools (Kudu) > Go > Tools > Zip Push Deploy,'
Write-Host '  then drag the zip onto the file list. It replaces site/wwwroot.'
Write-Host ''
Write-Host 'Afterwards check the live page serves the bundle named above, not the previous one.'
