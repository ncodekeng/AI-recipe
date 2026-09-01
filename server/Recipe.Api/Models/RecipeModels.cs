using System.ComponentModel.DataAnnotations;

namespace Recipe.Api.Models;

public sealed class GenerateRecipesRequest
{
    [MinLength(1)]
    public List<IngredientInput> Ingredients { get; init; } = [];
    public List<string> Allergens { get; init; } = [];
    public List<string> AvoidIngredients { get; init; } = [];
    public string DietaryPreference { get; init; } = "Anything";

    [Range(10, 180)]
    public int MaxCookingMinutes { get; init; } = 45;

    [Range(1, 12)]
    public int Servings { get; init; } = 2;
}

public sealed record RecipeIngredient(string Amount, string Name);

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
    IReadOnlyList<string>? MissingIngredients = null,
    string? SourceName = null,
    string? SourceUrl = null,
    string? ImageUrl = null);

public sealed record RecipeGenerationResponse(
    IReadOnlyList<RecipeSuggestion> Recipes,
    string Provider,
    string SafetyNote,
    string? Notice = null);
