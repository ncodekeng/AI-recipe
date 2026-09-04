using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
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

    private static CommercialRecipeImageClient CreateClient(
        string response,
        bool allowUnverifiedForTesting = false,
        string? environmentName = null)
    {
        var httpClient = new HttpClient(new JsonHandler(response))
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
        return new CommercialRecipeImageClient(
            httpClient,
            options,
            new TestHostEnvironment(environmentName ??
                (allowUnverifiedForTesting ? Environments.Development : Environments.Production)),
            NullLogger<CommercialRecipeImageClient>.Instance);
    }

    private static string Response(
        string license,
        bool includeCreator,
        string title = "File:Chicken_potato_skillet.jpg") => $$"""
        {
          "query": {
            "pages": [{
              "title": "{{title}}",
              "imageinfo": [{
                "url": "https://upload.wikimedia.org/example/chicken-potato-original.jpg",
                "thumburl": "https://upload.wikimedia.org/example/chicken-potato.jpg",
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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
    }
}
