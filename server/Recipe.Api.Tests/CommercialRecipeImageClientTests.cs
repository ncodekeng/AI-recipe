using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class CommercialRecipeImageClientTests
{
    [Fact]
    public async Task Accepts_a_relevant_image_with_verified_commercial_license_metadata()
    {
        var client = CreateClient(Response("CC BY-SA 4.0", includeCreator: true));

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal("https://upload.wikimedia.org/example/chicken-potato.jpg", image.ImageUrl);
        Assert.Equal("https://commons.wikimedia.org/wiki/File:Chicken_potato_skillet.jpg", image.SourceUrl);
        Assert.Equal("CC BY-SA 4.0", image.LicenseType);
        Assert.Equal("https://creativecommons.org/licenses/by-sa/4.0/", image.LicenseUrl);
        Assert.Equal("Wikimedia Commons", image.Provider);
        Assert.Equal("Example Photographer", image.Creator);
        Assert.True(image.CommercialUseAllowed);
        Assert.True(image.AttributionRequired);
        Assert.Contains("Example Photographer", image.AttributionRequirements, StringComparison.Ordinal);
        Assert.Contains("same terms", image.AttributionRequirements, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CC BY-NC 4.0")]
    [InlineData("All rights reserved")]
    public async Task Rejects_a_license_that_is_not_in_the_commercial_allowlist(string license)
    {
        var client = CreateClient(Response(license, includeCreator: true));

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.Null(image);
    }

    [Theory]
    [InlineData("CC0")]
    [InlineData("Public domain")]
    public async Task Accepts_no_attribution_commercial_licenses_without_a_creator(string license)
    {
        var client = CreateClient(Response(license, includeCreator: false));

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(license, image.LicenseType);
        Assert.Contains("No", image.AttributionRequirements, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_attribution_license_without_a_creator()
    {
        var client = CreateClient(Response("CC BY 4.0", includeCreator: false));

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.Null(image);
    }

    [Fact]
    public async Task Rejects_an_image_that_does_not_match_the_dish_name()
    {
        var client = CreateClient(Response("CC0", includeCreator: false, title: "File:Mountain_landscape.jpg"));

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.Null(image);
    }

    [Fact]
    public async Task Ignores_generic_style_words_when_matching_a_dish_photo()
    {
        var client = CreateClient(Response(
            "CC0",
            includeCreator: false,
            title: "File:Chicken_and_peppers.jpg"));

        var image = await client.FindAsync(
            "One Pan Baked Chicken and Peppers",
            CancellationToken.None);

        Assert.NotNull(image);
    }

    [Fact]
    public async Task Development_flag_returns_a_relevant_unverified_test_image()
    {
        var client = CreateClient(
            Response("All rights reserved", includeCreator: false, title: "File:Chicken potato dinner.jpg"),
            allowUnverifiedForTesting: true);

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.NotNull(image);
        Assert.False(image.IsVerified);
        Assert.Equal("Unverified test image", image.LicenseType);
        Assert.Contains("Testing only", image.AttributionRequirements, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_ignores_the_unverified_test_flag()
    {
        var client = CreateClient(
            Response("All rights reserved", includeCreator: false, title: "File:Chicken potato dinner.jpg"),
            allowUnverifiedForTesting: true,
            environmentName: Environments.Production);

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.Null(image);
    }

    [Fact]
    public async Task Cached_image_retains_license_metadata_without_a_second_lookup()
    {
        var handler = new JsonHandler(Response("CC BY 4.0", includeCreator: true));
        var client = CreateClient(string.Empty, handler: handler);

        var first = await client.FindAsync("Chicken potato skillet", CancellationToken.None);
        var cached = await client.FindAsync(" chicken-potato skillet ", CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(first);
        Assert.Equal(first, cached);
        Assert.Equal("CC BY 4.0", cached!.LicenseType);
        Assert.Equal("Example Photographer", cached.Creator);
    }

    [Fact]
    public async Task Rejects_an_image_url_outside_the_approved_host()
    {
        var client = CreateClient(Response(
            "CC0",
            includeCreator: false,
            imageUrl: "https://attacker.example.test/chicken.jpg"));

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.Null(image);
    }

    [Fact]
    public async Task Provider_timeout_degrades_to_a_missing_photo()
    {
        var client = CreateClient(string.Empty, handler: new TimeoutHandler());

        var image = await client.FindAsync("Chicken potato skillet", CancellationToken.None);

        Assert.Null(image);
    }

    private static CommercialRecipeImageClient CreateClient(
        string response,
        bool allowUnverifiedForTesting = false,
        string? environmentName = null,
        HttpMessageHandler? handler = null)
    {
        var httpClient = new HttpClient(handler ?? new JsonHandler(response))
        {
            BaseAddress = new Uri("https://commons.wikimedia.org/")
        };
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            CommercialImages = new CommercialImageOptions
            {
                Enabled = true,
                AllowUnverifiedForTesting = allowUnverifiedForTesting,
                MaxCandidates = 8
            }
        });
        var photoCache = new RecipePhotoCache(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 }),
            options,
            NullLogger<RecipePhotoCache>.Instance);
        return new CommercialRecipeImageClient(
            httpClient,
            options,
            new TestHostEnvironment(environmentName ??
                (allowUnverifiedForTesting ? Environments.Development : Environments.Production)),
            photoCache,
            NullLogger<CommercialRecipeImageClient>.Instance);
    }

    private static string Response(
        string license,
        bool includeCreator,
        string title = "File:Chicken_potato_skillet.jpg",
        string imageUrl = "https://upload.wikimedia.org/example/chicken-potato.jpg") => $$"""
        {
          "query": {
            "pages": [{
              "title": "{{title}}",
              "imageinfo": [{
                "url": "https://upload.wikimedia.org/example/chicken-potato-original.jpg",
                "thumburl": "{{imageUrl}}",
                "descriptionurl": "https://commons.wikimedia.org/wiki/File:Chicken_potato_skillet.jpg",
                "mime": "image/jpeg",
                "mediatype": "BITMAP",
                "extmetadata": {
                  "LicenseShortName": { "value": "{{license}}" },
                  "LicenseUrl": { "value": "https://creativecommons.org/licenses/by-sa/4.0/" }{{(includeCreator ? ",\n                  \"Artist\": { \"value\": \"<a href='https://example.test'>Example Photographer</a>\" }" : string.Empty)}}
                }
              }]
            }]
          }
        }
        """;

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

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new TaskCanceledException("Simulated provider timeout.");
    }
}
