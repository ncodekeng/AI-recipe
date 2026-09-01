using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class EdamamRecipeClient(
    HttpClient httpClient,
    IOptions<RecipeCatalogOptions> options)
{
    private static readonly IReadOnlyDictionary<string, string> AllergenHealthLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Peanuts"] = "peanut-free",
            ["Tree nuts"] = "tree-nut-free",
            ["Milk"] = "dairy-free",
            ["Eggs"] = "egg-free",
            ["Gluten cereals"] = "gluten-free",
            ["Soy"] = "soy-free",
            ["Fish"] = "fish-free",
            ["Crustaceans"] = "crustacean-free",
            ["Molluscs"] = "mollusk-free",
            ["Sesame"] = "sesame-free",
            ["Celery"] = "celery-free",
            ["Mustard"] = "mustard-free",
            ["Lupin"] = "lupine-free",
            ["Sulphites"] = "sulfite-free"
        };

    private readonly EdamamOptions _options = options.Value.Edamam;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<RecipeGenerationResponse> FindRecipesAsync(
        GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Edamam recipe search is not configured.");
        }

        var query = BuildQuery(request);
        using var response = await httpClient.GetAsync(query, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EdamamSearchResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Edamam returned an empty response.");

        var recipes = payload.Hits
            .Select((hit, index) => MapRecipe(hit.Recipe, request, index))
            .Where(recipe => recipe is not null)
            .Cast<RecipeSuggestion>()
            .Take(3)
            .ToList();

        if (recipes.Count == 0)
        {
            throw new RecipeSafetyException(
                "No real recipes matched these ingredients and restrictions. Try removing an optional restriction or adding more ingredients.");
        }

        return new RecipeGenerationResponse(
            recipes,
            "Edamam",
            string.Empty,
            "Real recipes are shown from their original publishers. Open the source for the complete method and verify every product label.");
    }

    private string BuildQuery(GenerateRecipesRequest request)
    {
        var ingredientQuery = string.Join(", ", request.Ingredients
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6));
        if (ingredientQuery.Length > 100)
        {
            ingredientQuery = ingredientQuery[..100];
        }

        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("type", "public"),
            new("q", ingredientQuery),
            new("app_id", _options.AppId),
            new("app_key", _options.AppKey),
            new("time", $"1-{request.MaxCookingMinutes}"),
            new("imageSize", "REGULAR"),
            new("field", "uri"),
            new("field", "label"),
            new("field", "url"),
            new("field", "source"),
            new("field", "yield"),
            new("field", "ingredientLines"),
            new("field", "ingredients"),
            new("field", "totalTime"),
            new("field", "cuisineType"),
            new("field", "dietLabels"),
            new("field", "healthLabels"),
            new("field", "image")
        };

        foreach (var healthLabel in GetHealthLabels(request))
        {
            parameters.Add(new("health", healthLabel));
        }

        return QueryHelpers.AddQueryString("api/recipes/v2", parameters);
    }

    private static IEnumerable<string> GetHealthLabels(GenerateRecipesRequest request)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var allergen in request.Allergens)
        {
            if (AllergenHealthLabels.TryGetValue(allergen.Trim(), out var label))
            {
                labels.Add(label);
            }
        }

        switch (request.DietaryPreference.Trim().ToLowerInvariant())
        {
            case "vegan":
                labels.Add("vegan");
                break;
            case "vegetarian":
                labels.Add("vegetarian");
                break;
            case "pescatarian":
                labels.Add("pecatarian");
                break;
            case "gluten-free":
                labels.Add("gluten-free");
                break;
            case "dairy-free":
                labels.Add("dairy-free");
                break;
            case "halal-style":
                labels.Add("alcohol-free");
                labels.Add("pork-free");
                break;
            case "kosher-style":
                labels.Add("kosher");
                break;
        }

        return labels;
    }

    private static RecipeSuggestion? MapRecipe(
        EdamamRecipe recipe,
        GenerateRecipesRequest request,
        int index)
    {
        if (string.IsNullOrWhiteSpace(recipe.Label) ||
            string.IsNullOrWhiteSpace(recipe.Url) ||
            !Uri.TryCreate(recipe.Url, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var ingredients = recipe.Ingredients
            .Select(item => new RecipeIngredient(
                string.IsNullOrWhiteSpace(item.Text) ? FormatAmount(item.Quantity, item.Measure) : string.Empty,
                FirstNotEmpty(item.Text, item.Food, "Ingredient")))
            .ToList();
        if (ingredients.Count == 0)
        {
            ingredients = recipe.IngredientLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => new RecipeIngredient(string.Empty, line.Trim()))
                .ToList();
        }

        var missing = ingredients
            .Where(item => !PantryContains(request.Ingredients, item.Name))
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var match = ingredients.Count == 0
            ? 0
            : (int)Math.Round(100d * (ingredients.Count - missing.Count) / ingredients.Count);
        var tags = recipe.DietLabels
            .Concat(recipe.HealthLabels)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (tags.Count == 0)
        {
            tags.Add("Real recipe");
        }

        return new RecipeSuggestion(
            StableGuid(recipe.Uri ?? recipe.Url),
            recipe.Label.Trim(),
            $"A real recipe from {FirstNotEmpty(recipe.Source, sourceUri.Host, "the original publisher")}, matched against your kitchen.",
            recipe.TotalTime > 0 ? (int)Math.Round(recipe.TotalTime) : 0,
            "Source recipe",
            recipe.CuisineType.FirstOrDefault() ?? "International",
            recipe.Yield > 0 ? Math.Max(1, (int)Math.Round(recipe.Yield)) : request.Servings,
            match,
            tags,
            ingredients,
            [],
            AccentFor(index),
            missing,
            FirstNotEmpty(recipe.Source, sourceUri.Host, "Original publisher"),
            recipe.Url,
            ValidHttpsUrl(recipe.Image));
    }

    private static bool PantryContains(IEnumerable<IngredientInput> pantry, string recipeIngredient)
    {
        var recipeTokens = Tokens(recipeIngredient);
        return pantry.Any(item =>
        {
            var pantryTokens = Tokens(item.Name);
            return recipeTokens.Intersect(pantryTokens, StringComparer.OrdinalIgnoreCase).Any();
        });
    }

    private static IEnumerable<string> Tokens(string value) =>
        value.ToLowerInvariant()
            .Split([' ', ',', '-', '(', ')', '/', '&'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Length > 3 && token.EndsWith('s') ? token[..^1] : token)
            .Where(token => token.Length >= 3 && token is not "fresh" and not "large" and not "small");

    private static string FormatAmount(double quantity, string? measure)
    {
        var amount = quantity > 0
            ? quantity.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        return string.Join(' ', new[] { amount, measure?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash[..16]);
    }

    private static string FirstNotEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string? ValidHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.ToString()
            : null;

    private static string AccentFor(int index) => (index % 3) switch
    {
        0 => "coral",
        1 => "saffron",
        _ => "sage"
    };

    private sealed class EdamamSearchResponse
    {
        [JsonPropertyName("hits")]
        public List<EdamamHit> Hits { get; init; } = [];
    }

    private sealed class EdamamHit
    {
        [JsonPropertyName("recipe")]
        public EdamamRecipe Recipe { get; init; } = new();
    }

    private sealed class EdamamRecipe
    {
        [JsonPropertyName("uri")]
        public string? Uri { get; init; }

        [JsonPropertyName("label")]
        public string Label { get; init; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("image")]
        public string? Image { get; init; }

        [JsonPropertyName("yield")]
        public double Yield { get; init; }

        [JsonPropertyName("totalTime")]
        public double TotalTime { get; init; }

        [JsonPropertyName("ingredientLines")]
        public List<string> IngredientLines { get; init; } = [];

        [JsonPropertyName("ingredients")]
        public List<EdamamIngredient> Ingredients { get; init; } = [];

        [JsonPropertyName("cuisineType")]
        public List<string> CuisineType { get; init; } = [];

        [JsonPropertyName("dietLabels")]
        public List<string> DietLabels { get; init; } = [];

        [JsonPropertyName("healthLabels")]
        public List<string> HealthLabels { get; init; } = [];
    }

    private sealed class EdamamIngredient
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("food")]
        public string? Food { get; init; }

        [JsonPropertyName("quantity")]
        public double Quantity { get; init; }

        [JsonPropertyName("measure")]
        public string? Measure { get; init; }
    }
}
