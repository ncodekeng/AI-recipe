using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeCatalogService(
    IRecipeAiService generatedRecipes,
    EdamamRecipeClient edamam,
    RecipeSafetyValidator safetyValidator,
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
            return await generatedRecipes.GenerateRecipesAsync(request, cancellationToken);
        }

        try
        {
            var response = await edamam.FindRecipesAsync(request, cancellationToken);
            return safetyValidator.Validate(response, request);
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
            return fallback with
            {
                Notice = "Real recipe search is temporarily unavailable, so generated recipe ideas are shown instead."
            };
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
        _options.Provider.Equals("Edamam", StringComparison.OrdinalIgnoreCase) &&
        _options.Edamam.IsConfigured;
}

public sealed class RecipeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
