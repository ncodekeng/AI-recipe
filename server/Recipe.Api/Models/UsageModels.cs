namespace Recipe.Api.Models;

public sealed record UsageStatusResponse(
    bool AiEnabled,
    string ResetsAtUtc,
    int ScansUsed,
    int ScanLimit,
    int ScansRemaining,
    int RecipesUsed,
    int RecipeLimit,
    int RecipesRemaining,
    bool CanReset,
    bool IsUnlimited = false);
