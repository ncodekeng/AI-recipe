$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'server\Recipe.Api'
$apiProcess = Start-Process dotnet -ArgumentList @('run', '--project', $apiProject, '--no-build') -WindowStyle Hidden -PassThru

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        try {
            $status = Invoke-RestMethod -Uri 'http://localhost:5050/api/status' -TimeoutSec 2
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 700
        }
    }

    if (-not $ready) {
        throw 'The API did not become ready on http://localhost:5050.'
    }

    $clientId = 'smoke-primary-client-0001'
    $clientHeaders = @{ 'X-Plate-Client-Id' = $clientId }

    $imagePath = Join-Path $env:TEMP ('mise-smoke-' + [guid]::NewGuid().ToString() + '.png')
    [IO.File]::WriteAllBytes(
        $imagePath,
        [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nWQAAAAASUVORK5CYII='))

    try {
        $analysisJson = curl.exe -s -H "X-Plate-Client-Id: $clientId" -F "photos=@$imagePath;type=image/png" 'http://localhost:5050/api/ingredients/analyze'
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

    $generated = Invoke-RestMethod `
        -Uri 'http://localhost:5050/api/recipes/generate' `
        -Method Post `
        -Headers $clientHeaders `
        -ContentType 'application/json' `
        -Body $recipeBody

    if ($generated.recipes.Count -ne 3) {
        throw "Expected 3 recipes but received $($generated.recipes.Count)."
    }

    $forbidden = $generated.recipes.ingredients.name |
        Where-Object { $_ -match 'egg|cheese|milk|cream|butter' }

    if ($forbidden) {
        throw "The allergen check failed: $($forbidden -join ', ')."
    }

    $usage = Invoke-RestMethod `
        -Uri 'http://localhost:5050/api/usage' `
        -Headers $clientHeaders

    if ($usage.scansUsed -ne 1 -or $usage.recipesUsed -ne 1) {
        throw "Usage tracking failed: scans=$($usage.scansUsed), recipes=$($usage.recipesUsed)."
    }

    $avoidBody = @{
        ingredients = @(
            @{ name = 'Tomatoes'; quantity = '4' },
            @{ name = 'Mushrooms'; quantity = '250 g' },
            @{ name = 'Spinach'; quantity = '1 bag' }
        )
        allergens = @()
        avoidIngredients = @('Tomatoes')
        dietaryPreference = 'Anything'
        maxCookingMinutes = 30
        servings = 2
    } | ConvertTo-Json -Depth 5

    $avoidResult = Invoke-RestMethod `
        -Uri 'http://localhost:5050/api/recipes/generate' `
        -Method Post `
        -Headers @{ 'X-Plate-Client-Id' = 'smoke-avoid-client-0001' } `
        -ContentType 'application/json' `
        -Body $avoidBody

    $avoided = $avoidResult.recipes.ingredients.name |
        Where-Object { $_ -match 'tomato' }
    if ($avoided) {
        throw 'A custom avoided ingredient passed the deterministic safety validator.'
    }

    $feedbackHeaders = @{ 'X-Plate-Client-Id' = 'smoke-feedback-client-0001' }
    $feedbackBody = @{ rating = 5; message = 'Smoke test feedback' } | ConvertTo-Json
    $feedback = Invoke-RestMethod `
        -Uri 'http://localhost:5050/api/feedback' `
        -Method Post `
        -Headers $feedbackHeaders `
        -ContentType 'application/json' `
        -Body $feedbackBody
    if ($feedback.status -ne 'received') {
        throw 'Feedback submission failed.'
    }

    try {
        Invoke-RestMethod `
            -Uri 'http://localhost:5050/api/feedback' `
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

    $quotaHeaders = @{ 'X-Plate-Client-Id' = 'smoke-quota-client-0001' }
    1..3 | ForEach-Object {
        Invoke-RestMethod `
            -Uri 'http://localhost:5050/api/recipes/generate' `
            -Method Post `
            -Headers $quotaHeaders `
            -ContentType 'application/json' `
            -Body $recipeBody | Out-Null
    }

    try {
        Invoke-RestMethod `
            -Uri 'http://localhost:5050/api/recipes/generate' `
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
        Recipes = $generated.recipes.Count
        AllergyFilter = 'passed'
        CustomAvoidFilter = 'passed'
        Feedback = 'passed'
        UsageQuota = 'passed'
    } | Format-List
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}
