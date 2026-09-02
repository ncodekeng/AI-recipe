using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeCatalogService(
    EdamamRecipeClient edamam,
    RecipeSafetyValidator safetyValidator,
    RecipeRankingService ranking,
    IOptions<RecipeCatalogOptions> options,
    ILogger<RecipeCatalogService> logger) : IRecipeCatalogService
{
    private readonly RecipeCatalogOptions _options = options.Value;

    public async Task<RecipeGenerationResponse> FindRecipesAsync(
        GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Edamam.IsConfigured)
        {
            throw new RecipeCatalogException(
                "Real recipe search requires Edamam credentials. PLATE will not invent a replacement recipe.");
        }

        try
        {
            var response = await edamam.FindRecipesAsync(request, cancellationToken);
            var safeResponse = safetyValidator.Validate(response, request);
            return RankAndLimit(safeResponse, request);
        }
        catch (RecipeSafetyException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Edamam recipe search failed.");
            throw new RecipeCatalogException(
                "Real recipe search is temporarily unavailable. No generated recipes were substituted; please try again shortly.",
                exception);
        }
    }

    private RecipeGenerationResponse RankAndLimit(
        RecipeGenerationResponse response,
        GenerateRecipesRequest request) =>
        response with
        {
            Recipes = ranking.Rank(response.Recipes, request.Ingredients).Take(3).ToList()
        };
}

public sealed class RecipeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
