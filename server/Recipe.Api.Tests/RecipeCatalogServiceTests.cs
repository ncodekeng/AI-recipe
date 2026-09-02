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
        var normalizer = new IngredientNormalizer();
        var service = new RecipeCatalogService(
            new EdamamRecipeClient(new HttpClient { BaseAddress = new Uri("https://api.edamam.com/") }, options),
            new RecipeSafetyValidator(),
            new RecipeRankingService(normalizer),
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
