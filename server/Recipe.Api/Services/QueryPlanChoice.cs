using Recipe.Api.Models;

namespace Recipe.Api.Services;

/// <summary>
/// Whether this particular basket is worth asking a model about.
///
/// Hybrid exists because the two planners are good at different things. The dish table knows that Mexican
/// plus tortillas means tacos, instantly and for nothing, and it will go on knowing it. What it cannot do
/// is read a basket nobody anticipated -- an ingredient no concept covers, half a dozen things that only
/// go together one way, or a customer who typed what they actually wanted in their own words.
///
/// So the question is not which planner is better but which baskets the table already answers. Each reason
/// below is a case where it demonstrably does not, and every one is decided from the request alone: no
/// model is asked whether to ask a model.
/// </summary>
public static class QueryPlanChoice
{
    /// <param name="Reason">Why, for the logs and the benchmark. Empty when the table is enough.</param>
    public sealed record Decision(bool AskTheModel, string Reason);

    private static readonly Decision Deterministic = new(false, "deterministic");

    public static Decision For(
        GenerateRecipesRequest request,
        IngredientNormalizer normalizer,
        IReadOnlyList<string> deterministicQueries,
        int complexBasketSize)
    {
        // The customer's own words. The table has no way to act on "something warming" or "nothing too
        // spicy" beyond pasting it into a query, and it is the clearest signal of an intent nobody
        // anticipated.
        if (!string.IsNullOrWhiteSpace(request.CustomInstructions))
        {
            return new Decision(true, "custom instructions");
        }

        var named = request.Ingredients
            .Select(item => item.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();

        // An ingredient the dictionary has never heard of. It still normalizes -- to its own words -- but
        // nothing knows what it goes with, so no dish in the table can be scored against it.
        var unknown = named.FirstOrDefault(name => !normalizer.IsKnownConcept(name));
        if (unknown is not null)
        {
            return new Decision(true, "unfamiliar ingredient");
        }

        // Several things that only go together one or two ways. The table scores dishes one ingredient at a
        // time and cannot see a combination.
        if (named.Count >= Math.Max(2, complexBasketSize))
        {
            return new Decision(true, "complex combination");
        }

        // The table had nothing to say: no cuisine chosen and no dish matched, so the plan fell back to
        // generic forms. That is exactly the thin coverage the client's taco search suffered from.
        if (deterministicQueries.Count == 0 ||
            deterministicQueries.All(query => query.EndsWith("recipe", StringComparison.OrdinalIgnoreCase)))
        {
            return new Decision(true, "thin deterministic coverage");
        }

        return Deterministic;
    }
}
