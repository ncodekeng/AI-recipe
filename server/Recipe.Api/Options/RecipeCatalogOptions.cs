namespace Recipe.Api.Options;

public sealed class RecipeCatalogOptions
{
    public const string SectionName = "RecipeCatalog";

    public EdamamOptions Edamam { get; init; } = new();
}

public sealed class EdamamOptions
{
    public string AppId { get; init; } = string.Empty;
    public string AppKey { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) &&
        !string.IsNullOrWhiteSpace(AppKey);
}
