using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// Discovery through Google's image index as well as its web index.
///
/// It is here because a client showed that searching Google Images for an ingredient -- rack of lamb -- turns
/// up individual recipes from many different publishers at once. The two indexes genuinely differ, so each
/// finds pages the other misses.
///
/// Two things have to hold. An image result is a route to a page, never a picture to show: the recipe is on
/// the page the photograph was published on, and the photograph belongs to whoever took it. And the image
/// index returns a class of result the web index mostly does not -- pinboards, stock libraries, supermarket
/// listings -- each of which would otherwise cost a publisher fetch to discover it holds no recipe.
/// </summary>
public sealed class SerperImageDiscoveryTests
{
    [Fact]
    public async Task Reads_the_page_an_image_was_published_on_not_the_image()
    {
        var handler = new SerperHandler();

        var candidates = await Discover(handler, "ImageSearch");

        var lamb = Assert.Single(candidates, candidate => candidate.Url.Contains("rack-of-lamb"));
        // `link` is the candidate: that is the page with the recipe on it.
        Assert.Equal("https://www.greatbritishchefs.com/recipes/rack-of-lamb", lamb.Url);
        Assert.Equal(RecipeCandidateSource.ImageSearch, lamb.Source);
        Assert.Equal("Great British Chefs", lamb.PublisherName);
        // Carried for diagnostics, and going no further than that -- see the display test below.
        Assert.Equal("https://cdn.example.test/lamb-full.jpg", lamb.ImageUrl);
        Assert.Equal("https://encrypted-tbn0.gstatic.test/lamb-thumb.jpg", lamb.ThumbnailUrl);
    }

    [Fact]
    public async Task Asks_both_indexes_at_once_in_combined_mode()
    {
        var handler = new SerperHandler();

        var candidates = await Discover(handler, "Combined");

        // One call per endpoint per query, and the queries run together rather than one after another.
        Assert.Equal(2, handler.Endpoints.Count(endpoint => endpoint == "search"));
        Assert.Equal(2, handler.Endpoints.Count(endpoint => endpoint == "images"));
        Assert.Contains(candidates, candidate => candidate.Source == RecipeCandidateSource.WebSearch);
        Assert.Contains(candidates, candidate => candidate.Source == RecipeCandidateSource.ImageSearch);
    }

    [Theory]
    [InlineData("WebSearch", "search", "images")]
    [InlineData("ImageSearch", "images", "search")]
    public async Task One_mode_asks_one_index(string mode, string used, string unused)
    {
        var handler = new SerperHandler();

        await Discover(handler, mode);

        Assert.Contains(used, handler.Endpoints);
        Assert.DoesNotContain(unused, handler.Endpoints);
    }

    [Fact]
    public async Task The_same_page_found_by_both_indexes_is_one_candidate()
    {
        // Otherwise it is fetched twice and can fill two of the four slots with one dinner.
        var handler = new SerperHandler();

        var candidates = await Discover(handler, "Combined");

        var shared = candidates.Count(candidate => candidate.Url.Contains("shared-brisket"));
        Assert.Equal(1, shared);
        Assert.Equal(
            candidates.Count,
            candidates.Select(candidate => SerperDiscoveryProvider.CanonicalUrl(candidate.Url)).Distinct().Count());
    }

    [Fact]
    public async Task Neither_index_monopolises_the_front_of_the_pool()
    {
        // Only the first MaxPagesRead candidates are ever read, so putting one index entirely ahead of the
        // other would make a combined search combined in name only.
        var handler = new SerperHandler();

        var candidates = await Discover(handler, "Combined");
        var firstFour = candidates.Take(4).ToList();

        Assert.Contains(firstFour, candidate => candidate.Source == RecipeCandidateSource.WebSearch);
        Assert.Contains(firstFour, candidate => candidate.Source == RecipeCandidateSource.ImageSearch);
    }

    [Fact]
    public async Task A_recipe_page_is_offered_before_a_pinboard_or_a_roundup()
    {
        var handler = new SerperHandler();

        var candidates = await Discover(handler, "Combined");
        var pinterest = candidates.FindIndex(candidate => candidate.Url.Contains("pinterest"));
        var recipe = candidates.FindIndex(candidate => candidate.Url.Contains("rack-of-lamb"));

        Assert.True(recipe >= 0 && pinterest >= 0, "both should still be in the pool");
        Assert.True(recipe < pinterest, "a recipe page must not queue behind a pinboard");
    }

