using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Recipe.Api.Models;

// A record, not a class, and deliberately.
//
// RecipeCatalogService rebuilds this after applying dietary restrictions. Built by hand, that copy left
// out CuisinePreference and RecipePreferences, so a chosen cuisine and every preference chip reached the
// search as empty strings -- the selections did nothing at all, silently, and nothing failed to say so.
// `request with { ... }` copies every property by default, so a field added here cannot be forgotten
// there. See ApplyDietaryRestrictions.
public sealed record GenerateRecipesRequest
{
    public const int MaxIngredientCount = 100;

    // No MinLength: an empty basket is legitimate when DiscoverByPreference is set, which is what the
    // Inspire Me screen sends and what "No scanning needed" on that screen promises. Model-level
    // validation cannot see the other field, so the rule lives in RecipesController where both are known.
    [MaxLength(MaxIngredientCount)]
    public List<IngredientInput> Ingredients { get; init; } = [];
    public List<string> Allergens { get; init; } = [];
    public List<string> AvoidIngredients { get; init; } = [];
    public List<Guid> RecentlyShownRecipeIds { get; init; } = [];
    public string DietaryPreference { get; init; } = "Anything";

    [MaxLength(60)]
    public string CuisinePreference { get; init; } = string.Empty;

    [MaxLength(10)]
    public List<string> RecipePreferences { get; init; } = [];

    [MaxLength(100)]
    public string MainIngredient { get; init; } = string.Empty;

    // Zero means that the user selected "Unlimited".
    [Range(0, 240)]
    public int MaxCookingMinutes { get; init; } = 45;

    [Range(1, 12)]
    public int Servings { get; init; } = 2;

    public bool ShowPhotos { get; init; } = true;

    [Range(3, 5)]
    public int MaxRecipes { get; init; } = 5;

    public bool OnlyUseAvailableIngredients { get; init; }

    // The three below are sent by the app and were missing from this model entirely, so they were
    // discarded at deserialization and never reached the search. The meal type picker, the "Anything
    // else?" box and Inspire Me itself all had no effect on the result.

    /// <summary>Breakfast, Lunch, Dinner, Snack, Soup, Salad, Sandwich, or empty for any.</summary>
    [MaxLength(40)]
    public string MealType { get; init; } = string.Empty;

    /// <summary>
    /// Inspire Me: the basket is empty on purpose and the search is driven by the preferences instead.
    /// Without this an empty basket is indistinguishable from a scan that lost its ingredients.
    /// </summary>
    public bool DiscoverByPreference { get; init; }

    /// <summary>Free text from the "Anything else?" box. Treated as data, never as instructions.</summary>
    [MaxLength(100)]
    public string CustomInstructions { get; init; } = string.Empty;
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
    string ImageRightsStatus = RecipeImageRightsStatuses.Unavailable,
    int? CaloriesPerServing = null,
    string? MatchReason = null,
    bool SourceVerified = false,
    string? ImageProvider = null,
    string? ImageCreator = null,
    bool ImageCommercialUseAllowed = false,
    bool ImageAttributionRequired = false,
    bool ImageVerified = false,
    string? SourceTitle = null,
    int? PrepMinutes = null,
    int? CookMinutes = null,
    bool PublisherPageVerified = false,
    [property: JsonIgnore] string? PublisherImageCandidateUrl = null);

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

public sealed class RecipePhotoLookupRequest
{
    [MinLength(1)]
    [MaxLength(6)]
    public List<RecipePhotoCandidate> Recipes { get; init; } = [];
}

public sealed record RecipePhotoCandidate(
    Guid Id,
    string Title,
    string? SourceUrl = null,
    bool PublisherPageVerified = false);

public sealed record RecipePhotoLookupResult(
    Guid Id,
    string? ImageUrl,
    string? ImageSourceUrl,
    string? ImageLicenseType,
    string? ImageLicenseUrl,
    string? ImageAttributionRequirements,
    string ImageRightsStatus,
    string? ImageProvider = null,
    string? ImageCreator = null,
    bool ImageCommercialUseAllowed = false,
    bool ImageAttributionRequired = false,
    bool ImageVerified = false);
