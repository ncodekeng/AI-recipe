using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class RecipeCatalogServiceTests
{
    [Fact]
    public async Task Missing_catalog_credentials_never_fall_back_to_an_invented_recipe()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions());
        var foodAiOptions = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions());
        var normalizer = new IngredientNormalizer();
        var prompts = new TestPromptProvider();
        var cache = new RecipeSearchCache(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 }),
            normalizer,
            options,
            prompts,
            NullLogger<RecipeSearchCache>.Instance);
        var service = new RecipeCatalogService(
            new AzureGroundedRecipeClient(new HttpClient(), foodAiOptions, options, prompts),
            new EdamamRecipeClient(new HttpClient { BaseAddress = new Uri("https://api.edamam.com/") }, options),
            new CommercialRecipeImageClient(
                new HttpClient { BaseAddress = new Uri("https://commons.wikimedia.org/") },
                options,
                new TestHostEnvironment(),
                NullLogger<CommercialRecipeImageClient>.Instance),
            new RecipeSafetyValidator(),
            new RecipeRankingService(normalizer),
            cache,
            options,
            NullLogger<RecipeCatalogService>.Instance);

        var exception = await Assert.ThrowsAsync<RecipeCatalogException>(() =>
            service.FindRecipesAsync(new GenerateRecipesRequest
            {
                Ingredients = [new IngredientInput("lamb", "500 g")]
            }, CancellationToken.None));

        Assert.Contains("will not invent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
