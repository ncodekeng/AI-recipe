using System.Net;
using System.Text;
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
        var service = CreateService(options);

        var exception = await Assert.ThrowsAsync<RecipeCatalogException>(() =>
            service.FindRecipesAsync(new GenerateRecipesRequest
            {
                Ingredients = [new IngredientInput("lamb", "500 g")]
            }, CancellationToken.None));

        Assert.Contains("will not invent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Available_only_returns_an_empty_success_when_every_recipe_needs_more_ingredients()
    {
        const string payload = """
            {
              "hits": [{
                "recipe": {
                  "uri": "recipe_near_match",
                  "label": "Chicken with Garlic",
                  "url": "https://publisher.example.test/chicken-garlic",
                  "source": "Example Kitchen",
                  "yield": 2,
                  "totalTime": 25,
                  "ingredients": [
                    { "text": "300 g chicken", "food": "chicken", "quantity": 300, "measure": "g" },
                    { "text": "2 garlic cloves", "food": "garlic", "quantity": 2, "measure": "clove" }
                  ],
                  "instructionLines": [],
                  "cuisineType": ["British"],
                  "dietLabels": [],
                  "healthLabels": []
                }
              }]
            }
            """;
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Provider = "Edamam",
            Edamam = new EdamamOptions { AppId = "test-id", AppKey = "test-key" }
        });
        var service = CreateService(options, new JsonHandler(payload));

        var response = await service.FindRecipesAsync(new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("chicken", "300 g")],
            OnlyUseAvailableIngredients = true,
            ShowPhotos = false
        }, CancellationToken.None);

        Assert.Empty(response.Recipes);
        Assert.Contains("No recipes found using only what you have", response.Notice);
    }

    private static RecipeCatalogService CreateService(
        Microsoft.Extensions.Options.IOptions<RecipeCatalogOptions> options,
        HttpMessageHandler? recipeHandler = null)
    {
        var foodAiOptions = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions());
        var normalizer = new IngredientNormalizer();
        var prompts = new TestPromptProvider();
        var cache = new RecipeSearchCache(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 }),
            normalizer,
            options,
            prompts,
            NullLogger<RecipeSearchCache>.Instance);
        var recipeHttpClient = recipeHandler is null
            ? new HttpClient()
            : new HttpClient(recipeHandler);
        recipeHttpClient.BaseAddress = new Uri("https://api.edamam.com/");

        return new RecipeCatalogService(
            new AzureGroundedRecipeClient(
                new HttpClient(),
                foodAiOptions,
                options,
                prompts,
                NullLogger<AzureGroundedRecipeClient>.Instance),
            new EdamamRecipeClient(recipeHttpClient, options),
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
    }

    private sealed class JsonHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
    }
}
