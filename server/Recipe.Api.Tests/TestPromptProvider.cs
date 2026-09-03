using Recipe.Api.Services;

namespace Recipe.Api.Tests;

internal sealed class TestPromptProvider(
    string revision = "test-prompts-v1",
    string? ingredientRecognitionPrompt = null,
    string? recipeRecommendationPrompt = null) : IAiPromptProvider
{
    public AiPromptSnapshot Current { get; private set; } = new(
        ingredientRecognitionPrompt ?? AiPromptDefaults.IngredientRecognition,
        recipeRecommendationPrompt ?? AiPromptDefaults.RecipeRecommendation,
        ingredientRecognitionPrompt is null && recipeRecommendationPrompt is null,
        null,
        revision);

    public void ChangeRevision(string revision)
    {
        Current = Current with { Revision = revision, UsingDefaults = false };
    }
}
