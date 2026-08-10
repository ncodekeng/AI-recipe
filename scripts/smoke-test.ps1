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

    $imagePath = Join-Path $env:TEMP ('mise-smoke-' + [guid]::NewGuid().ToString() + '.png')
    [IO.File]::WriteAllBytes(
        $imagePath,
        [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nWQAAAAASUVORK5CYII='))

    try {
        $analysisJson = curl.exe -s -F "photos=@$imagePath;type=image/png" 'http://localhost:5050/api/ingredients/analyze'
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

    [pscustomobject]@{
        Status = $status.status
        Provider = $status.aiProvider
        DetectedIngredients = $analysis.ingredients.Count
        Recipes = $generated.recipes.Count
        AllergyFilter = 'passed'
    } | Format-List
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}
