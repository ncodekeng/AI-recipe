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
    public async Task Maps_the_best_real_provider_image_and_source_data()
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
                  "totalTime": 30,
                  "ingredients": [
                    { "text": "2 salmon fillets", "food": "salmon fillet", "quantity": 2, "measure": "fillet" },
                    { "text": "1 lemon", "food": "lemon", "quantity": 1, "measure": "whole" }
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
            Provider = "Edamam",
            Edamam = new EdamamOptions { AppId = "test-id", AppKey = "test-key" }
        });
        var client = new EdamamRecipeClient(httpClient, options);

        var response = await client.FindRecipesAsync(
            new GenerateRecipesRequest
            {
                Ingredients = [new IngredientInput("salmon", "2")],
                Servings = 2
            },
            CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.Equal("https://images.example.test/regular.jpg", recipe.ImageUrl);
        Assert.Equal("Example Kitchen", recipe.SourceName);
        Assert.Equal("https://publisher.example.test/roast-salmon", recipe.SourceUrl);
        Assert.Equal("salmon fillet", recipe.Ingredients[0].Name);
        Assert.Equal(2, recipe.Ingredients[0].Quantity);
    }

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
