using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class RecipeSearchCacheTests
{
    [Fact]
    public void Cache_is_disabled_without_explicit_provider_permission()
    {
        var cache = CreateCache(new RecipeCacheOptions
        {
            Enabled = true,
            ProviderPermissionConfirmed = false
        });
        var request = Request(["lamb", "onion"]);

        cache.Store(request, Response());

        Assert.False(cache.TryGet(request, out _));
    }

    [Fact]
    public void Cache_reuses_normalized_ingredient_and_safety_settings()
    {
        var cache = CreateCache(new RecipeCacheOptions
        {
            Enabled = true,
            ProviderPermissionConfirmed = true,
            DurationHours = 168
        });
        var original = Request(["Lamb shanks", "Red onions"], ["Milk", "Peanuts"]);
        var equivalent = Request(["onion", "lamb"], ["Peanuts", "MILK"]);
        var response = Response();

        cache.Store(original, response);

        Assert.True(cache.TryGet(equivalent, out var cached));
        Assert.Same(response, cached);
    }

    [Fact]
    public void Cache_does_not_cross_different_safety_settings()
    {
        var cache = CreateCache(new RecipeCacheOptions
        {
            Enabled = true,
            ProviderPermissionConfirmed = true
        });
        cache.Store(Request(["lamb"]), Response());

        Assert.False(cache.TryGet(Request(["lamb"], ["Milk"]), out _));
    }

    [Fact]
    public void Cache_does_not_cross_different_recent_recipe_history()
    {
        var cache = CreateCache(new RecipeCacheOptions
        {
            Enabled = true,
            ProviderPermissionConfirmed = true
        });
        var original = Request(["lamb"]);
        original.RecentlyShownRecipeIds.Add(Guid.NewGuid());
        cache.Store(original, Response());

        Assert.False(cache.TryGet(Request(["lamb"]), out _));
    }

    private static RecipeSearchCache CreateCache(RecipeCacheOptions cacheOptions)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Cache = cacheOptions
        });
        return new RecipeSearchCache(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 }),
            new IngredientNormalizer(),
            options,
            NullLogger<RecipeSearchCache>.Instance);
    }

    private static GenerateRecipesRequest Request(
        string[] ingredients,
        string[]? allergens = null) => new()
        {
            Ingredients = ingredients.Select(name => new IngredientInput(name, "as needed")).ToList(),
            Allergens = (allergens ?? []).ToList(),
            AvoidIngredients = ["celery"],
            DietaryPreference = "Halal-style",
            MaxCookingMinutes = 90,
            Servings = 4
        };

    private static RecipeGenerationResponse Response() => new(
        [new RecipeSuggestion(
            Guid.NewGuid(),
            "Sourced lamb",
            "A real recipe.",
            90,
            "Source recipe",
            "European",
            4,
            0,
            ["Dinner"],
            [new RecipeIngredient("500 g", "lamb")],
            [],
            "coral",
            SourceName: "Publisher",
            SourceUrl: "https://publisher.example.test/lamb")],
        "Edamam",
        "Check labels.");
}
