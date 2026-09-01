using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/recipes")]
public sealed class RecipesController(IRecipeAiService recipeAi, AiUsageGuard usageGuard) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult<RecipeGenerationResponse>> Generate(
        [FromBody] GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Ingredients.Count == 0 || request.Ingredients.All(item => string.IsNullOrWhiteSpace(item.Name)))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Add at least one ingredient.",
                Detail = "Review the detected food or enter an ingredient manually."
            });
        }

        if (request.Ingredients.Count > 50 ||
            request.Ingredients.Any(item => item.Name.Length > 100 || item.Quantity.Length > 80) ||
            request.Allergens.Count > 20 ||
            request.AvoidIngredients.Count > 20 ||
            request.AvoidIngredients.Any(item => item.Length > 100))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The recipe request is too large.",
                Detail = "Use up to 50 ingredients and 20 dietary restrictions."
            });
        }

        var admission = usageGuard.TryAcquire(
            ClientIdentity.Resolve(HttpContext),
            AiOperation.RecipeGeneration);
        if (!admission.Allowed)
        {
            return StatusCode(admission.Rejection!.StatusCode, new ProblemDetails
            {
                Status = admission.Rejection.StatusCode,
                Title = admission.Rejection.Title,
                Detail = admission.Rejection.Detail
            });
        }

        using var usageLease = admission.Lease!;
        Response.Headers["X-Plate-Recipes-Remaining"] = admission.Status.RecipesRemaining.ToString();
        try
        {
            return Ok(await recipeAi.GenerateRecipesAsync(request, cancellationToken));
        }
        catch (RecipeSafetyException exception)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "No suitable recipes were found.",
                Detail = exception.Message
            });
        }
    }
}
