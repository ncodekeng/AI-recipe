namespace Recipe.Api.Options;

public sealed class RecipeCatalogOptions
{
    public const string SectionName = "RecipeCatalog";

    public string Provider { get; init; } = "AzureWebSearch";
    public AzureWebSearchOptions AzureWebSearch { get; init; } = new();
    public EdamamOptions Edamam { get; init; } = new();
    public RecipeCacheOptions Cache { get; init; } = new();
}

public sealed class AzureWebSearchOptions
{
    public int MaxToolCalls { get; init; } = 4;
    public int MaxOutputTokens { get; init; } = 4000;
    public int CandidateCount { get; init; } = 8;
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

public sealed class RecipeCacheOptions
{
    public bool Enabled { get; init; }
    public bool ProviderPermissionConfirmed { get; init; }
    public int DurationHours { get; init; } = 168;
    public int MaxEntries { get; init; } = 500;

    public bool CanStoreProviderData => Enabled && ProviderPermissionConfirmed;
}
