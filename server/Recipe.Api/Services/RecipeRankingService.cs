using Recipe.Api.Models;

namespace Recipe.Api.Services;

public sealed class RecipeRankingService(IngredientNormalizer normalizer)
{
    public RecipeMatch CalculateMatch(
        IEnumerable<IngredientInput> pantry,
        IEnumerable<RecipeIngredient> requiredIngredients)
    {
        var pantryNames = pantry
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var meaningfulRequired = requiredIngredients
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => !normalizer.IsPantryStaple(item.Name))
            .GroupBy(item => normalizer.Normalize(item.Name), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => group.First())
            .ToList();

        var available = new List<RecipeIngredient>();
        var missing = new List<RecipeIngredient>();
        foreach (var ingredient in meaningfulRequired)
        {
            if (pantryNames.Any(item => normalizer.Matches(item, ingredient.Name)))
            {
                available.Add(ingredient);
            }
            else
            {
                missing.Add(ingredient);
            }
        }

        var requiredCount = meaningfulRequired.Count;
        var matchPercentage = requiredCount == 0
            ? 100
            : (int)Math.Round(100d * available.Count / requiredCount, MidpointRounding.AwayFromZero);
        return new RecipeMatch(available, missing, requiredCount, available.Count, matchPercentage);
    }

    public IReadOnlyList<RecipeSuggestion> Rank(
        IEnumerable<RecipeSuggestion> recipes,
        IEnumerable<IngredientInput> pantry)
    {
        return recipes
            .Select((recipe, providerIndex) =>
            {
                var match = CalculateMatch(pantry, recipe.Ingredients);
                var enriched = recipe with
                {
                    IngredientMatch = match.MatchPercentage,
                    AvailableIngredients = match.AvailableIngredients,
                    MissingIngredients = match.MissingIngredients,
                    RequiredIngredientCount = match.RequiredIngredientCount,
                    AvailableIngredientCount = match.AvailableIngredientCount
                };
                return new RankedRecipe(enriched, Score(enriched, providerIndex));
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Recipe.MissingIngredients?.Count ?? int.MaxValue)
            .ThenByDescending(item => item.Recipe.IngredientMatch)
            .Select(item => item.Recipe)
            .ToList();
    }

    private static double Score(RecipeSuggestion recipe, int providerIndex)
    {
        var missingCount = recipe.MissingIngredients?.Count ?? recipe.RequiredIngredientCount;
        var nearMatchBonus = missingCount switch
        {
            0 => 8,
            1 => 24,
            2 => 22,
            3 => 17,
            _ => Math.Max(0, 13 - ((missingCount - 3) * 4))
        };
        var providerRelevance = Math.Max(0, 32 - (providerIndex * 2));
        var usefulComplexity = Math.Min(recipe.RequiredIngredientCount, 8) * 1.2;
        var excessComplexityPenalty = Math.Max(0, recipe.RequiredIngredientCount - 12) * 1.5;

        return (recipe.IngredientMatch * 0.55) + nearMatchBonus + providerRelevance +
               usefulComplexity - excessComplexityPenalty;
    }

    private sealed record RankedRecipe(RecipeSuggestion Recipe, double Score);
}
