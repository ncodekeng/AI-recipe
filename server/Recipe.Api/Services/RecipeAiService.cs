using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeAiService(
    AzureOpenAiClient azure,
    DemoFoodAiService demo,
    IOptions<FoodAiOptions> options,
    ILogger<RecipeAiService> logger) : IRecipeAiService
{
    private readonly FoodAiOptions _options = options.Value;

    public async Task<IngredientAnalysisResponse> AnalyzeIngredientsAsync(
        IReadOnlyList<UploadedPhoto> photos,
        CancellationToken cancellationToken)
    {
        if (!UseAzure())
        {
            return demo.AnalyzeIngredients(photos);
        }

        try
        {
            return await azure.AnalyzeIngredientsAsync(photos, cancellationToken);
        }
        catch (Exception exception) when (_options.UseDemoFallback && exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Azure ingredient analysis failed; using the demo provider.");
            var fallback = demo.AnalyzeIngredients(photos);
            return fallback with
            {
                Notice = "Azure could not be reached, so the prototype switched to demo recognition."
            };
        }
    }

    public async Task<RecipeGenerationResponse> GenerateRecipesAsync(
        GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        if (!UseAzure())
        {
            return demo.GenerateRecipes(request);
        }

        try
        {
            return await azure.GenerateRecipesAsync(request, cancellationToken);
        }
        catch (Exception exception) when (_options.UseDemoFallback && exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Azure recipe generation failed; using the demo provider.");
            var fallback = demo.GenerateRecipes(request);
            return fallback with
            {
                Notice = "Azure could not be reached, so the prototype switched to locally generated demo recipes."
            };
        }
    }

    private bool UseAzure() =>
        _options.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase) && azure.IsConfigured;
}
