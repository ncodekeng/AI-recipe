namespace Recipe.Api.Options;

public sealed class UsageControlOptions
{
    public const string SectionName = "UsageControl";

    public bool Enabled { get; init; } = true;
    public bool AiEnabled { get; init; } = true;
    public int DailyScanLimit { get; init; } = 10;
    public int DailyRecipeLimit { get; init; } = 3;
    public decimal EstimatedScanCostUsd { get; init; } = 0.02m;
    public decimal EstimatedRecipeCostUsd { get; init; } = 0.05m;
    public decimal GlobalDailyBudgetUsd { get; init; } = 50m;
}
