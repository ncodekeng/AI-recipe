using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class EdamamRecipeClientTests
{
    [Fact]
    public async Task Maps_source_data_but_not_an_image_without_license_metadata()
    {
        const string payload = """
            {
              "hits": [{
                "recipe": {
                  "uri": "recipe_1",
                  "label": "Roast Salmon with Lemon",
                  "url": "https://publisher.example.test/roast-salmon",
                  "source": "Example Kitchen",
                  "yield": 2,
                  "calories": 708,
                  "totalTime": 30,
                  "ingredients": [
                    { "text": "2 salmon fillets", "food": "salmon fillet", "quantity": 2, "measure": "fillet" },
                    { "text": "1 lemon", "food": "lemon", "quantity": 1, "measure": "whole" }
                  ],
                  "instructionLines": [
                    "Roast the salmon until cooked through.",
                    "Serve with the lemon."
                  ],
                  "images": {
                    "SMALL": { "url": "https://images.example.test/small.jpg" },
                    "REGULAR": { "url": "https://images.example.test/regular.jpg" }
                  },
                  "cuisineType": ["British"],
                  "dietLabels": ["Balanced"],
                  "healthLabels": []
                }
              }]
            }
            """;
        var httpClient = new HttpClient(new JsonHandler(payload))
        {
            BaseAddress = new Uri("https://api.edamam.com/")
        };
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Edamam = new EdamamOptions { AppId = "test-id", AppKey = "test-key" }
        });
        var client = new EdamamRecipeClient(httpClient, options, new IngredientNormalizer());

        var response = await client.FindRecipesAsync(
            new GenerateRecipesRequest
            {
                Ingredients = [new IngredientInput("salmon", "2")],
                MainIngredient = "salmon",
                Servings = 2
            },
            CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.Null(recipe.ImageUrl);
        Assert.Equal("Example Kitchen", recipe.SourceName);
        Assert.Equal("Roast Salmon with Lemon", recipe.SourceTitle);
        Assert.Equal("https://publisher.example.test/roast-salmon", recipe.SourceUrl);
        Assert.True(recipe.SourceVerified);
        Assert.Equal("salmon fillet", recipe.Ingredients[0].Name);
        Assert.Equal(2, recipe.Ingredients[0].Quantity);
        Assert.Equal(RecipeDirectionsKinds.Provider, recipe.DirectionsKind);
        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal(354, recipe.CaloriesPerServing);
    }

    [Fact]
    public async Task Query_prioritizes_substantial_foods_over_scanned_condiments()
    {
        var handler = new JsonHandler("""{ "hits": [] }""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.edamam.com/")
        };
        var options = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            Edamam = new EdamamOptions { AppId = "test-id", AppKey = "test-key" }
        });
        var client = new EdamamRecipeClient(httpClient, options, new IngredientNormalizer());

        await Assert.ThrowsAsync<RecipeSafetyException>(() => client.FindRecipesAsync(
            new GenerateRecipesRequest
            {
                Ingredients =
                [
                    new IngredientInput("mustard jar", "1 jar"),
                    new IngredientInput("bottle of juice", "2 bottles"),
                    new IngredientInput("raw chicken breast", "1 piece"),
                    new IngredientInput("potatoes", "3"),
                    new IngredientInput("red bell pepper", "1"),
                    new IngredientInput("onion", "1"),
                    new IngredientInput("tomatoes", "2"),
                    new IngredientInput("spinach", "1 bunch")
                ]
            },
            CancellationToken.None));

        var query = Uri.UnescapeDataString(handler.RequestUri!.Query);
        Assert.Contains("chicken, potato, bell pepper, onion, tomato, spinach", query);
        Assert.DoesNotContain("mustard", query);
        Assert.DoesNotContain("juice", query);
    }

    private sealed class JsonHandler(string payload) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
