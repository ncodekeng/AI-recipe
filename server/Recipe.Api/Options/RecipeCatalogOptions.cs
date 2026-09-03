namespace Recipe.Api.Options;

public sealed class RecipeCatalogOptions
{
    public const string SectionName = "RecipeCatalog";

    public string Provider { get; init; } = "AzureWebSearch";
    public AzureWebSearchOptions AzureWebSearch { get; init; } = new();
    public EdamamOptions Edamam { get; init; } = new();
    public CommercialImageOptions CommercialImages { get; init; } = new();
    public RecipeCacheOptions Cache { get; init; } = new();
}

public sealed class AzureWebSearchOptions
{
    public int MaxToolCalls { get; init; } = 4;
    public int MaxOutputTokens { get; init; } = 4000;
    public int CandidateCount { get; init; } = 6;
    public int MinimumResultCount { get; init; } = 6;
    public int BatchSize { get; init; } = 3;
    public int MaxSearchAttempts { get; init; } = 2;
    public string Market { get; init; } = "en-GB";
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
    public int MaxCandidates { get; init; } = 8;
}

public sealed class RecipeCacheOptions
{
    public bool Enabled { get; init; }
    public bool ProviderPermissionConfirmed { get; init; }
    public int DurationHours { get; init; } = 168;
    public int MaxEntries { get; init; } = 500;

    public bool CanStoreProviderData => Enabled && ProviderPermissionConfirmed;
}
