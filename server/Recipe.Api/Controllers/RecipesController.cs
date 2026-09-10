using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/recipes")]
public sealed class RecipesController(
    IRecipeCatalogService recipeCatalog,
    CommercialRecipeImageClient commercialImages,
    PublisherRecipePageClient publisherPages,
    IngredientNormalizer ingredientNormalizer,
    AdminSessionService adminSessions,
    AiUsageGuard usageGuard) : ControllerBase
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

        if (request.Ingredients.Count > GenerateRecipesRequest.MaxIngredientCount ||
            request.Ingredients.Any(item => item.Name.Length > 100 || item.Quantity.Length > 80) ||
            request.Allergens.Count > 20 ||
            request.AvoidIngredients.Count > 20 ||
            request.RecentlyShownRecipeIds.Count > 30 ||
            request.AvoidIngredients.Any(item => item.Length > 100))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The recipe request is too large.",
                Detail = $"Use up to {GenerateRecipesRequest.MaxIngredientCount} ingredients and 20 dietary restrictions."
            });
        }
        if (!string.IsNullOrWhiteSpace(request.MainIngredient) &&
            !request.Ingredients.Any(item =>
                ingredientNormalizer.Matches(item.Name, request.MainIngredient)))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Choose a main ingredient from Kitchen Memory.",
                Detail = "The selected main ingredient must match one of the submitted ingredients."
            });
        }

        var admission = usageGuard.TryAcquire(
            ClientIdentity.Resolve(HttpContext),
            AiOperation.RecipeGeneration,
            adminSessions.IsAuthenticated(HttpContext));
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
            return Ok(await recipeCatalog.FindRecipesAsync(request, cancellationToken));
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
        catch (RecipeCatalogException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Recipe search is unavailable.",
                Detail = exception.Message
            });
        }
    }

    [HttpPost("photos")]
    public async Task<ActionResult<IReadOnlyList<RecipePhotoLookupResult>>> FindPhotos(
        [FromBody] RecipePhotoLookupRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Recipes.Count == 0 ||
            request.Recipes.Count > 6 ||
            request.Recipes.Any(item => item.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 160 ||
                (!string.IsNullOrWhiteSpace(item.SourceUrl) &&
                 (item.SourceUrl.Length > 2_048 ||
                  !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var sourceUri) ||
                  !SafePublisherHttpMessageHandler.IsSafeHttpsUri(sourceUri)))))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The recipe photo request is invalid.",
                Detail = "Send one to six recipe IDs with titles no longer than 160 characters."
            });
        }

        var results = await Task.WhenAll(request.Recipes.Select(async recipe =>
        {
            var searchedImage = commercialImages.IsEnabled
                ? await commercialImages.FindAsync(recipe.Title, cancellationToken)
                : null;
            var publisherImage = recipe.PublisherPageVerified
                ? await publisherPages.FindUnverifiedPublisherImageForTestingAsync(
                    recipe.SourceUrl,
                    recipe.Title,
                    cancellationToken)
                : null;
            var image = searchedImage?.IsVerified == true
                ? searchedImage
                : publisherImage ?? searchedImage;
            return image is null
                ? new RecipePhotoLookupResult(
                    recipe.Id,
                    null,
                    null,
                    null,
                    null,
                    null,
                    RecipeImageRightsStatuses.Unavailable,
                    ImageVerified: false)
                : new RecipePhotoLookupResult(
                    recipe.Id,
                    image.ImageUrl,
                    image.SourceUrl,
                    image.LicenseType,
                    image.LicenseUrl,
                    image.AttributionRequirements,
                    image.IsVerified
                        ? RecipeImageRightsStatuses.VerifiedCommercial
                        : RecipeImageRightsStatuses.UnverifiedTestOnly,
                    image.Provider,
                    image.Creator,
                    image.CommercialUseAllowed,
                    image.AttributionRequired,
                    image.IsVerified);
        }));

        return Ok(results);
    }
}
