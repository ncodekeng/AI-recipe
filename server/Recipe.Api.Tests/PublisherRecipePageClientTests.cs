using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class PublisherRecipePageClientTests
{
    [Fact]
    public async Task Enriches_recipe_from_matching_publisher_json_ld()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@graph": [
                { "@type": "WebPage", "name": "Dinner" },
                {
                  "@type": ["Recipe", "Thing"],
                  "name": "Chicken and Potato Traybake",
                  "image": { "url": "/images/chicken-potato.jpg" },
                  "prepTime": "PT15M",
                  "cookTime": "PT30M",
                  "totalTime": "PT45M",
                  "recipeYield": "4 servings",
                  "nutrition": { "calories": "420 calories" },
                  "recipeIngredient": [
                    "2 chicken breasts",
                    "500 g potatoes",
                    "1 tbsp olive oil"
                  ],
                  "recipeInstructions": [
                    { "@type": "HowToStep", "text": "Publisher method is not copied." }
                  ]
                }
              ]
            }
            </script>
            </head></html>
            """;
        var handler = new PageHandler(html);
        var client = CreateClient(handler, "Development", allowTestImages: true);
        var original = Recipe("Chicken Potato Traybake");

        var response = await client.EnrichAsync(
            new RecipeGenerationResponse([original], "Azure Web Search", "Safety"),
            new GenerateRecipesRequest
            {
                Ingredients = [new IngredientInput("chicken", "2"), new IngredientInput("potato", "500 g")],
                MainIngredient = "chicken"
            },
            CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.True(recipe.PublisherPageVerified);
        Assert.Equal("Chicken and Potato Traybake", recipe.Title);
        Assert.Equal(3, recipe.Ingredients.Count);
        Assert.Contains(recipe.Ingredients, ingredient => ingredient.Name.Equals("chicken", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(15, recipe.PrepMinutes);
        Assert.Equal(30, recipe.CookMinutes);
        Assert.Equal(45, recipe.CookingMinutes);
        Assert.Equal(4, recipe.Servings);
        Assert.Equal(420, recipe.CaloriesPerServing);
        Assert.Equal(RecipeDirectionsKinds.AiGenerated, recipe.DirectionsKind);
        Assert.Equal(original.Steps, recipe.Steps);
        Assert.Contains("Verified structured recipe data", response.Notice);
        Assert.Equal("https://publisher.example.test/images/chicken-potato.jpg", recipe.PublisherImageCandidateUrl);
        Assert.DoesNotContain("chicken-potato.jpg", JsonSerializer.Serialize(recipe), StringComparison.Ordinal);

        var testImage = client.CreateUnverifiedPublisherImageForTesting(recipe);
        Assert.NotNull(testImage);
        Assert.Equal("Recipe publisher", testImage.Provider);
        Assert.False(testImage.IsVerified);
        Assert.False(testImage.CommercialUseAllowed);
    }

    [Fact]
    public async Task Rejects_json_ld_for_a_different_recipe()
    {
        const string html = """
            <script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Chocolate Cake",
              "recipeIngredient": ["flour", "cocoa", "sugar"]
            }
            </script>
            """;
        var handler = new PageHandler(html);
        var client = CreateClient(handler);
        var original = Recipe("Chicken Potato Traybake");

        var response = await client.EnrichAsync(
            new RecipeGenerationResponse([original], "Azure Web Search", "Safety"),
            Request(),
            CancellationToken.None);

        Assert.False(Assert.Single(response.Recipes).PublisherPageVerified);
    }

    [Fact]
    public async Task Enriches_legacy_recipe_microdata_and_resolves_relative_image()
    {
        const string html = """
            <html><body>
            <div itemscope itemtype="http://schema.org/Recipe">
              <h1 itemprop="name">Spanish Paprika Beef</h1>
              <time itemprop="totalTime">70 mins</time>
              <span itemprop="recipeYield">4-6 people</span>
              <meta itemprop=" image" content="/media/976_11846_x.jpg" />
              <ul>
                <li itemprop="ingredients">1 kg topside joint, boned and rolled</li>
                <li itemprop="ingredients">2 red onions</li>
                <li itemprop="ingredients">1 tbsp smoked paprika</li>
              </ul>
              <div itemprop="recipeInstructions">Publisher method is not copied.</div>
            </div>
            </body></html>
            """;
        var handler = new PageHandler(html);
        var client = CreateClient(handler, "Development", allowTestImages: true);
        var original = Recipe("Spanish Paprika Beef") with
        {
            Ingredients =
            [
                new RecipeIngredient("1 kg", "topside joint"),
                new RecipeIngredient("2", "red onions"),
                new RecipeIngredient("1 tbsp", "smoked paprika")
            ],
            SourceName = "Abel & Cole",
            SourceUrl = "https://www.abelandcole.co.uk/recipes/spanish-paprika-beef"
        };

        var response = await client.EnrichAsync(
            new RecipeGenerationResponse([original], "Azure Web Search", "Safety"),
            new GenerateRecipesRequest
            {
                Ingredients =
                [
                    new IngredientInput("topside joint", "1 kg"),
                    new IngredientInput("red onion", "2"),
                    new IngredientInput("smoked paprika", "1 tbsp")
                ]
            },
            CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.True(recipe.PublisherPageVerified);
        Assert.Equal("Spanish Paprika Beef", recipe.Title);
        Assert.Equal(70, recipe.CookingMinutes);
        Assert.Equal(4, recipe.Servings);
        Assert.Equal(3, recipe.Ingredients.Count);
        Assert.Equal(
            "https://www.abelandcole.co.uk/media/976_11846_x.jpg",
            recipe.PublisherImageCandidateUrl);
        Assert.Equal(original.Steps, recipe.Steps);

        var image = client.CreateUnverifiedPublisherImageForTesting(recipe);
        Assert.NotNull(image);
        Assert.Equal(recipe.PublisherImageCandidateUrl, image.ImageUrl);
        Assert.Equal("Unverified publisher image", image.LicenseType);
    }

    [Fact]
    public async Task Never_requests_a_private_or_local_source_url()
    {
        var handler = new PageHandler("<html></html>");
        var client = CreateClient(handler);
        var original = Recipe("Chicken Potato Traybake") with
        {
            SourceUrl = "https://127.0.0.1/recipe"
        };

        var response = await client.EnrichAsync(
            new RecipeGenerationResponse([original], "Azure Web Search", "Safety"),
            Request(),
            CancellationToken.None);

        Assert.False(Assert.Single(response.Recipes).PublisherPageVerified);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Caches_a_successful_page_without_recipe_data()
    {
        var handler = new PageHandler("<html><body>No structured recipe</body></html>");
        var client = CreateClient(handler);
        var original = Recipe("Chicken Potato Traybake");
        var response = new RecipeGenerationResponse([original], "Azure Web Search", "Safety");

        await client.EnrichAsync(response, Request(), CancellationToken.None);
        await client.EnrichAsync(response, Request(), CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Production_never_maps_an_unlicensed_publisher_image()
    {
        var client = CreateClient(new PageHandler("<html></html>"), "Production", allowTestImages: true);
        var recipe = Recipe("Chicken Potato Traybake") with
        {
            PublisherPageVerified = true,
            PublisherImageCandidateUrl = "https://publisher.example.test/food.jpg"
        };

        Assert.Null(client.CreateUnverifiedPublisherImageForTesting(recipe));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.2.3.4", false)]
    [InlineData("172.20.1.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.10.20", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Public_address_filter_blocks_non_public_networks(string value, bool expected)
    {
        Assert.Equal(expected, SafePublisherHttpMessageHandler.IsPublicAddress(IPAddress.Parse(value)));
    }

    private static PublisherRecipePageClient CreateClient(
        HttpMessageHandler handler,
        string environment = "Production",
        bool allowTestImages = false)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            PublisherExtraction = new PublisherExtractionOptions
            {
                Enabled = true,
                CacheEnabled = true,
                CacheDurationHours = 24,
                CacheMaxEntries = 50
            },
            Cache = new RecipeCacheOptions
            {
                Enabled = true,
                ProviderPermissionConfirmed = true
            },
            CommercialImages = new CommercialImageOptions
            {
                AllowUnverifiedForTesting = allowTestImages
            }
        });
        return new PublisherRecipePageClient(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 50 }),
            options,
            new TestHostEnvironment(environment),
            new IngredientNormalizer(),
            NullLogger<PublisherRecipePageClient>.Instance);
    }

    private static GenerateRecipesRequest Request() => new()
    {
        Ingredients = [new IngredientInput("chicken", "2"), new IngredientInput("potato", "500 g")],
        MainIngredient = "chicken"
    };

    private static RecipeSuggestion Recipe(string title) => new(
        Guid.Parse("2740508f-2387-4d63-9608-a64b1e7761d7"),
        title,
        "Real recipe.",
        35,
        "Easy",
        "British",
        2,
        0,
        ["Web grounded"],
        [new RecipeIngredient("2", "chicken"), new RecipeIngredient("500 g", "potato")],
        ["Keep this separately labelled AI cooking guide."],
        "sage",
        SourceName: "Example Publisher",
        SourceUrl: "https://publisher.example.test/chicken-potato",
        DirectionsKind: RecipeDirectionsKinds.AiGenerated,
        SourceVerified: true);

    private sealed class PageHandler(
        string payload,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "text/html") : HttpMessageHandler
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
