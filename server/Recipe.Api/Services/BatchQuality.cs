using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

/// <summary>
/// Whether a batch of results is weak enough to be worth searching again for.
///
/// The Hybrid planner tried to spot a hard basket before searching and could not: its triggers fired on 2
/// of 17 baskets on 25 November 2026, and neither was Eastern European or Swedish, the two the model
/// actually improved. Those baskets look entirely ordinary from the request. What predicted the model's
/// advantage was the quality of what came back, so this judges that instead.
///
/// Only what is knowable here. Basket utilisation is computed later, by RecipeRankingService against the
/// pantry, so it cannot be read at this point -- which is a real limit on how well a weak batch can be
/// spotted from inside the provider. What is available is how many recipes verified, and whether the
/// publishers' own stated cuisines match the one the customer asked for.
/// </summary>
public static class BatchQuality
{
    /// <param name="Weak">Whether to try again with a different plan.</param>
    /// <param name="Reason">Why, for the logs. Empty when the batch stands.</param>
    public sealed record Verdict(bool Weak, string Reason);

    private static readonly Verdict Fine = new(false, "");

    public static Verdict Assess(
        IReadOnlyList<RecipeSuggestion> accepted,
        GenerateRecipesRequest request,
        int target,
        QueryPlanningOptions planning)
    {
        if (target <= 0)
        {
            return Fine;
        }

        // Fewer recipes than asked for. The commonest kind of weak batch and the least ambiguous.
        var wanted = Math.Max(1, (int)Math.Ceiling(target * Math.Clamp(planning.FallbackVerifiedFraction, 0.1, 1.0)));
        if (accepted.Count < wanted)
        {
            return new Verdict(true, $"only {accepted.Count} of {target} verified");
        }

        // Results that are not the cuisine asked for. Judged only on what publishers actually stated: a
        // page that gave no cuisine is not evidence either way, and counting silence against a batch would
        // condemn every publisher who leaves the field empty.
        var cuisine = request.CuisinePreference?.Trim();
        if (string.IsNullOrEmpty(cuisine) ||
            cuisine.Equals("Surprise Me", StringComparison.OrdinalIgnoreCase))
        {
            return Fine;
        }

        var stated = accepted
            .Where(recipe => !string.IsNullOrWhiteSpace(recipe.Cuisine) &&
                             !recipe.Cuisine.Equals("Not stated", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (stated.Count == 0)
        {
            return Fine;
        }

        var matching = stated.Count(recipe =>
            recipe.Cuisine!.Contains(cuisine, StringComparison.OrdinalIgnoreCase) ||
            cuisine.Contains(recipe.Cuisine!, StringComparison.OrdinalIgnoreCase));
        var fraction = (double)matching / stated.Count;
        return fraction < Math.Clamp(planning.FallbackCuisineFraction, 0d, 1d)
            ? new Verdict(true, $"{matching} of {stated.Count} stated {cuisine}")
            : Fine;
    }
}
