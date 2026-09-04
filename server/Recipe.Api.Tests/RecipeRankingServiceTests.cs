using System.Text.Json;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class RecipeRankingServiceTests
{
    private readonly IngredientNormalizer _normalizer = new();

    [Theory]
    [InlineData("chicken", "chicken breast")]
    [InlineData("pepper", "red bell peppers")]
    [InlineData("onion", "yellow onions")]
    public void Normalizer_matches_related_ingredient_names(string pantry, string required)
    {
        Assert.True(_normalizer.Matches(pantry, required));
    }

    [Fact]
    public void Normalizer_does_not_treat_meat_as_stock()
    {
        Assert.False(_normalizer.Matches("chicken", "chicken stock"));
    }

    [Fact]
    public void Match_finds_missing_ingredients_and_ignores_pantry_basics()
    {
        var service = new RecipeRankingService(_normalizer);
        var match = service.CalculateMatch(
            [new IngredientInput("Chicken", "2 pieces")],
            [
                new RecipeIngredient("2", "Chicken breast", 2, "piece"),
                new RecipeIngredient("2 cloves", "Garlic", 2, "clove"),
                new RecipeIngredient("to taste", "Sea salt"),
                new RecipeIngredient("1 tbsp", "Extra virgin olive oil"),
                new RecipeIngredient("100 ml", "Water")
            ]);

        Assert.Equal(2, match.RequiredIngredientCount);
        Assert.Equal(1, match.AvailableIngredientCount);
        Assert.Equal(50, match.MatchPercentage);
        var missing = Assert.Single(match.MissingIngredients);
        Assert.Equal("Garlic", missing.Name);
        Assert.Equal(2, missing.Quantity);
        Assert.Equal("clove", missing.Unit);
    }

    [Fact]
    public void Ranking_prefers_a_complete_match_when_available()
    {
        var service = new RecipeRankingService(_normalizer);
        var pantry = new[]
        {
            new IngredientInput("chicken", "2"),
            new IngredientInput("pepper", "2"),
            new IngredientInput("onion", "1")
        };
        var recipes = new[]
        {
            Recipe("Chicken stuffed peppers", "chicken breast", "red bell pepper", "onion", "feta cheese"),
            Recipe("Chicken and onion", "chicken", "onion")
        };

        var ranked = service.Rank(recipes, pantry);

        Assert.Equal("Chicken and onion", ranked[0].Title);
        Assert.Equal(100, ranked[0].IngredientMatch);
        Assert.Empty(ranked[0].MissingIngredients!);
        Assert.Equal("feta cheese", Assert.Single(ranked[1].MissingIngredients!).Name);
    }

    [Fact]
    public void Top_pick_is_a_complete_match_when_available()
    {
        var service = new RecipeRankingService(_normalizer);
        var exact = Recipe("Provider-favourite omelette", "egg");
        var oneMissing = Recipe("Spinach omelette", "egg", "spinach");
        var twoMissing = Recipe("Feta spinach omelette", "egg", "spinach", "feta");

        var ranked = service.Rank(
            [exact, twoMissing, oneMissing],
            [new IngredientInput("egg", "4")]);

        Assert.Equal(exact.Id, ranked[0].Id);
        Assert.Equal(100, ranked[0].IngredientMatch);
        Assert.Empty(ranked[0].MissingIngredients!);
    }

    [Fact]
    public void Ranking_places_a_traditional_near_match_before_a_complete_match()
    {
        var service = new RecipeRankingService(_normalizer);
        var traditional = Recipe("Traditional chicken pie", "chicken", "onion", "pastry") with
        {
            Tags = ["Traditional"]
        };
        var complete = Recipe("Chicken and onion", "chicken", "onion");

        var ranked = service.Rank(
            [complete, traditional],
            [new IngredientInput("chicken", "2"), new IngredientInput("onion", "1")]);

        Assert.Equal(traditional.Id, ranked[0].Id);
        Assert.Single(ranked[0].MissingIngredients!);
        Assert.Equal(complete.Id, ranked[1].Id);
        Assert.Empty(ranked[1].MissingIngredients!);
    }

    [Fact]
    public void Ranking_does_not_promote_a_traditional_recipe_missing_more_than_three_items()
    {
        var service = new RecipeRankingService(_normalizer);
        var distantTraditional = Recipe(
            "Traditional feast",
            "chicken",
            "carrot",
            "celery",
            "leek",
            "potato") with
        {
            Tags = ["Traditional"]
        };
        var complete = Recipe("Roast chicken", "chicken");

        var ranked = service.Rank(
            [distantTraditional, complete],
            [new IngredientInput("chicken", "2")]);

        Assert.Equal(complete.Id, ranked[0].Id);
        Assert.Empty(ranked[0].MissingIngredients!);
        Assert.Equal(4, ranked[1].MissingIngredients!.Count);
    }

    [Fact]
    public void Top_pick_uses_fewest_missing_items_when_no_complete_match_exists()
    {
        var service = new RecipeRankingService(_normalizer);
        var oneMissing = Recipe("Spinach omelette", "egg", "spinach");
        var twoMissing = Recipe("Feta spinach omelette", "egg", "spinach", "feta");

        var ranked = service.Rank(
            [twoMissing, oneMissing],
            [new IngredientInput("egg", "4")]);

        Assert.Equal(oneMissing.Id, ranked[0].Id);
        Assert.Single(ranked[0].MissingIngredients!);
    }

    [Fact]
    public void Top_pick_diversifies_between_equally_close_matches()
    {
        var service = new RecipeRankingService(_normalizer);
        var seen = Recipe("Spinach omelette", "egg", "spinach");
        var fresh = Recipe("Mushroom omelette", "egg", "mushroom");

        var ranked = service.Rank(
            [seen, fresh],
            [new IngredientInput("egg", "4")],
            [seen.Id]);

        Assert.Equal(fresh.Id, ranked[0].Id);
    }

    [Fact]
    public void Cook_with_what_I_have_returns_only_zero_missing_matches()
    {
        var service = new RecipeRankingService(_normalizer);
        var complete = Recipe("Chicken and onion", "chicken", "onion");
        var nearMatch = Recipe("Chicken and spinach", "chicken", "spinach");

        var ranked = service.Rank(
            [nearMatch, complete],
            [new IngredientInput("chicken", "2"), new IngredientInput("onion", "1")],
            onlyUseAvailableIngredients: true);

        Assert.Equal(complete.Id, Assert.Single(ranked).Id);
        Assert.Empty(ranked[0].MissingIngredients!);
        Assert.Equal(100, ranked[0].IngredientMatch);
    }

    [Fact]
    public void Show_all_recipes_keeps_complete_and_near_matches()
    {
        var service = new RecipeRankingService(_normalizer);
        var complete = Recipe("Chicken and onion", "chicken", "onion");
        var nearMatch = Recipe("Chicken and spinach", "chicken", "spinach");

        var ranked = service.Rank(
            [nearMatch, complete],
            [new IngredientInput("chicken", "2"), new IngredientInput("onion", "1")]);

        Assert.Equal(2, ranked.Count);
        Assert.Contains(ranked, recipe => recipe.Id == complete.Id && recipe.MissingIngredients!.Count == 0);
        Assert.Contains(ranked, recipe => recipe.Id == nearMatch.Id && recipe.MissingIngredients!.Count == 1);
    }

    [Fact]
    public void Recipe_serializes_real_image_and_match_fields()
    {
        var service = new RecipeRankingService(_normalizer);
        var ranked = service.Rank(
            [Recipe("Salmon supper", "salmon", "lemon") with
            {
                ImageUrl = "https://upload.wikimedia.org/salmon.jpg",
                ImageSourceUrl = "https://commons.wikimedia.org/wiki/File:Salmon.jpg",
                ImageLicenseType = "CC BY 4.0",
                ImageLicenseUrl = "https://creativecommons.org/licenses/by/4.0/",
                ImageAttributionRequirements = "Credit the photographer and link the source and license.",
                ImageRightsStatus = RecipeImageRightsStatuses.VerifiedCommercial
            }],
            [new IngredientInput("salmon fillets", "2")]);

        var json = JsonSerializer.Serialize(ranked[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"imageUrl\":\"https://upload.wikimedia.org/salmon.jpg\"", json);
        Assert.Contains("\"imageSourceUrl\":\"https://commons.wikimedia.org/wiki/File:Salmon.jpg\"", json);
        Assert.Contains("\"imageLicenseType\":\"CC BY 4.0\"", json);
        Assert.Contains("\"imageLicenseUrl\":\"https://creativecommons.org/licenses/by/4.0/\"", json);
        Assert.Contains("\"imageAttributionRequirements\"", json);
        Assert.Contains("\"imageRightsStatus\":\"VerifiedCommercial\"", json);
        Assert.Contains("\"availableIngredients\"", json);
        Assert.Contains("\"missingIngredients\"", json);
        Assert.Contains("\"ingredientMatch\":50", json);
    }

    private static RecipeSuggestion Recipe(string title, params string[] ingredients) => new(
        Guid.NewGuid(),
        title,
        "A provider recipe.",
        30,
        "Easy",
        "European",
        2,
        0,
        ["Dinner"],
        ingredients.Select(name => new RecipeIngredient("as needed", name)).ToList(),
        [],
        "coral",
        SourceName: "Publisher",
        SourceUrl: "https://publisher.example.test/recipe");
}
