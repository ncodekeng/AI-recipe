[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'server\Recipe.Api'
$apiAssembly = Join-Path $apiProject "bin\$Configuration\net10.0\Recipe.Api.dll"
if (-not (Test-Path -LiteralPath $apiAssembly)) {
    throw 'Build the API before running the smoke test.'
}

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$baseUrl = "http://localhost:$port"
$previousAspNetCoreUrls = $env:ASPNETCORE_URLS
$previousDailyRecipeLimit = $env:UsageControl__DailyRecipeLimit
$env:ASPNETCORE_URLS = $baseUrl
$env:UsageControl__DailyRecipeLimit = '3'
$apiProcess = Start-Process dotnet -ArgumentList @($apiAssembly) -WorkingDirectory $apiProject -WindowStyle Hidden -PassThru

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        try {
            $status = Invoke-RestMethod -Uri "$baseUrl/api/status" -TimeoutSec 2
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 700
        }
    }

    if (-not $ready) {
        throw "The API did not become ready on $baseUrl."
    }

    $runId = [guid]::NewGuid().ToString('N')
    $clientId = "smoke-primary-$runId"
    $clientHeaders = @{ 'X-Plate-Client-Id' = $clientId }

    $imagePath = Join-Path $env:TEMP ('mise-smoke-' + [guid]::NewGuid().ToString() + '.png')
    [IO.File]::WriteAllBytes(
        $imagePath,
        [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nWQAAAAASUVORK5CYII='))

    try {
        $analysisJson = curl.exe -s -H "X-Plate-Client-Id: $clientId" -F "photos=@$imagePath;type=image/png" "$baseUrl/api/ingredients/analyze"
        $analysis = $analysisJson | ConvertFrom-Json
    }
    finally {
        if (Test-Path -LiteralPath $imagePath) {
            Remove-Item -LiteralPath $imagePath -Force
        }
    }

    if ($analysis.ingredients.Count -lt 1) {
        throw 'Ingredient analysis returned no ingredients.'
    }

    $recipeBody = @{
        ingredients = @(
            @{ name = 'Tomatoes'; quantity = '4' },
            @{ name = 'Eggs'; quantity = '6' },
            @{ name = 'Cheddar cheese'; quantity = '200 g' },
            @{ name = 'Spinach'; quantity = '1 bag' }
        )
        allergens = @('Eggs', 'Milk')
        dietaryPreference = 'Vegan'
        maxCookingMinutes = 30
        servings = 2
    } | ConvertTo-Json -Depth 5

    $recipeRefused = $false
    try {
        Invoke-RestMethod `
            -Uri "$baseUrl/api/recipes/generate" `
            -Method Post `
            -Headers $clientHeaders `
            -ContentType 'application/json' `
            -Body $recipeBody | Out-Null
    }
    catch {
        $recipeStatusCode = [int]$_.Exception.Response.StatusCode
        $recipeRefused = $recipeStatusCode -eq 503
    }

    if (-not $recipeRefused) {
        throw 'Recipe search did not clearly refuse to fabricate a recipe without online-provider credentials.'
    }

    $usage = Invoke-RestMethod `
        -Uri "$baseUrl/api/usage" `
        -Headers $clientHeaders

    if ($usage.scansUsed -ne 1 -or $usage.recipesUsed -ne 1) {
        throw "Usage tracking failed: scans=$($usage.scansUsed), recipes=$($usage.recipesUsed)."
    }

    $basketBody = @{
        recipeId = [guid]::NewGuid()
        ingredients = @(
            @{ name = 'Garlic'; amount = '2 cloves'; quantity = 2; unit = 'clove' },
            @{ name = 'Feta cheese'; amount = '100 g'; quantity = 100; unit = 'g' }
        )
    } | ConvertTo-Json -Depth 5
    $basket = Invoke-RestMethod `
        -Uri "$baseUrl/api/grocery/deliveroo/basket" `
        -Method Post `
        -Headers $clientHeaders `
        -ContentType 'application/json' `
        -Body $basketBody
    if ($basket.basketCreated -or $basket.checkoutUrl -or $basket.ingredients.Count -ne 2) {
        throw 'The Deliveroo manual handoff returned an invalid basket claim or ingredient list.'
    }

    $feedbackHeaders = @{ 'X-Plate-Client-Id' = "smoke-feedback-$runId" }
    $feedbackBody = @{ rating = 5; message = 'Smoke test feedback' } | ConvertTo-Json
    $feedback = Invoke-RestMethod `
        -Uri "$baseUrl/api/feedback" `
        -Method Post `
        -Headers $feedbackHeaders `
        -ContentType 'application/json' `
        -Body $feedbackBody
    if ($feedback.status -ne 'received') {
        throw 'Feedback submission failed.'
    }

    try {
        Invoke-RestMethod `
            -Uri "$baseUrl/api/feedback" `
            -Method Post `
            -Headers $feedbackHeaders `
            -ContentType 'application/json' `
            -Body $feedbackBody | Out-Null
        throw 'The feedback rate limit was not enforced.'
    }
    catch {
        $feedbackStatusCode = [int]$_.Exception.Response.StatusCode
        if ($feedbackStatusCode -ne 429) {
            throw
        }
    }

    $quotaHeaders = @{ 'X-Plate-Client-Id' = "smoke-quota-$runId" }
    1..3 | ForEach-Object {
        try {
            Invoke-RestMethod `
                -Uri "$baseUrl/api/recipes/generate" `
                -Method Post `
                -Headers $quotaHeaders `
                -ContentType 'application/json' `
                -Body $recipeBody | Out-Null
            throw 'An unsourced recipe was returned during the quota check.'
        }
        catch {
            $setupStatusCode = [int]$_.Exception.Response.StatusCode
            if ($setupStatusCode -ne 503) {
                throw
            }
        }
    }

    try {
        Invoke-RestMethod `
            -Uri "$baseUrl/api/recipes/generate" `
            -Method Post `
            -Headers $quotaHeaders `
            -ContentType 'application/json' `
            -Body $recipeBody | Out-Null
        throw 'The daily generation quota was not enforced.'
    }
    catch {
        $statusCode = [int]$_.Exception.Response.StatusCode
        if ($statusCode -ne 429) {
            throw
        }
    }

    [pscustomobject]@{
        Status = $status.status
        Provider = $status.aiProvider
        DetectedIngredients = $analysis.ingredients.Count
        RecipeSourcePolicy = 'passed'
        GroceryHandoff = 'passed'
        Feedback = 'passed'
        UsageQuota = 'passed'
    } | Format-List
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
    $env:ASPNETCORE_URLS = $previousAspNetCoreUrls
    $env:UsageControl__DailyRecipeLimit = $previousDailyRecipeLimit
}
