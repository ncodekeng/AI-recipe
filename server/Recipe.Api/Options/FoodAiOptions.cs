namespace Recipe.Api.Options;

public sealed class FoodAiOptions
{
    public const string SectionName = "FoodAi";

    public string Provider { get; init; } = "Demo";
    public bool UseDemoFallback { get; init; } = true;
    public AzureOpenAiOptions AzureOpenAI { get; init; } = new();
    public IngredientScanCacheOptions ScanCache { get; init; } = new();
    public CookingGuideOptions CookingGuide { get; init; } = new();
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

/// <summary>
/// Writing PLATE's own cooking guide for dishes whose facts were read from a publisher's page.
///
/// Separate from the search options because the guide is the one part of a recipe that genuinely needs a
/// model: the ingredients, timings and yield are stated by the publisher and are read for free, and the
/// publisher's method is theirs and is never copied.
/// </summary>
public sealed class CookingGuideOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Enough for a batch of four guides with room to spare. Truncation is the failure mode that cost the
    /// old pipeline whole searches, so this is set loosely rather than tightly.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 3000;

    /// <summary>A deadline for the one call. Past this the recipes are returned without a guide.</summary>
    public int TimeoutSeconds { get; init; } = 25;

    /// <summary>Below this it is not a guide, and no guide beats a one-line one that says "cook it".</summary>
    public int MinSteps { get; init; } = 3;

    public int MaxSteps { get; init; } = 9;
}
