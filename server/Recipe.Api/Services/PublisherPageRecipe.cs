namespace Recipe.Api.Services;

/// <summary>
/// The facts a publisher states about one recipe in its own structured data.
///
/// Notably absent: the cooking method. Nothing here is ever copied from a publisher's prose -- these are
/// the things a recipe simply is (what it is called, what goes in it, how long it takes, how many it
/// feeds), which is why they can be read and restated. The method is the publisher's writing and stays
/// theirs; PLATE links to it and writes its own.
/// </summary>
public sealed record PublisherPageRecipe(
    string Title,
    IReadOnlyList<string> IngredientLines,
    int? PrepMinutes,
    int? CookMinutes,
    int? TotalMinutes,
    int? Servings,
    int? CaloriesPerServing,
    string? ImageUrl);
