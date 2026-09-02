using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class DeliverooBasketServiceTests
{
    [Fact]
    public async Task Builds_an_honest_deduplicated_manual_handoff()
    {
        var service = new DeliverooBasketService(new IngredientNormalizer());
        var request = new CreateGroceryBasketRequest
        {
            RecipeId = Guid.NewGuid(),
            Ingredients =
            [
                new GroceryIngredient("Garlic", "2 cloves", 2, "clove"),
                new GroceryIngredient("garlic cloves", "2 cloves", 2, "clove"),
                new GroceryIngredient("Feta cheese", "100 g", 100, "g"),
                new GroceryIngredient("Sea salt", "to taste")
            ]
        };

        var result = await service.CreateBasketAsync(request, CancellationToken.None);

        Assert.False(result.BasketCreated);
        Assert.Null(result.CheckoutUrl);
        Assert.Equal("manual_handoff", result.Status);
        Assert.Equal(["Garlic", "Feta cheese"], result.Ingredients.Select(item => item.Name));
        Assert.Equal("https://deliveroo.co.uk/", result.HandoffUrl);
    }
}
