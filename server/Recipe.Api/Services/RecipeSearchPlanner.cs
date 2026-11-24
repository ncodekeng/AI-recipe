using System.Text;
using Recipe.Api.Models;

namespace Recipe.Api.Services;

/// <summary>
/// Turns a PLATE request into a handful of targeted search queries.
///
/// Deterministic and free. No model is asked what to search for, because nothing here needs judgement:
/// the user already told us the ingredients, the cuisine, the meal type, the restrictions and the time
/// they have. A model round to restate that would add a second of latency and a fraction of a cent to
/// every search for no gain.
///
/// It exists because the old search issued ONE broad query. Measured on 24 November 2026, a basket of
/// raw beef brisket produced exactly `beef brisket recipes`, and the four results were four chefs'
/// versions of the same slow-cooked joint. One query can only return one neighbourhood of the web.
/// Several deliberately different queries are what produce several genuinely different dinners.
/// </summary>
public static class RecipeSearchPlanner
{
    // The cuisines the app offers, in the order its own picker lists them (src/lib/cuisines.js). Used to
    // fan out when the user picked Surprise Me or gave no cuisine at all.
    private static readonly string[] Cuisines =
    [
        "Italian", "Indian", "Mexican", "Japanese", "Thai", "Chinese", "French", "Mediterranean",
        "Korean", "Caribbean", "British", "Arabic", "Eastern European", "Swedish", "Ghanaian",
    ];

    // Ways of cooking one ingredient that lead somewhere different. Used when a cuisine IS fixed, where
    // varying the cuisine is not allowed but varying the dish still is.
    private static readonly string[] Forms = ["recipe", "traybake recipe", "stew recipe", "salad recipe", "soup recipe"];

    /// <summary>
    /// The queries to run, most promising first. Always at least one, never more than <paramref name="maxQueries"/>,
    /// and never duplicated.
    /// </summary>
    public static IReadOnlyList<string> Plan(GenerateRecipesRequest request, int maxQueries = 4)
    {
        var limit = Math.Clamp(maxQueries, 1, 8);
        var queries = new List<string>();

        var subject = Subject(request);
        var qualifiers = Qualifiers(request);
        var cuisine = request.CuisinePreference.Trim();
        var surprise = cuisine.Length == 0 || cuisine.Equals("Surprise Me", StringComparison.OrdinalIgnoreCase);

        if (!surprise)
        {
            // A chosen cuisine is a requirement, not a hint. Every query keeps it and the dish form varies
            // instead, so the batch stays inside the cuisine the customer asked for.
            foreach (var form in Forms)
            {
                Add(queries, Compose(cuisine, subject, form, qualifiers), limit);
            }
        }
        else if (subject.Length == 0)
        {
            // Inspire Me with nothing at all: the cuisines themselves are the only axis available.
            foreach (var option in Cuisines)
            {
                Add(queries, Compose(option, string.Empty, "recipe", qualifiers), limit);
            }
        }
        else
        {
            // Ingredients but no cuisine. The plain query first, because it is the one most likely to
            // find the obvious dish, then the same ingredients pushed into different kitchens.
            Add(queries, Compose(string.Empty, subject, "recipe", qualifiers), limit);
            foreach (var option in Cuisines)
            {
                Add(queries, Compose(option, subject, "recipe", qualifiers), limit);
            }
        }

        // A basket of several ingredients: ask for the main one on its own as well, which finds dishes
        // that do not happen to use everything in the kitchen.
        var main = request.MainIngredient.Trim();
        if (main.Length > 0 && !string.Equals(main, subject, StringComparison.OrdinalIgnoreCase))
        {
            Add(queries, Compose(surprise ? string.Empty : cuisine, main, "recipe", qualifiers), limit);
        }

        if (queries.Count == 0)
        {
            Add(queries, "easy dinner recipe", limit);
        }

        return queries;
    }

    /// <summary>What the dish is about: the main ingredient, or the strongest few from the basket.</summary>
    private static string Subject(GenerateRecipesRequest request)
    {
        var main = request.MainIngredient.Trim();
        if (main.Length > 0)
        {
            return main;
        }

        // Three at most. A query naming the whole pantry matches nothing: no published recipe uses
        // everything in somebody's fridge.
        var names = request.Ingredients
            .Select(item => item.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return string.Join(' ', names);
    }

    /// <summary>
    /// The constraints that belong in the query text itself. Dietary needs and the meal type change which
    /// pages are worth finding; allergies deliberately do not appear, because "no peanuts" in a search box
    /// finds peanut recipes. Those are enforced by FoodSafetyRules against the real ingredient list after
    /// extraction, which is the only place a safety claim can honestly be made.
    /// </summary>
    private static string Qualifiers(GenerateRecipesRequest request)
    {
        var parts = new List<string>();

        var mealType = request.MealType.Trim();
        if (mealType.Length > 0 && !mealType.Equals("Any", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(mealType.ToLowerInvariant());
        }

        var diet = request.DietaryPreference.Trim();
        if (diet.Length > 0 && !diet.Equals("Anything", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(diet.ToLowerInvariant());
        }

        foreach (var preference in request.RecipePreferences.Take(2))
        {
            var value = preference?.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add(value.ToLowerInvariant());
            }
        }

        // "Under 30 minutes" is how publishers title these, so it is worth searching for. Above an hour
        // the phrasing stops being a useful search term and the limit is enforced after extraction.
        if (request.MaxCookingMinutes is > 0 and <= 60)
        {
            parts.Add($"under {request.MaxCookingMinutes} minutes");
        }

        var custom = request.CustomInstructions.Trim();
        if (custom.Length > 0)
        {
            // The customer's own words, as a search term and nothing more. Length is already capped at
            // 100 by the model; newlines would break the query line.
            parts.Add(custom.Replace('\n', ' ').Replace('\r', ' '));
        }

        return string.Join(' ', parts);
    }

    private static string Compose(string cuisine, string subject, string form, string qualifiers)
    {
        var builder = new StringBuilder();
        void Append(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0) return;
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(trimmed);
        }

        Append(cuisine);
        Append(subject);
        Append(form);
        Append(qualifiers);
        return builder.ToString();
    }

    private static void Add(List<string> queries, string query, int limit)
    {
        var value = query.Trim();
        if (queries.Count >= limit || value.Length == 0)
        {
            return;
        }

        if (!queries.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(value);
        }
    }
}
