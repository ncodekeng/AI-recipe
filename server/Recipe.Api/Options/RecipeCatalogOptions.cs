namespace Recipe.Api.Options;

public sealed class RecipeCatalogOptions
{
    public const string SectionName = "RecipeCatalog";

    public string Provider { get; init; } = "AzureWebSearch";
    public AzureWebSearchOptions AzureWebSearch { get; init; } = new();
    public GoogleSearchOptions GoogleSearch { get; init; } = new();
    public EdamamOptions Edamam { get; init; } = new();
    public PublisherExtractionOptions PublisherExtraction { get; init; } = new();
    public CommercialImageOptions CommercialImages { get; init; } = new();
    public RecipeCacheOptions Cache { get; init; } = new();
}

public sealed class PublisherExtractionOptions
{
    public bool Enabled { get; init; } = true;
    public int MaxResponseBytes { get; init; } = 2 * 1024 * 1024;
    public int MaxConcurrentRequests { get; init; } = 3;
    public bool CacheEnabled { get; init; }
    public int CacheDurationHours { get; init; } = 24;
    public int CacheMaxEntries { get; init; } = 500;
}

public sealed class AzureWebSearchOptions
{
    public int MaxToolCalls { get; init; } = 4;
    public int MaxOutputTokens { get; init; } = 4000;
    public int CandidateCount { get; init; } = 5;
    public int MinimumResultCount { get; init; } = 5;
    public int BatchSize { get; init; } = 3;
    public int MaxSearchAttempts { get; init; } = 2;
    public string Market { get; init; } = "en-GB";
}

/// <summary>
/// Discovery through Serper's Google Search API. Every limit is configurable rather than compiled in,
/// because the right numbers are a trade between breadth, latency and cost per search, and that balance
/// is a product decision that will move.
/// </summary>
public sealed class GoogleSearchOptions
{
    /// <summary>
    /// Server-side only. Set through configuration or an environment variable; it must never go anywhere
    /// the frontend build can read, unlike the VITE_ settings, which ship inside the bundle.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    public string Endpoint { get; init; } = "https://google.serper.dev/";

    /// <summary>How many planned queries to run for one search. More breadth, more credits.</summary>
    public int MaxQueries { get; init; } = 4;

    /// <summary>Results requested per query. Serper is reported to charge extra beyond ten.</summary>
    public int ResultsPerQuery { get; init; } = 10;

    /// <summary>Queries run together. They are independent, so the slowest sets the pace.</summary>
    public int MaxConcurrentQueries { get; init; } = 4;

    /// <summary>A deadline for the whole discovery step, not per query.</summary>
    public int TimeoutSeconds { get; init; } = 8;

    /// <summary>Google country and language codes, matching the app's en-GB market.</summary>
    public string Country { get; init; } = "gb";

    public string Language { get; init; } = "en";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class EdamamOptions
{
    public string AppId { get; init; } = string.Empty;
    public string AppKey { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) &&
        !string.IsNullOrWhiteSpace(AppKey);
}

public sealed class CommercialImageOptions
{
    public bool Enabled { get; init; } = true;
    public bool AllowUnverifiedForTesting { get; init; }
    public bool AllowUnverifiedPublisherImagesForPrototype { get; init; }
    public int MaxCandidates { get; init; } = 8;
    public int MaxResponseBytes { get; init; } = 2 * 1024 * 1024;
    public bool CacheEnabled { get; init; } = true;
    public int CacheDurationHours { get; init; } = 168;
    public int CacheMaxEntries { get; init; } = 500;
    public string[] AllowedProviders { get; init; } =
        ["WikimediaCommons", "Pexels", "Pixabay"];
}

public sealed class RecipeCacheOptions
{
    public bool Enabled { get; init; }
    public bool ProviderPermissionConfirmed { get; init; }
    public int DurationHours { get; init; } = 168;
    public int MaxEntries { get; init; } = 500;

    public bool CanStoreProviderData => Enabled && ProviderPermissionConfirmed;
}
