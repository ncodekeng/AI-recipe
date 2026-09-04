using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeCatalogService(
    AzureGroundedRecipeClient azureWebSearch,
    EdamamRecipeClient edamam,
    CommercialRecipeImageClient commercialImages,
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
        var provider = _options.Provider.Trim();
        var useAzureWebSearch = provider.Equals("AzureWebSearch", StringComparison.OrdinalIgnoreCase);
        var useEdamam = provider.Equals("Edamam", StringComparison.OrdinalIgnoreCase);

        if (!useAzureWebSearch && !useEdamam)
        {
            throw new RecipeCatalogException(
                $"Unknown recipe provider '{provider}'. Use AzureWebSearch or Edamam.");
        }

        if (useAzureWebSearch && !azureWebSearch.IsConfigured)
        {
            throw new RecipeCatalogException(
                "Azure web-grounded recipe search requires an Azure OpenAI endpoint, API key, and deployment. PLATE will not invent a replacement recipe.");
        }

        if (useEdamam && !_options.Edamam.IsConfigured)
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
            var response = useAzureWebSearch
                ? await azureWebSearch.FindRecipesAsync(request, cancellationToken)
                : await edamam.FindRecipesAsync(request, cancellationToken);
            var safeResponse = safetyValidator.Validate(response, request);
            var rankedResponse = RankAndLimit(safeResponse, request);
            var result = await ApplyCommercialImagesAsync(
                rankedResponse,
                request.ShowPhotos,
                cancellationToken);
            cache.Store(request, result);
            return result;
        }
        catch (RecipeSafetyException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "{Provider} recipe search failed.", provider);
            throw new RecipeCatalogException(
                "Real recipe search is temporarily unavailable. No replacement recipe was invented; please try again shortly.",
                exception);
        }
    }

    private RecipeGenerationResponse RankAndLimit(
        RecipeGenerationResponse response,
        GenerateRecipesRequest request)
    {
        var recipes = ranking
            .Rank(
                response.Recipes,
                request.Ingredients,
                request.RecentlyShownRecipeIds,
                request.OnlyUseAvailableIngredients)
            .Take(6)
            .ToList();
        if (request.OnlyUseAvailableIngredients && recipes.Count == 0)
        {
            throw new RecipeSafetyException(
                "No cited recipes used only the ingredients in your Kitchen Memory. Try Show all recipes or add another ingredient.");
        }

        return response with { Recipes = recipes };
    }

    private async Task<RecipeGenerationResponse> ApplyCommercialImagesAsync(
        RecipeGenerationResponse response,
        bool showPhotos,
        CancellationToken cancellationToken)
    {
        var tasks = response.Recipes.Select(async recipe =>
        {
            if (!showPhotos || !commercialImages.IsEnabled)
            {
                return ClearImage(recipe);
            }

            var image = await commercialImages.FindAsync(recipe.Title, cancellationToken);
            return image is null
                ? ClearImage(recipe)
                : recipe with
                {
                    ImageUrl = image.ImageUrl,
                    ImageSourceUrl = image.SourceUrl,
                    ImageLicenseType = image.LicenseType,
                    ImageLicenseUrl = image.LicenseUrl,
                    ImageAttributionRequirements = image.AttributionRequirements,
                    ImageRightsStatus = image.IsVerified
                        ? RecipeImageRightsStatuses.VerifiedCommercial
                        : RecipeImageRightsStatuses.UnverifiedTestOnly
                };
        });

        return response with { Recipes = await Task.WhenAll(tasks) };
    }

    private static RecipeSuggestion ClearImage(RecipeSuggestion recipe) => recipe with
    {
        ImageUrl = null,
        ImageSourceUrl = null,
        ImageLicenseType = null,
        ImageLicenseUrl = null,
        ImageAttributionRequirements = null,
        ImageRightsStatus = RecipeImageRightsStatuses.Unavailable
    };

    private static string AppendNotice(string? current, string message) =>
        string.IsNullOrWhiteSpace(current) ? message : $"{current} {message}";
}

public sealed class RecipeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
