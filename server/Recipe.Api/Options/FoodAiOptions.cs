namespace Recipe.Api.Options;

public sealed class FoodAiOptions
{
    public const string SectionName = "FoodAi";

    public string Provider { get; init; } = "Demo";
    public bool UseDemoFallback { get; init; } = true;
    public AzureOpenAiOptions AzureOpenAI { get; init; } = new();
    public IngredientScanCacheOptions ScanCache { get; init; } = new();
}

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Deployment { get; init; } = string.Empty;
    public string ImageDetail { get; init; } = "high";
    public int MaxOutputTokensPerImage { get; init; } = 4000;
    public int MaxParallelImages { get; init; } = 2;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Deployment);
}

public sealed class IngredientScanCacheOptions
{
    public bool Enabled { get; init; } = true;
    public int DurationHours { get; init; } = 168;
    public int MaxEntries { get; init; } = 500;
}
