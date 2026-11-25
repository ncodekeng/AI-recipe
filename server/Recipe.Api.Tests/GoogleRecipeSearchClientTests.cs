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

    // ---- Image search ------------------------------------------------------------------------------

    private const string GoogleImage = "https://cdn.publisher.test/photograph-we-may-not-use.jpg";
    private const string GoogleThumbnail = "https://encrypted-tbn0.gstatic.test/thumb.jpg";

    [Fact]
    public async Task A_photograph_found_through_google_images_is_never_put_on_the_recipe()
    {
        // The point of an image result is the page it links to, not the picture. That picture belongs to
        // whoever took it and was reached through a third party's index, so PLATE does not display,
        // download or rehost it. Photographs come from CommercialRecipeImageClient, which verifies a
        // commercial licence, or from the publisher's own schema.org image under the prototype flag.
        var pages = new Dictionary<string, string>
        {
            ["https://a.test/1"] = RecipePage("Beef Brisket Curry", Brisket),
            ["https://b.test/2"] = RecipePage("Beef Brisket Stew", Brisket),
            ["https://c.test/3"] = RecipePage("Beef Brisket Pie", Brisket),
            ["https://d.test/4"] = RecipePage("Beef Brisket Ramen", Brisket),
        };

        var response = await Run(
            pages.Keys,
            pages,
            Request("beef brisket"),
            source: RecipeCandidateSource.ImageSearch);

        Assert.Equal(4, response.Recipes.Count);
        var json = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.DoesNotContain(GoogleImage, json, StringComparison.Ordinal);
        Assert.DoesNotContain(GoogleThumbnail, json, StringComparison.Ordinal);
        Assert.DoesNotContain("gstatic", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(response.Recipes, recipe => Assert.Null(recipe.ImageUrl));
    }

    [Fact]
    public async Task A_recipe_found_through_image_search_is_still_read_from_the_publishers_page()
    {
        // No shortcut because it came from the image index: the title, ingredients and timings are the
        // publisher's own structured data, read from the page, exactly as for a web result.
        var pages = new Dictionary<string, string>
        {
            ["https://a.test/1"] = RecipePage("Rack of Lamb with Herb Crust", Brisket, totalTime: "PT45M"),
        };

        var response = await Run(
            pages.Keys,
            pages,
            Request("beef brisket", maxRecipes: 3),
            source: RecipeCandidateSource.ImageSearch);

        var recipe = Assert.Single(response.Recipes);
        Assert.Equal("Rack of Lamb with Herb Crust", recipe.Title);
        Assert.Equal(45, recipe.CookingMinutes);
        Assert.NotEmpty(recipe.Ingredients);
        Assert.True(recipe.PublisherPageVerified);
        Assert.Equal("https://a.test/1", recipe.SourceUrl);
    }

    [Fact]
    public async Task A_pinboard_is_never_fetched()
    {
        // Image search finds these constantly, and each one would otherwise cost a page read to discover
        // it holds no recipe.
        var pages = new Dictionary<string, string>
        {
            ["https://www.pinterest.co.uk/pin/123"] = RecipePage("Looks like a recipe", Brisket),
            ["https://a.test/1"] = RecipePage("Beef Brisket Curry", Brisket),
        };

        var fetched = new List<string>();
        var response = await Run(
            pages.Keys,
            pages,
            Request("beef brisket", maxRecipes: 3),
            fetched,
            source: RecipeCandidateSource.ImageSearch);

        Assert.DoesNotContain("https://www.pinterest.co.uk/pin/123", fetched);
        Assert.Single(response.Recipes);
    }

    // ---- Show more results ------------------------------------------------------------------------

    [Fact]
    public async Task A_second_batch_returns_dinners_the_first_batch_did_not()
    {
        var pages = EightDishes();
        var first = await Run(pages.Keys, pages, Request("beef brisket"));
        Assert.Equal(4, first.Recipes.Count);

        var second = await Run(pages.Keys, pages, ShowMore(first));

        Assert.Equal(4, second.Recipes.Count);
        Assert.Empty(second.Recipes.Select(r => r.Id).Intersect(first.Recipes.Select(r => r.Id)));
        Assert.Empty(second.Recipes.Select(r => r.SourceUrl).Intersect(first.Recipes.Select(r => r.SourceUrl)));
    }

    [Fact]
    public async Task A_page_already_shown_is_not_fetched_a_second_time()
    {
        // A recipe's id is the hash of its source URL, so an already-seen candidate is recognised straight
        // from the search result. The saving is a whole page read, not a parse.
        var pages = EightDishes();
        var first = await Run(pages.Keys, pages, Request("beef brisket"));

        var fetched = new List<string>();
        await Run(pages.Keys, pages, ShowMore(first), fetched);

        foreach (var url in first.Recipes.Select(recipe => recipe.SourceUrl))
        {
            Assert.DoesNotContain(url, fetched);
        }
    }

    [Fact]
    public async Task A_later_batch_asks_different_questions()
    {
        // The ids alone would only empty the batch: the planner would return the same four queries,
        // discovery the same pages, and every one of them would then be filtered out as already seen.
        var pages = EightDishes();
        var firstQueries = new List<string>();
        var first = await Run(pages.Keys, pages, Request("beef brisket"), queriesSeen: firstQueries);

        var laterQueries = new List<string>();
        await Run(pages.Keys, pages, ShowMore(first), queriesSeen: laterQueries);

        Assert.NotEmpty(firstQueries);
        Assert.NotEmpty(laterQueries);
        Assert.Empty(firstQueries.Intersect(laterQueries, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Running_out_of_results_is_an_empty_batch_rather_than_an_error()
    {
        // Exhausting the list is the ordinary end of it. The app reads an empty batch as "that is everything
        // we could verify for this search"; an exception here would show the customer an error instead.
        var pages = FourDishes();
        var first = await Run(pages.Keys, pages, Request("beef brisket"));
        Assert.Equal(4, first.Recipes.Count);

        var second = await Run(pages.Keys, pages, ShowMore(first));

        Assert.Empty(second.Recipes);
    }

    [Fact]
    public async Task A_first_search_that_finds_nothing_still_refuses()
    {
        // The empty batch above is only right for a continuation. A first search with nothing to show must
        // still say so rather than return an empty list that reads as "no recipes exist for this".
        var pages = new Dictionary<string, string>
        {
            ["https://a.test/1"] = "<html><body>no recipe here</body></html>",
        };

        await Assert.ThrowsAsync<RecipeSafetyException>(
            () => Run(pages.Keys, pages, Request("beef brisket")));
    }

    private static GenerateRecipesRequest ShowMore(RecipeGenerationResponse previous) =>
        Request("beef brisket") with
        {
            RecentlyShownRecipeIds = previous.Recipes.Select(recipe => recipe.Id).ToList(),
        };

    /// <summary>Eight genuinely different dinners, so a second batch has somewhere to come from.</summary>
    private static Dictionary<string, string> EightDishes() => new()
    {
        ["https://a.test/1"] = RecipePage("Beef Brisket Curry", Brisket),
        ["https://b.test/2"] = RecipePage("Beef Brisket Stew", Brisket),
        ["https://c.test/3"] = RecipePage("Beef Brisket Pie", Brisket),
        ["https://d.test/4"] = RecipePage("Beef Brisket Ramen", Brisket),
        ["https://e.test/5"] = RecipePage("Beef Brisket Tacos", Brisket),
        ["https://f.test/6"] = RecipePage("Beef Brisket Lasagne", Brisket),
        ["https://g.test/7"] = RecipePage("Beef Brisket Risotto", Brisket),
        ["https://h.test/8"] = RecipePage("Beef Brisket Biryani", Brisket),
    };

    private static Dictionary<string, string> FourDishes() => new()
    {
        ["https://a.test/1"] = RecipePage("Beef Brisket Curry", Brisket),
        ["https://b.test/2"] = RecipePage("Beef Brisket Stew", Brisket),
        ["https://c.test/3"] = RecipePage("Beef Brisket Pie", Brisket),
        ["https://d.test/4"] = RecipePage("Beef Brisket Ramen", Brisket),
    };

    private static readonly string[] Brisket =
        ["1 kg beef brisket", "2 onions", "500 ml beef stock", "1 tbsp tomato puree"];

    private static async Task<RecipeGenerationResponse> Run(
        IEnumerable<string> urls,
        IReadOnlyDictionary<string, string> pages,
        GenerateRecipesRequest request,
        List<string>? fetched = null,
        string listingTitle = "",
        List<string>? queriesSeen = null,
        RecipeCandidateSource source = RecipeCandidateSource.WebSearch)
    {
        var candidates = urls
            .Select(url => new RecipeCandidate(
                url,
                url.Contains("collection", StringComparison.OrdinalIgnoreCase) ? listingTitle : "A recipe",
                null,
                "beef brisket recipe",
                source,
                source == RecipeCandidateSource.ImageSearch ? "A Publisher" : null,
                source == RecipeCandidateSource.ImageSearch ? GoogleImage : null,
                source == RecipeCandidateSource.ImageSearch ? GoogleThumbnail : null))
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
            new StubDiscovery(candidates, queriesSeen),
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

    private sealed class StubDiscovery(
        IReadOnlyList<RecipeCandidate> candidates,
        List<string>? queriesSeen = null) : IRecipeDiscoveryProvider
    {
        public string Name => "StubSearch";

        public bool IsConfigured => true;

        public Task<IReadOnlyList<RecipeCandidate>> DiscoverAsync(
            IReadOnlyList<string> queries,
            int resultsPerQuery,
            CancellationToken cancellationToken)
        {
            queriesSeen?.AddRange(queries);
            return Task.FromResult(candidates);
        }
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
