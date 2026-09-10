using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class RecipeSafetyValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://publisher.example.test/recipe")]
    public void Validator_rejects_recipes_without_an_https_source(string? sourceUrl)
    {
        var response = new RecipeGenerationResponse(
            [Recipe(sourceUrl)],
            "Untrusted provider",
            string.Empty);

        Assert.Throws<RecipeSafetyException>(() =>
            new RecipeSafetyValidator().Validate(response, new GenerateRecipesRequest
            {
                Ingredients = [new IngredientInput("lamb", "500 g")]
            }));
    }

    [Fact]
    public void Validator_accepts_a_sourced_online_recipe()
    {
        var response = new RecipeGenerationResponse(
            [Recipe("https://publisher.example.test/recipe")],
            "Edamam",
            string.Empty);

        var result = new RecipeSafetyValidator().Validate(response, new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("lamb", "500 g")]
        });

        Assert.Single(result.Recipes);
    }

    [Fact]
    public void Validator_rejects_a_source_that_was_not_provider_verified()
    {
        var recipe = Recipe("https://publisher.example.test/recipe") with { SourceVerified = false };
        var response = new RecipeGenerationResponse([recipe], "Untrusted provider", string.Empty);

        Assert.Throws<RecipeSafetyException>(() =>
            new RecipeSafetyValidator().Validate(response, new GenerateRecipesRequest
            {
                Ingredients = [new IngredientInput("lamb", "500 g")]
            }));
    }

    private static RecipeSuggestion Recipe(string? sourceUrl) => new(
        Guid.NewGuid(),
        "Braised lamb",
        "A sourced recipe.",
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
        SourceUrl: sourceUrl,
        SourceVerified: true);
}
