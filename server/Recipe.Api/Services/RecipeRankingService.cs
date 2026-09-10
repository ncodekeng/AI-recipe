using Recipe.Api.Models;

namespace Recipe.Api.Services;

public sealed class RecipeRankingService(IngredientNormalizer normalizer)
{
    public RecipeMatch CalculateMatch(
        IEnumerable<IngredientInput> pantry,
        IEnumerable<RecipeIngredient> requiredIngredients,
        string? mainIngredient = null)
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
        var relevantPantryCount = pantryNames
            .Select(normalizer.Normalize)
            .Where(item => !string.IsNullOrWhiteSpace(item) && !normalizer.IsPantryStaple(item))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var relevantIngredientCount = Math.Min(relevantPantryCount, requiredCount);
        var coverage = relevantIngredientCount == 0
            ? 1d
            : Math.Min(1d, (double)available.Count / relevantIngredientCount);
        var availability = requiredCount == 0
            ? 1d
            : (double)available.Count / requiredCount;
        var mainIngredientPresent = string.IsNullOrWhiteSpace(mainIngredient) ||
            meaningfulRequired.Any(item => normalizer.Matches(mainIngredient, item.Name));
        var matchPercentage = mainIngredientPresent
            ? (int)Math.Round(
                (coverage * 0.40d + availability * 0.60d) * 100d,
                MidpointRounding.AwayFromZero)
            : 0;
        return new RecipeMatch(
            available,
            missing,
            requiredCount,
            available.Count,
            matchPercentage,
            coverage,
            availability,
            mainIngredientPresent,
            BuildMatchReason(matchPercentage, available, missing, mainIngredientPresent));
    }

    public IReadOnlyList<RecipeSuggestion> Rank(
        IEnumerable<RecipeSuggestion> recipes,
        IEnumerable<IngredientInput> pantry,
        IEnumerable<Guid>? recentlyShownRecipeIds = null,
        bool onlyUseAvailableIngredients = false,
        string? mainIngredient = null)
    {
        var recentIds = recentlyShownRecipeIds?.ToHashSet() ?? [];
        var candidates = recipes
            .Select((recipe, providerIndex) =>
            {
                var match = CalculateMatch(pantry, recipe.Ingredients, mainIngredient);
                var enriched = recipe with
                {
                    IngredientMatch = match.MatchPercentage,
                    AvailableIngredients = match.AvailableIngredients,
                    MissingIngredients = match.MissingIngredients,
                    RequiredIngredientCount = match.RequiredIngredientCount,
                    AvailableIngredientCount = match.AvailableIngredientCount,
                    MatchReason = match.MatchReason
                };
                return new RankedRecipe(
                    enriched,
                    Score(enriched, providerIndex) - (recentIds.Contains(enriched.Id) ? 35 : 0),
                    recentIds.Contains(enriched.Id),
                    match.MainIngredientPresent);
            })
            .Where(item => item.MainIngredientPresent)
            .ToList();

        if (onlyUseAvailableIngredients)
        {
            candidates = candidates
                .Where(item => MissingCount(item.Recipe) == 0)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var leading = new List<RankedRecipe>();
        var traditionalNearMatch = candidates
            .Where(item => IsTraditional(item.Recipe))
            .Where(item => MissingCount(item.Recipe) is >= 1 and <= 3)
            .OrderBy(item => item.WasRecentlyShown)
            .ThenBy(item => MissingCount(item.Recipe))
            .ThenByDescending(item => item.Recipe.IngredientMatch)
            .ThenByDescending(item => item.Score)
            .FirstOrDefault();
        if (traditionalNearMatch is not null)
        {
            leading.Add(traditionalNearMatch);
        }

        var completeMatch = candidates
            .Where(item => MissingCount(item.Recipe) == 0)
            .Where(item => leading.All(selected => selected.Recipe.Id != item.Recipe.Id))
            .OrderBy(item => item.WasRecentlyShown)
            .ThenByDescending(item => item.Score)
            .FirstOrDefault();
        if (completeMatch is not null)
        {
            leading.Add(completeMatch);
        }

        if (leading.Count == 0)
        {
            var fewestMissing = candidates.Min(item => MissingCount(item.Recipe));
            leading.Add(candidates
                .Where(item => MissingCount(item.Recipe) == fewestMissing)
                .OrderBy(item => item.WasRecentlyShown)
                .ThenByDescending(item => item.Score)
                .First());
        }

        var selectedIds = leading.Select(item => item.Recipe.Id).ToHashSet();
        var variedRemainder = candidates
            .Where(item => !selectedIds.Contains(item.Recipe.Id))
            .Select(item => new { Item = item, RandomOrder = Random.Shared.Next() })
            .OrderBy(item => item.Item.WasRecentlyShown)
            .ThenBy(item => item.RandomOrder)
            .Select(item => item.Item);

        return leading
            .Concat(variedRemainder)
            .Select(item => item.Recipe)
            .ToList();
    }

    private static int MissingCount(RecipeSuggestion recipe) =>
        recipe.MissingIngredients?.Count ?? recipe.RequiredIngredientCount;

    private static bool IsTraditional(RecipeSuggestion recipe) =>
        recipe.Tags.Any(tag => tag.Equals("Traditional", StringComparison.OrdinalIgnoreCase));

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

    private static string BuildMatchReason(
        int matchPercentage,
        IReadOnlyList<RecipeIngredient> available,
        IReadOnlyList<RecipeIngredient> missing,
        bool mainIngredientPresent)
    {
        if (!mainIngredientPresent)
        {
            return "0% match - the selected main ingredient is not part of this recipe.";
        }

        var used = string.Join(", ", available
            .Select(item => item.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6));
        var missingNames = string.Join(", ", missing
            .Select(item => item.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3));
        var usedText = string.IsNullOrWhiteSpace(used)
            ? "uses no confirmed non-pantry ingredients yet"
            : $"uses {used}";
        var missingText = missing.Count switch
        {
            0 => "no non-pantry ingredients are missing",
            1 => $"only {missingNames} is missing",
            _ => $"{missingNames} are missing"
        };
        return $"{matchPercentage}% match - {usedText}; {missingText}.";
    }

    private sealed record RankedRecipe(
        RecipeSuggestion Recipe,
        double Score,
        bool WasRecentlyShown,
        bool MainIngredientPresent);
}
