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
            .Where(item => !normalizer.IsPantryStaple(MatchText(item)))
            .GroupBy(item => normalizer.Normalize(MatchText(item)), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => group.First())
            .ToList();

        var available = new List<RecipeIngredient>();
        var missing = new List<RecipeIngredient>();
        foreach (var ingredient in meaningfulRequired)
        {
            if (pantryNames.Any(item => normalizer.Matches(item, MatchText(ingredient))))
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
        // The name is ours, derived from the publisher's line; the line itself is theirs. So the line gets
        // the final say, because a recipe is only dropped when its main ingredient is genuinely absent.
        // Measured on 25 November 2026: "Easy chicken casserole" was dropped from a chicken search because
        // its chicken line reduced to the name "bone-in" -- the same extraction that turned BBC Good Food's
        // dual units "150g/5½oz small mushrooms" into the name "/5½oz small mushrooms". The customer got
        // three recipes instead of four over a quantity format.
        var mainIngredientPresent = string.IsNullOrWhiteSpace(mainIngredient) ||
            meaningfulRequired.Any(item =>
                normalizer.Matches(mainIngredient, item.Name) ||
                (!string.IsNullOrWhiteSpace(item.OriginalText) &&
                    normalizer.Matches(mainIngredient, item.OriginalText)));
        // Two different questions, and the customer's is the first one.
        //
        //   coverage     = how much of THEIR basket the recipe uses   (basket utilisation)
        //   availability = how much of the RECIPE they already have   (recipe completeness)
        //
        // These were weighted 0.40 and 0.60, so a recipe was ranked mostly on how little shopping it
        // needed. That favours short recipes: something with four ingredients, three of them staples, scores
        // well while using almost nothing the customer actually has. Measured on the client's taco basket on
        // 25 November 2026, the leading result used 18% of the basket. The weights are now the other way
        // round, because somebody who put beef, tortillas and peppers in a basket wants those three cooked.
        //
        // The main ingredient needs no weight here: a recipe without it is dropped outright above, which is
        // the strongest preference the ranking can express.
        // How much of the customer's basket the recipe uses, not capped by how short the recipe is.
        var basketUtilisation = relevantPantryCount == 0
            ? 1d
            : Math.Min(1d, (double)available.Count / relevantPantryCount);

        var matchPercentage = mainIngredientPresent
            ? (int)Math.Round(
                (basketUtilisation * 0.60d + availability * 0.40d) * 100d,
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
            basketUtilisation,
            mainIngredientPresent,
            BuildMatchReason(matchPercentage, available, missing, mainIngredientPresent));
    }

    /// <summary>
    /// The text that identifies an ingredient for matching: its name, or the publisher's own line when the
    /// name carries nothing.
    ///
    /// The name is extracted by PLATE and can reduce to nothing at all. Measured on 25 November 2026,
    /// "Creamy Tuscan Chicken" lost its chicken because the line's name came out as "boneless", which is a
    /// noise word -- so it normalized to the empty string, the empty group was discarded here, and the
    /// ingredient had vanished before the main-ingredient test could look for it. A chicken search then
    /// returned three recipes instead of four. The publisher's line still said chicken breasts.
    /// </summary>
    private string MatchText(RecipeIngredient ingredient)
    {
        var name = ingredient.Name ?? string.Empty;
        return normalizer.Normalize(name).Length > 0
            ? name
            : ingredient.OriginalText ?? name;
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

        // Nothing is promoted to the front if it uses substantially less of the basket than the best
        // candidate does. The two picks below exist to lead with something worth cooking -- a traditional
        // dish a few items short, or something cookable tonight -- and neither is worth burying a far better
        // match for. Measured on 25 November 2026, a Korean basket led with a sheet-pan japchae using 24% of
        // the basket because it needed no shopping, above three recipes using 68%.
        const int promotionBand = 20;
        var bestMatch = candidates.Max(item => item.Recipe.IngredientMatch);
        bool StrongEnoughToLead(RankedRecipe item) =>
            item.Recipe.IngredientMatch >= bestMatch - promotionBand;

        var leading = new List<RankedRecipe>();
        var traditionalNearMatch = candidates
            .Where(StrongEnoughToLead)
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
            .Where(StrongEnoughToLead)
            .Where(item => leading.All(selected => selected.Recipe.Id != item.Recipe.Id))
            .OrderBy(item => item.WasRecentlyShown)
            .ThenByDescending(item => item.Recipe.IngredientMatch)
            .ThenByDescending(item => item.Score)
            .FirstOrDefault();
        if (completeMatch is not null)
        {
            leading.Add(completeMatch);
        }

        if (leading.Count == 0)
        {
            // Nothing qualified to be promoted, so lead with the best match rather than the shortest
            // shopping list. This used to pick the recipe missing fewest ingredients, which is how the
            // sheet-pan japchae reached first place: the band above kept it out of the complete-match pick
            // and this put it straight back, because needing no shopping also scores highest here.
            leading.Add(candidates
                .OrderBy(item => item.WasRecentlyShown)
                .ThenByDescending(item => item.Recipe.IngredientMatch)
                .ThenByDescending(item => item.Score)
                .First());
        }

        var selectedIds = leading.Select(item => item.Recipe.Id).ToHashSet();
        // The remainder is varied, but within bands rather than freely.
        //
        // It used to be shuffled outright, so a recipe using a fifth of the basket could land above one
        // using two thirds -- the variety was real and so was the cost of it. Banding by how much of the
        // basket each uses keeps comparable recipes shuffling against each other while a substantially
        // stronger match stays above a substantially weaker one. The band is wide enough (a fifth of the
        // scale) that near-equal recipes still swap places between searches, which is what keeps a repeated
        // search from looking frozen.
        const int bandWidth = 20;
        var variedRemainder = candidates
            .Where(item => !selectedIds.Contains(item.Recipe.Id))
            .Select(item => new
            {
                Item = item,
                Band = item.Recipe.IngredientMatch / bandWidth,
                RandomOrder = Random.Shared.Next(),
            })
            .OrderBy(item => item.Item.WasRecentlyShown)
            .ThenByDescending(item => item.Band)
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
