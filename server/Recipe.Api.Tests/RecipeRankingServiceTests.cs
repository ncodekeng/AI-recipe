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
    public void Ranking_can_prefer_an_attractive_near_match_over_a_small_complete_match()
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

        Assert.Equal("Chicken stuffed peppers", ranked[0].Title);
        Assert.Equal(75, ranked[0].IngredientMatch);
        Assert.Equal("feta cheese", Assert.Single(ranked[0].MissingIngredients!).Name);
        Assert.Equal(2, ranked[1].AvailableIngredientCount);
    }

    [Fact]
    public void Recipe_serializes_real_image_and_match_fields()
    {
        var service = new RecipeRankingService(_normalizer);
        var ranked = service.Rank(
            [Recipe("Salmon supper", "salmon", "lemon") with { ImageUrl = "https://images.example.test/salmon.jpg" }],
            [new IngredientInput("salmon fillets", "2")]);

        var json = JsonSerializer.Serialize(ranked[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"imageUrl\":\"https://images.example.test/salmon.jpg\"", json);
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
