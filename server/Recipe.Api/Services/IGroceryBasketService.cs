using Recipe.Api.Models;

namespace Recipe.Api.Services;

public interface IGroceryBasketService
{
    Task<GroceryBasketResponse> CreateBasketAsync(
        CreateGroceryBasketRequest request,
        CancellationToken cancellationToken);
}
