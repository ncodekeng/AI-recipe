using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public async Task Azure_result_is_enriched_from_its_exact_cited_publisher_page()
    {
        const string sourceUrl = "https://publisher.example.test/chicken-potato";
        var recipePayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            recipes = new[]
            {
                new
                {
                    title = "Chicken Potato Traybake",
                    cookingMinutes = 35,
                    difficulty = "Easy",
                    cuisine = "British",
                    servings = 2,
                    tags = new[] { "Dinner" },
                    ingredients = new[]
                    {
                        new { amount = "2", name = "chicken", originalText = "2 chicken breasts" },
                        new { amount = "500 g", name = "potato", originalText = "500 g potatoes" }
                    },
                    cookingGuideSteps = new[] { "Cook the listed ingredients and verify the chicken is safely cooked." },
                    sourceUrl,
                    winePairing = "A light Pinot Noir is a rough match."
                }
            }
        });
        var azurePayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            output = new object[]
            {
                new
                {
                    type = "web_search_call",
                    action = new { sources = new[] { new { type = "url", url = sourceUrl } } }
                },
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = recipePayload,
                            annotations = new[] { new { type = "url_citation", url = sourceUrl } }
                        }
                    }
                }
            }
        });
        const string publisherHtml = """
            <script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Chicken and Potato Traybake",
              "image": "https://publisher.example.test/chicken-potato.jpg",
              "prepTime": "PT10M",
              "cookTime": "PT35M",
              "totalTime": "PT45M",
              "recipeYield": "3 servings",
              "recipeIngredient": ["2 chicken breasts", "500 g potatoes", "1 onion"]
            }
            </script>
            """;
        var azureHandler = new JsonHandler(azurePayload);
        var publisherHandler = new JsonHandler(publisherHtml, mediaType: "text/html");
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Provider = "AzureWebSearch",
            AzureWebSearch = new AzureWebSearchOptions
            {
                CandidateCount = 1,
                MinimumResultCount = 1,
                BatchSize = 1,
                MaxSearchAttempts = 1
            },
            PublisherExtraction = new PublisherExtractionOptions { Enabled = true },
            CommercialImages = new CommercialImageOptions
            {
                Enabled = false,
                AllowUnverifiedForTesting = true,
                AllowedProviders = []
            }
        });
        var foodOptions = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions
        {
            AzureOpenAI = new AzureOpenAiOptions
            {
                Endpoint = "https://azure.example.test",
                ApiKey = "test-key",
                Deployment = "test-deployment"
            }
        });
        var service = CreateService(
            options,
            azureHandler,
            imageHandler: null,
            publisherHandler: publisherHandler,
            foodAiOptions: foodOptions,
            environment: new TestHostEnvironment("Development"));

        var response = await service.FindRecipesAsync(new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("chicken", "2"), new IngredientInput("potato", "500 g")],
            MainIngredient = "chicken",
            MaxRecipes = 3,
            ShowPhotos = true
        }, CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.True(recipe.SourceVerified);
        Assert.True(recipe.PublisherPageVerified);
        Assert.Equal("Chicken and Potato Traybake", recipe.Title);
        Assert.Equal(3, recipe.Ingredients.Count);
        Assert.Equal(10, recipe.PrepMinutes);
        Assert.Equal(35, recipe.CookMinutes);
        Assert.Equal(45, recipe.CookingMinutes);
        Assert.Equal(3, recipe.Servings);
        Assert.Equal("https://publisher.example.test/chicken-potato.jpg", recipe.ImageUrl);
        Assert.Equal("Recipe publisher", recipe.ImageProvider);
        Assert.Equal(RecipeImageRightsStatuses.UnverifiedTestOnly, recipe.ImageRightsStatus);
        Assert.Equal(1, azureHandler.CallCount);
        Assert.Equal(1, publisherHandler.CallCount);
    }

    private static RecipeCatalogService CreateService(
        Microsoft.Extensions.Options.IOptions<RecipeCatalogOptions> options,
        HttpMessageHandler? recipeHandler = null,
        HttpMessageHandler? imageHandler = null,
        HttpMessageHandler? publisherHandler = null,
        Microsoft.Extensions.Options.IOptions<FoodAiOptions>? foodAiOptions = null,
        IHostEnvironment? environment = null)
    {
        foodAiOptions ??= Microsoft.Extensions.Options.Options.Create(new FoodAiOptions());
        environment ??= new TestHostEnvironment();
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
                recipeHandler is null ? new HttpClient() : new HttpClient(recipeHandler),
                foodAiOptions,
                options,
                prompts,
                normalizer,
                new RecipeRankingService(normalizer),
                NullLogger<AzureGroundedRecipeClient>.Instance),
            new EdamamRecipeClient(recipeHttpClient, options, normalizer),
            new PublisherRecipePageClient(
                publisherHandler is null
                    ? new HttpClient(new JsonHandler("Not found", HttpStatusCode.NotFound))
                    : new HttpClient(publisherHandler),
                imageMemoryCache,
                options,
                environment,
                normalizer,
                NullLogger<PublisherRecipePageClient>.Instance),
            new CommercialRecipeImageClient(
                imageHandler is null
                    ? new HttpClient { BaseAddress = new Uri("https://commons.wikimedia.org/") }
                    : new HttpClient(imageHandler) { BaseAddress = new Uri("https://commons.wikimedia.org/") },
                options,
                environment,
                photoCache,
                NullLogger<CommercialRecipeImageClient>.Instance),
            new RecipeSafetyValidator(),
            new RecipeRankingService(normalizer),
            cache,
            options,
            NullLogger<RecipeCatalogService>.Instance);
    }

    private sealed class JsonHandler(
        string payload,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "application/json") : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, mediaType)
            });
        }
    }
}
