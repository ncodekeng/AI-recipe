using Recipe.Api.Models;

namespace Recipe.Api.Services;

public interface IRecipeAiService
{
    Task<IngredientAnalysisResponse> AnalyzeIngredientsAsync(
        IReadOnlyList<UploadedPhoto> photos,
        CancellationToken cancellationToken);

    Task<RecipeGenerationResponse> GenerateRecipesAsync(
        GenerateRecipesRequest request,
        CancellationToken cancellationToken);
}
