using System.Net;
using System.Text;
using System.Text.Json;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class AzureGroundedRecipeClientTests
{
    [Fact]
    public async Task Uses_web_search_and_maps_only_a_cited_recipe()
    {
        const string sourceUrl = "https://publisher.example.test/lamb-stew";
        var handler = new CapturingHandler(Response(
            sourceUrl,
            sourceUrl,
            "Cotes du Rhone"));
        var client = CreateClient(handler);

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.Equal("Azure Web Search", response.Provider);
        Assert.Equal(sourceUrl, recipe.SourceUrl);
        Assert.Equal("publisher.example.test", recipe.SourceName);
        Assert.Equal("Cotes du Rhone", recipe.WinePairing);
        Assert.Null(recipe.ImageUrl);
        Assert.Empty(recipe.Steps);

        Assert.Equal("https://test.openai.azure.com/openai/v1/responses", handler.RequestUri?.ToString());
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Contains("\"type\":\"web_search\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"country\":\"GB\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"tool_choice\":\"required\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"parallel_tool_calls\":false", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("web_search_call.action.sources", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"store\":false", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("lamb", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_a_recipe_url_not_returned_by_web_search()
    {
        var handler = new CapturingHandler(Response(
            "https://publisher.example.test/cited",
            "https://invented.example.test/not-cited",
            string.Empty));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<RecipeSafetyException>(() =>
            client.FindRecipesAsync(Request(), CancellationToken.None));

        Assert.Contains("verifiable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suppresses_wine_pairing_for_halal_style_requests()
    {
        const string sourceUrl = "https://publisher.example.test/halal-lamb";
        var client = CreateClient(new CapturingHandler(Response(
            sourceUrl,
            sourceUrl,
            "Syrah")));
        var request = Request("Halal-style");

        var response = await client.FindRecipesAsync(request, CancellationToken.None);

        Assert.Null(Assert.Single(response.Recipes).WinePairing);
    }

    private static AzureGroundedRecipeClient CreateClient(HttpMessageHandler handler)
    {
        var foodOptions = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions
        {
            AzureOpenAI = new AzureOpenAiOptions
            {
                Endpoint = "https://test.openai.azure.com",
                ApiKey = "test-key",
                Deployment = "gpt-test"
            }
        });
        var catalogOptions = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions());
        return new AzureGroundedRecipeClient(new HttpClient(handler), foodOptions, catalogOptions);
    }

    private static GenerateRecipesRequest Request(string diet = "Anything") => new()
    {
        Ingredients = [new IngredientInput("lamb", "500 g")],
        DietaryPreference = diet,
        MaxCookingMinutes = 90,
        Servings = 2
    };

    private static string Response(string citedUrl, string recipeUrl, string winePairing)
    {
        var recipePayload = JsonSerializer.Serialize(new
        {
            recipes = new[]
            {
                new
                {
                    title = "Publisher lamb stew",
                    description = "A warming sourced lamb stew.",
                    cookingMinutes = 75,
                    difficulty = "Medium",
                    cuisine = "British",
                    servings = 2,
                    tags = new[] { "Dinner" },
                    ingredients = new[]
                    {
                        new
                        {
                            amount = "500 g",
                            name = "lamb",
                            quantity = (double?)500,
                            unit = "g",
                            originalText = "500 g lamb"
                        }
                    },
                    sourceUrl = recipeUrl,
                    winePairing
                }
            }
        });

        return JsonSerializer.Serialize(new
        {
            output = new object[]
            {
                new
                {
                    type = "web_search_call",
                    action = new { sources = new[] { new { type = "url", url = citedUrl } } }
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
                            annotations = new[] { new { type = "url_citation", url = citedUrl } }
                        }
                    }
                }
            }
        });
    }

    private sealed class CapturingHandler(string payload) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("api-key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
