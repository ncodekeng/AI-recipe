using Recipe.Api.Models;

namespace Recipe.Api.Services;

public interface IRecipeCatalogService
{
    Task<RecipeGenerationResponse> FindRecipesAsync(
        GenerateRecipesRequest request,
        CancellationToken cancellationToken);
}
