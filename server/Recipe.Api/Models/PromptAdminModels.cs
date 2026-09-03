namespace Recipe.Api.Models;

public sealed class UpdateAiPromptsRequest
{
    public string IngredientRecognitionPrompt { get; init; } = string.Empty;
    public string RecipeRecommendationPrompt { get; init; } = string.Empty;
}

public sealed record AiPromptSettingsResponse(
    string IngredientRecognitionPrompt,
    string RecipeRecommendationPrompt,
    bool UsingDefaults,
    DateTimeOffset? UpdatedAtUtc,
    int MaxPromptLength);
