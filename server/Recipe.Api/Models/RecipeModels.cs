using System.ComponentModel.DataAnnotations;

namespace Recipe.Api.Models;

public sealed class GenerateRecipesRequest
{
    [MinLength(1)]
    public List<IngredientInput> Ingredients { get; init; } = [];
    public List<string> Allergens { get; init; } = [];
    public List<string> AvoidIngredients { get; init; } = [];
    public List<Guid> RecentlyShownRecipeIds { get; init; } = [];
    public string DietaryPreference { get; init; } = "Anything";

    [Range(10, 180)]
    public int MaxCookingMinutes { get; init; } = 45;

    [Range(1, 12)]
    public int Servings { get; init; } = 2;

    public bool ShowPhotos { get; init; } = true;
}

public sealed record RecipeIngredient(
    string Amount,
    string Name,
    double? Quantity = null,
    string? Unit = null,
    string? OriginalText = null);

public sealed record RecipeSuggestion(
    Guid Id,
    string Title,
    string Description,
    int CookingMinutes,
    string Difficulty,
    string Cuisine,
    int Servings,
    int IngredientMatch,
    IReadOnlyList<string> Tags,
    IReadOnlyList<RecipeIngredient> Ingredients,
    IReadOnlyList<string> Steps,
    string Accent,
    IReadOnlyList<RecipeIngredient>? MissingIngredients = null,
    string? SourceName = null,
    string? SourceUrl = null,
    string? ImageUrl = null,
    IReadOnlyList<RecipeIngredient>? AvailableIngredients = null,
    int RequiredIngredientCount = 0,
    int AvailableIngredientCount = 0,
    string? WinePairing = null,
    string DirectionsKind = RecipeDirectionsKinds.Unavailable,
    string? ImageSourceUrl = null,
    string? ImageLicenseType = null,
    string? ImageLicenseUrl = null,
    string? ImageAttributionRequirements = null,
    string ImageRightsStatus = RecipeImageRightsStatuses.Unavailable);

public static class RecipeDirectionsKinds
{
    public const string Provider = "Provider";
    public const string AiGenerated = "AiGenerated";
    public const string Unavailable = "Unavailable";
}

public static class RecipeImageRightsStatuses
{
    public const string VerifiedCommercial = "VerifiedCommercial";
    public const string UnverifiedTestOnly = "UnverifiedTestOnly";
    public const string Unavailable = "Unavailable";
}

public sealed record RecipeGenerationResponse(
    IReadOnlyList<RecipeSuggestion> Recipes,
    string Provider,
    string SafetyNote,
    string? Notice = null);
