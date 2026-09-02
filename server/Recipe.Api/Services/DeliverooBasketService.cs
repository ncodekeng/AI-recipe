using Recipe.Api.Models;

namespace Recipe.Api.Services;

public sealed class DeliverooBasketService(IngredientNormalizer normalizer) : IGroceryBasketService
{
    private static readonly Uri DeliverooHome = new("https://deliveroo.co.uk/");

    public Task<GroceryBasketResponse> CreateBasketAsync(
        CreateGroceryBasketRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ingredients = request.Ingredients
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => !normalizer.IsPantryStaple(item.Name))
            .GroupBy(item => normalizer.Normalize(item.Name), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => group.First() with
            {
                Name = group.First().Name.Trim(),
                Amount = group.First().Amount?.Trim() ?? string.Empty,
                Unit = string.IsNullOrWhiteSpace(group.First().Unit) ? null : group.First().Unit!.Trim()
            })
            .Take(30)
            .ToList();

        if (ingredients.Count == 0)
        {
            throw new GroceryBasketException("No meaningful missing ingredients were supplied.");
        }

        return Task.FromResult(new GroceryBasketResponse(
            "Deliveroo",
            "manual_handoff",
            BasketCreated: false,
            CheckoutUrl: null,
            HandoffUrl: DeliverooHome.ToString(),
            "Automatic Deliveroo basket creation requires approved partner access. Your missing-ingredient shopping list is ready to copy into Deliveroo.",
            ingredients));
    }
}

public sealed class GroceryBasketException(string message, Exception? innerException = null)
    : Exception(message, innerException);
