using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeCatalogService(
    IRecipeAiService generatedRecipes,
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
        if (!UseEdamam())
        {
            var generated = await generatedRecipes.GenerateRecipesAsync(request, cancellationToken);
            return RankAndLimit(generated, request);
        }

        if (!_options.Edamam.IsConfigured)
        {
            throw new RecipeCatalogException(
                "Real recipe search is selected but its credentials are not configured.");
        }

        try
        {
            var response = await edamam.FindRecipesAsync(request, cancellationToken);
            var safeResponse = safetyValidator.Validate(response, request);
            return RankAndLimit(safeResponse, request);
        }
        catch (RecipeSafetyException exception) when (_options.UseGeneratedFallback)
        {
            logger.LogWarning(exception, "Edamam returned no usable recipes; using the configured generated fallback.");
            var fallback = await generatedRecipes.GenerateRecipesAsync(request, cancellationToken);
            return RankAndLimit(fallback with
            {
                Notice = "No suitable real recipes were returned, so generated recipe ideas are shown instead."
            }, request);
        }
        catch (RecipeSafetyException)
        {
            throw;
        }
        catch (Exception exception) when (
            _options.UseGeneratedFallback && exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Edamam recipe search failed; using the configured generated fallback.");
            var fallback = await generatedRecipes.GenerateRecipesAsync(request, cancellationToken);
            return RankAndLimit(fallback with
            {
                Notice = "Real recipe search is temporarily unavailable, so generated recipe ideas are shown instead."
            }, request);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Edamam recipe search failed.");
            throw new RecipeCatalogException(
                "Real recipe search is temporarily unavailable. Please try again shortly.",
                exception);
        }
    }

    private bool UseEdamam() =>
        _options.Provider.Equals("Edamam", StringComparison.OrdinalIgnoreCase);

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
