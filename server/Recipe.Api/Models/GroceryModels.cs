namespace Recipe.Api.Models;

public sealed class CreateGroceryBasketRequest
{
    public Guid RecipeId { get; init; }

    public List<GroceryIngredient> Ingredients { get; init; } = [];
}

public sealed record GroceryIngredient(
    string Name,
    string Amount,
    double? Quantity = null,
    string? Unit = null);

public sealed record GroceryBasketResponse(
    string Provider,
    string Status,
    bool BasketCreated,
    string? CheckoutUrl,
    string? HandoffUrl,
    string Message,
    IReadOnlyList<GroceryIngredient> Ingredients);
