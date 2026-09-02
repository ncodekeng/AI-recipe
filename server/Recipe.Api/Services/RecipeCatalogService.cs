using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeCatalogService(
    EdamamRecipeClient edamam,
    RecipeSafetyValidator safetyValidator,
    RecipeRankingService ranking,
    RecipeSearchCache cache,
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

        if (cache.TryGet(request, out var cachedResponse) && cachedResponse is not null)
        {
            return cachedResponse with
            {
                Notice = AppendNotice(cachedResponse.Notice, "Loaded from the short-term recipe cache.")
            };
        }

        try
        {
            var response = await edamam.FindRecipesAsync(request, cancellationToken);
            var safeResponse = safetyValidator.Validate(response, request);
            var result = RankAndLimit(safeResponse, request);
            cache.Store(request, result);
            return result;
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
            Recipes = ranking
                .Rank(response.Recipes, request.Ingredients, request.RecentlyShownRecipeIds)
                .Take(3)
                .ToList()
        };

    private static string AppendNotice(string? current, string message) =>
        string.IsNullOrWhiteSpace(current) ? message : $"{current} {message}";
}

public sealed class RecipeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
