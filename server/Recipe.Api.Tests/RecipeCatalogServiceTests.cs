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
        var imageHandler = new JsonHandler("""{"query":{"pages":[]}}""");
        var service = CreateService(options, new JsonHandler(payload), imageHandler);

        var response = await service.FindRecipesAsync(new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("chicken", "300 g")],
            OnlyUseAvailableIngredients = true,
            ShowPhotos = false
        }, CancellationToken.None);

        Assert.Empty(response.Recipes);
        Assert.Contains("No recipes found using only what you have", response.Notice);
        Assert.Equal(0, imageHandler.CallCount);
    }

    [Fact]
    public async Task Missing_licensed_photo_does_not_fail_a_sourced_recipe()
    {
        const string payload = """
            {"hits":[{"recipe":{"uri":"recipe_1","label":"Roast Salmon","url":"https://publisher.example.test/roast-salmon","source":"Example Kitchen","yield":2,"totalTime":30,"ingredients":[{"text":"2 salmon fillets","food":"salmon","quantity":2,"measure":"fillet"}],"instructionLines":[],"cuisineType":["British"],"dietLabels":[],"healthLabels":[]}}]}
            """;
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Provider = "Edamam",
            Edamam = new EdamamOptions { AppId = "test-id", AppKey = "test-key" }
        });
        var imageHandler = new JsonHandler("""{"query":{"pages":[]}}""");
        var service = CreateService(options, new JsonHandler(payload), imageHandler);

        var response = await service.FindRecipesAsync(new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("salmon", "2")],
            MainIngredient = "salmon",
            ShowPhotos = true
        }, CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.True(recipe.SourceVerified);
        Assert.Null(recipe.ImageUrl);
        Assert.False(recipe.ImageVerified);
        Assert.Equal(1, imageHandler.CallCount);
    }

    private static RecipeCatalogService CreateService(
        Microsoft.Extensions.Options.IOptions<RecipeCatalogOptions> options,
        HttpMessageHandler? recipeHandler = null,
        HttpMessageHandler? imageHandler = null)
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

        var imageMemoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 });
        var photoCache = new RecipePhotoCache(
            imageMemoryCache,
            options,
            NullLogger<RecipePhotoCache>.Instance);
        return new RecipeCatalogService(
            new AzureGroundedRecipeClient(
                new HttpClient(),
                foodAiOptions,
                options,
                prompts,
                normalizer,
                new RecipeRankingService(normalizer),
                NullLogger<AzureGroundedRecipeClient>.Instance),
            new EdamamRecipeClient(recipeHttpClient, options, normalizer),
            new CommercialRecipeImageClient(
                imageHandler is null
                    ? new HttpClient { BaseAddress = new Uri("https://commons.wikimedia.org/") }
                    : new HttpClient(imageHandler) { BaseAddress = new Uri("https://commons.wikimedia.org/") },
                options,
                new TestHostEnvironment(),
                photoCache,
                NullLogger<CommercialRecipeImageClient>.Instance),
            new RecipeSafetyValidator(),
            new RecipeRankingService(normalizer),
            cache,
            options,
            NullLogger<RecipeCatalogService>.Instance);
    }

    private sealed class JsonHandler(string payload) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
