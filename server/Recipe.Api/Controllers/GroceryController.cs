using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/grocery/deliveroo")]
public sealed class GroceryController(IGroceryBasketService groceryBasket) : ControllerBase
{
    [HttpPost("basket")]
    public async Task<ActionResult<GroceryBasketResponse>> CreateBasket(
        [FromBody] CreateGroceryBasketRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RecipeId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "A recipe is required.",
                Detail = "Select a recipe before preparing its missing ingredients."
            });
        }

        if (request.Ingredients.Count == 0 || request.Ingredients.Count > 30 ||
            request.Ingredients.Any(item =>
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100 ||
                item.Amount?.Length > 80 || item.Unit?.Length > 40 ||
                item.Quantity is <= 0 or > 100000))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The missing-ingredient list is invalid.",
                Detail = "Send 1 to 30 named ingredients with sensible quantities."
            });
        }

        try
        {
            return Ok(await groceryBasket.CreateBasketAsync(request, cancellationToken));
        }
        catch (GroceryBasketException exception)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "The shopping list could not be prepared.",
                Detail = exception.Message
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Deliveroo checkout is unavailable.",
                Detail = "The recipe is still available. Keep the missing-ingredient list and try the grocery handoff again later."
            });
        }
    }
}
