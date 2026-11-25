namespace Recipe.Api.Options;

public sealed class FoodAiOptions
{
    public const string SectionName = "FoodAi";

    public string Provider { get; init; } = "Demo";
    public bool UseDemoFallback { get; init; } = true;
    public AzureOpenAiOptions AzureOpenAI { get; init; } = new();
    public IngredientScanCacheOptions ScanCache { get; init; } = new();
    public CookingGuideOptions CookingGuide { get; init; } = new();
    public QueryPlanningOptions QueryPlanning { get; init; } = new();
}

/// <summary>
/// Asking a model what to search for, which is under investigation rather than in use.
///
/// Deterministic by default: RecipeSearchPlanner and CuisineDishes do the same job from a dictionary and a
/// dish table, for nothing, in no measurable time. This exists so the alternative can be measured on the
/// same baskets through the same extraction, validation and ranking.
/// </summary>
public sealed class QueryPlanningOptions
{
    /// <summary>Deterministic, Llm, or Hybrid.</summary>
    public string Mode { get; init; } = "Deterministic";

    /// <summary>Enough for six short queries and the JSON around them.</summary>
    public int MaxOutputTokens { get; init; } = 600;

    /// <summary>
    /// A deadline for the one call. Short on purpose: past this the deterministic plan is already in hand
    /// and waiting longer costs the customer more than the better queries could be worth.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 8;

    /// <summary>
    /// In Hybrid, a basket of at least this many ingredients counts as a complex combination worth asking
    /// about. Below it the dish table handles the common cases perfectly well.
    /// </summary>
    public int ComplexBasketSize { get; init; } = 5;

    public bool UsesLlm =>
        Mode.Equals("Llm", StringComparison.OrdinalIgnoreCase) ||
        Mode.Equals("Hybrid", StringComparison.OrdinalIgnoreCase);

    public bool IsHybrid => Mode.Equals("Hybrid", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ask a model to re-plan when a batch comes back weak, rather than guessing beforehand which baskets
    /// are hard.
    ///
    /// Off by default and unbenchmarked. It exists because the Hybrid triggers were measured and did not
    /// work: they fired on 2 of 17 baskets and neither was one of the two the model actually helped. What
    /// predicted the model's advantage was the quality of the results, which is only knowable afterwards.
    ///
    /// Applied to "show more results" rather than the first batch, by default, because the first batch is
    /// what the customer is waiting for and a second search would double its latency. A later tap is
    /// already slower and already a reach for something new.
    /// </summary>
    public bool ResultQualityFallback { get; init; }

    /// <summary>Allow the fallback on the first batch too, at the cost of the customer's wait.</summary>
    public bool FallbackOnFirstBatch { get; init; }

    /// <summary>
    /// Fewer verified recipes than asked for counts as a weak batch. At or above this fraction of the
    /// target, the batch stands.
    /// </summary>
    public double FallbackVerifiedFraction { get; init; } = 1.0;

    /// <summary>
    /// Below this fraction of results matching the cuisine asked for, the batch counts as weak. Only
    /// applied when a cuisine was actually chosen, and only when the publishers stated one.
    /// </summary>
    public double FallbackCuisineFraction { get; init; } = 0.5;
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
