using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// The inverted pipeline: search for pages, then read them. These tests fix the behaviours that the
/// model-driven path got wrong in measured searches on 24 November 2026 -- it returned dishes that did not
/// contain the ingredient asked for, counted several pages about one dinner as several dinners, and
/// returned publisher index pages as recipes.
/// </summary>
public sealed class GoogleRecipeSearchClientTests
{
    [Fact]
    public async Task Builds_recipes_from_what_each_publisher_states()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://one.test/brisket"] = RecipePage(
                "Slow Cooker Beef Brisket",
                ["1 kg beef brisket", "500 ml golden ale", "2 onions", "1 tbsp flour"],
                totalTime: "PT9H",
                servings: "6",
                image: "/brisket.jpg"),
            ["https://two.test/tacos"] = RecipePage(
                "Beef Brisket Tacos",
                ["800 g beef brisket", "8 corn tortillas", "1 lime", "coriander"],
                totalTime: "PT4H",
                servings: "4"),
        };

        var response = await Run(pages.Keys, pages, Request("beef brisket"));

        Assert.Equal("Google Search", response.Provider);
        Assert.Equal(2, response.Recipes.Count);

        var brisket = response.Recipes[0];
        Assert.Equal("Slow Cooker Beef Brisket", brisket.Title);
        Assert.Equal(540, brisket.CookingMinutes);
        Assert.Equal(6, brisket.Servings);
        Assert.Equal("https://one.test/brisket", brisket.SourceUrl);
        Assert.Equal("one.test", brisket.SourceName);
        Assert.Equal(4, brisket.Ingredients.Count);
        Assert.Contains(brisket.Ingredients, i => i.OriginalText == "1 kg beef brisket");
        Assert.Equal("https://one.test/brisket.jpg", brisket.PublisherImageCandidateUrl);

        // The page was read, which is the entire basis for the recipe existing.
        Assert.True(brisket.SourceVerified);
        Assert.True(brisket.PublisherPageVerified);

        // The method is the publisher's writing and is never copied, so there is none yet.
        Assert.Empty(brisket.Steps);
        Assert.Equal(RecipeDirectionsKinds.Unavailable, brisket.DirectionsKind);
    }

    [Fact]
    public async Task Refuses_a_dish_that_does_not_contain_the_ingredient_asked_for()
    {
        // Measured: three searches for beef brisket returned a beef burger, savoury mince, stroganoff, a
        // stir fry, roast beef and a taco casserole, and no brisket. A publisher's own ingredient list is
        // a statement of fact, so it can settle this where a model's say-so cannot.
        var pages = new Dictionary<string, string>
        {
            ["https://one.test/casserole"] = RecipePage(
                "Beef and Bean Taco Casserole",
                ["500 g beef mince", "1 tin black beans", "tortilla chips", "cheese"]),
            ["https://two.test/brisket"] = RecipePage(
                "Braised Beef Brisket",
                ["1.5 kg beef brisket", "2 carrots", "500 ml stock", "bay leaves"]),
        };

        var response = await Run(pages.Keys, pages, Request("beef brisket"));

        var recipe = Assert.Single(response.Recipes);
        Assert.Equal("Braised Beef Brisket", recipe.Title);
    }

    [Fact]
    public async Task Counts_four_write_ups_of_one_dinner_as_one_dinner()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://a.test/1"] = RecipePage("Slow Cooker Beef Brisket", Brisket),
            ["https://b.test/2"] = RecipePage("Slow-cooker beef brisket", Brisket),
            ["https://c.test/3"] = RecipePage("Easy Slow Cooker Beef Brisket", Brisket),
            ["https://d.test/4"] = RecipePage("Beef Brisket Tacos", Brisket),
        };

        var response = await Run(pages.Keys, pages, Request("beef brisket"));

        Assert.Equal(2, response.Recipes.Count);
        Assert.Contains(response.Recipes, r => r.Title.Contains("Tacos", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Never_reads_a_publisher_index_page()
    {
        // These were 3 of the 33 candidates in the measured Serper run. Rejecting them on the search
        // result alone means the fetch is never paid for.
        var urls = new[]
        {
            "https://www.bbcgoodfood.com/recipes/collection/beef-brisket-recipes",
            "https://one.test/brisket",
        };
        var pages = new Dictionary<string, string>
        {
            ["https://one.test/brisket"] = RecipePage("Braised Beef Brisket", Brisket),
        };
        var fetched = new List<string>();

        var response = await Run(urls, pages, Request("beef brisket"), fetched, listingTitle: "Beef brisket recipes");

        Assert.Single(response.Recipes);
        Assert.DoesNotContain(fetched, url => url.Contains("collection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Stops_reading_pages_once_there_are_enough()
    {
        // Thirty candidates, four wanted, six workers: one wave and then stop. The saving is other
        // people's servers as much as our own latency.
        // Genuinely different dinners, each on its own site, or the dish and domain dedup would collapse
        // them and this would be measuring that instead.
        string[] forms = ["Curry", "Stew", "Pie", "Ramen", "Tacos", "Chilli", "Salad", "Soup", "Hash", "Roast"];
        var urls = Enumerable.Range(0, 30).Select(i => $"https://site{i}.test/dish").ToArray();
        var pages = urls
            .Select((url, i) => (url, html: RecipePage($"Beef Brisket {forms[i % forms.Length]} {i}", Brisket)))
            .ToDictionary(x => x.url, x => x.html);
        var fetched = new List<string>();

        var response = await Run(urls, pages, Request("beef brisket", maxRecipes: 4), fetched);

        Assert.Equal(4, response.Recipes.Count);
        Assert.True(fetched.Count <= 6, $"read {fetched.Count} pages to return 4 recipes");
    }

    [Fact]
    public async Task One_unreadable_page_does_not_lose_the_others()
    {
        var urls = new[] { "https://broken.test/x", "https://one.test/brisket" };
        var pages = new Dictionary<string, string>
        {
            ["https://broken.test/x"] = "<html>no structured data at all</html>",
            ["https://one.test/brisket"] = RecipePage("Braised Beef Brisket", Brisket),
        };

        var response = await Run(urls, pages, Request("beef brisket"));

        Assert.Equal("Braised Beef Brisket", Assert.Single(response.Recipes).Title);
    }

    [Fact]
    public async Task Refuses_rather_than_inventing_when_nothing_can_be_read()
    {
        var urls = new[] { "https://broken.test/x" };
        var pages = new Dictionary<string, string> { ["https://broken.test/x"] = "<html></html>" };

        var exception = await Assert.ThrowsAsync<RecipeSafetyException>(
            () => Run(urls, pages, Request("beef brisket")));
        Assert.Contains("will not invent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] Brisket =
        ["1 kg beef brisket", "2 onions", "500 ml beef stock", "1 tbsp tomato puree"];

    private static async Task<RecipeGenerationResponse> Run(
        IEnumerable<string> urls,
        IReadOnlyDictionary<string, string> pages,
        GenerateRecipesRequest request,
        List<string>? fetched = null,
        string listingTitle = "")
    {
        var candidates = urls
            .Select(url => new RecipeCandidate(
                url,
                url.Contains("collection", StringComparison.OrdinalIgnoreCase) ? listingTitle : "A recipe",
                null,
                "beef brisket recipe"))
            .ToList();

        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Provider = "GoogleSearch",
            GoogleSearch = new GoogleSearchOptions
            {
                ApiKey = "test-key",
                MaxQueries = 4,
                ResultsPerQuery = 10,
                MaxPagesRead = 30,
                MaxConcurrentPageReads = 6,
                PageReadTimeoutSeconds = 10
            },
            PublisherExtraction = new PublisherExtractionOptions { Enabled = true, MaxConcurrentRequests = 5 }
        });

        var publisherPages = new PublisherRecipePageClient(
            new HttpClient(new MapHandler(pages, fetched)),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 50 }),
            options,
            new TestHostEnvironment("Production"),
            new IngredientNormalizer(),
            NullLogger<PublisherRecipePageClient>.Instance);

        var client = new GoogleRecipeSearchClient(
            new StubDiscovery(candidates),
            publisherPages,
            new IngredientNormalizer(),
            options,
            NullLogger<GoogleRecipeSearchClient>.Instance);

        return await client.FindRecipesAsync(request, CancellationToken.None);
    }

    private static GenerateRecipesRequest Request(string main, int maxRecipes = 4) => new()
    {
        Ingredients = [new IngredientInput(main, "1 kg")],
        MainIngredient = main,
        DietaryPreference = "Anything",
        MaxRecipes = maxRecipes,
        Servings = 4
    };

    private static string RecipePage(
        string title,
        IReadOnlyList<string> ingredients,
        string totalTime = "PT1H",
        string servings = "4",
        string? image = null)
    {
        var lines = string.Join(",", ingredients.Select(i => $"\"{i}\""));
        var imageField = image is null ? "" : $"\"image\":{{\"@type\":\"ImageObject\",\"url\":\"{image}\"}},";
        return $$"""
            <html><head><script type="application/ld+json">
            {"@context":"https://schema.org","@type":"Recipe","name":"{{title}}",{{imageField}}
             "totalTime":"{{totalTime}}","recipeYield":"{{servings}}",
             "recipeIngredient":[{{lines}}],
             "recipeInstructions":[{"@type":"HowToStep","text":"Publisher method is not copied."}]}
            </script></head></html>
            """;
    }

    private sealed class StubDiscovery(IReadOnlyList<RecipeCandidate> candidates) : IRecipeDiscoveryProvider
    {
        public string Name => "StubSearch";

        public bool IsConfigured => true;

        public Task<IReadOnlyList<RecipeCandidate>> DiscoverAsync(
            IReadOnlyList<string> queries,
            int resultsPerQuery,
            CancellationToken cancellationToken) => Task.FromResult(candidates);
    }

    /// <summary>Serves a different page per URL, and records which ones were actually asked for.</summary>
    private sealed class MapHandler(IReadOnlyDictionary<string, string> pages, List<string>? fetched)
        : HttpMessageHandler
    {
        private readonly Lock _gate = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (fetched is not null)
            {
                lock (_gate)
                {
                    fetched.Add(url);
                }
            }

            if (!pages.TryGetValue(url, out var html))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
            });
        }
    }
}