    [Theory]
    // Pinboards and social feeds: a photograph with a caption, and the recipe somewhere else.
    [InlineData("https://www.pinterest.co.uk/pin/123456789/")]
    [InlineData("https://www.instagram.com/p/abcdef/")]
    [InlineData("https://www.youtube.com/watch?v=abcdef")]
    // Stock libraries: a picture of a dinner for sale.
    [InlineData("https://www.shutterstock.com/image-photo/rack-of-lamb-123")]
    [InlineData("https://www.gettyimages.co.uk/detail/photo/rack-of-lamb")]
    // Shopping: an ingredient to buy, not a dinner to cook.
    [InlineData("https://www.tesco.com/groceries/en-GB/products/254656789")]
    [InlineData("https://www.amazon.co.uk/dp/B07XYZ")]
    public void A_site_that_does_not_publish_recipes_is_recognised(string url)
    {
        Assert.True(RecipeDishConcept.IsUnsuitableSource(url), url);
    }

    [Theory]
    [InlineData("https://www.greatbritishchefs.com/recipes/rack-of-lamb")]
    [InlineData("https://www.bbcgoodfood.com/recipes/slow-cooker-brisket")]
    [InlineData("https://www.recipetineats.com/slow-cooker-beef-brisket/")]
    // A publisher whose name merely contains one of the words is not one of them.
    [InlineData("https://www.pinterestrecipes.example.test/recipes/lamb")]
    public void A_real_publisher_is_left_alone(string url)
    {
        Assert.False(RecipeDishConcept.IsUnsuitableSource(url), url);
    }

    private static async Task<List<RecipeCandidate>> Discover(SerperHandler handler, string mode)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Provider = "GoogleSearch",
            GoogleSearch = new GoogleSearchOptions
            {
                ApiKey = "test-key",
                DiscoveryMode = mode,
                MaxQueries = 2,
                ResultsPerQuery = 10,
                ImageResultsPerQuery = 10,
                MaxConcurrentQueries = 4,
                TimeoutSeconds = 10,
            },
        });

        var provider = new SerperDiscoveryProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://google.serper.dev/") },
            options,
            NullLogger<SerperDiscoveryProvider>.Instance);

        var candidates = await provider.DiscoverAsync(
            ["rack of lamb recipe", "rack of lamb dinner recipe"],
            10,
            CancellationToken.None);

        return candidates.ToList();
    }

    /// <summary>Answers both Serper endpoints, and records which were asked.</summary>
    private sealed class SerperHandler : HttpMessageHandler
    {
        private readonly Lock _gate = new();

        public List<string> Endpoints { get; } = [];

        private const string WebPayload = """
            {"organic":[
              {"link":"https://www.greatbritishchefs.com/recipes/rack-of-lamb-web","title":"Rack of lamb","snippet":"A recipe."},
              {"link":"https://www.bbcgoodfood.com/recipes/shared-brisket","title":"Brisket","snippet":"A recipe."},
              {"link":"https://www.olivemagazine.com/recipes/best-lamb-recipes","title":"30 best lamb recipes","snippet":"A roundup."}
            ]}
            """;

        private const string ImagePayload = """
            {"images":[
              {"link":"https://www.greatbritishchefs.com/recipes/rack-of-lamb","title":"Rack of lamb","source":"Great British Chefs","domain":"greatbritishchefs.com","imageUrl":"https://cdn.example.test/lamb-full.jpg","thumbnailUrl":"https://encrypted-tbn0.gstatic.test/lamb-thumb.jpg"},
              {"link":"https://www.pinterest.co.uk/pin/123456789/","title":"Rack of lamb idea","source":"Pinterest","imageUrl":"https://cdn.example.test/pin.jpg","thumbnailUrl":"https://encrypted-tbn0.gstatic.test/pin.jpg"},
              {"link":"https://www.bbcgoodfood.com/recipes/shared-brisket","title":"Brisket","source":"BBC Good Food","imageUrl":"https://cdn.example.test/brisket.jpg","thumbnailUrl":"https://encrypted-tbn0.gstatic.test/brisket.jpg"}
            ]}
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var endpoint = request.RequestUri!.AbsolutePath.Trim('/');
            lock (_gate)
            {
                Endpoints.Add(endpoint);
            }

            var payload = endpoint == "images" ? ImagePayload : WebPayload;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
