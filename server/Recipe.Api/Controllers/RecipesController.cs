using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/recipes")]
public sealed class RecipesController(IRecipeAiService recipeAi) : ControllerBase
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

        return Ok(await recipeAi.GenerateRecipesAsync(request, cancellationToken));
    }
}
