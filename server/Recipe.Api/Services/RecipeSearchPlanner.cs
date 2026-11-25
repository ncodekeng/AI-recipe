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

    // The dishes themselves live in CuisineDishes, which replaced the one-dish-per-cuisine list that used to
    // sit here. That list existed only for Inspire Me with an empty basket; every other search fell back to
    // the generic Forms above, which is why a Mexican basket of tortillas was never asked about tacos.
    //
    // A cuisine on its own is too broad to be a recipe query either way. Measured on 24 November 2026,
    // asking for "italian recipe" and its siblings returned 19 roundup pages out of 31 candidates -- "30
    // best Italian dinners" and the like -- and Inspire Me came back with a single recipe. A query that
    // names a dish gets pages about that dish, because that is what the publisher wrote the page for.

    /// <summary>
    /// The queries to run, most promising first. Always at least one, never more than <paramref name="maxQueries"/>,
    /// and never duplicated.
    /// </summary>
    /// <param name="skip">
    /// How many of the planned queries to pass over, so "Show more results" reaches pages the first batch
    /// never looked at. The plan is longer than one batch on purpose -- fifteen cuisines, fifteen signature
    /// dishes -- and without this a second tap re-ran the same four queries, found the same pages and had
    /// nothing new to offer. Returns empty once the plan is exhausted, which is the honest end of the list.
    /// </param>
    /// <param name="searchFocus">
    /// The basket's strongest few ingredients, already chosen by IngredientNormalizer: one substantial
    /// protein first, then staple bases, then useful produce, with pantry staples left out. Supply it for a
    /// basket of several ingredients. Without it the first three are used in the order the basket happens to
    /// be in, and measured on 25 November 2026 a basket of salt, olive oil, water, chicken thighs and rice
    /// searched for "salt olive oil water" and returned marinated olives and an olive oil bread dip.
    /// </param>
    /// <param name="normalizer">
    /// Supply it and the plan can name dishes: the basket's ingredients are resolved to concepts and matched
    /// against CuisineDishes, so a Mexican basket holding tortillas is searched for as tacos and fajitas
    /// rather than as "Mexican beef bread pepper stew". Without it the generic forms are used, which is what
    /// the plan did for every cuisine before.
    /// </param>
    public static IReadOnlyList<string> Plan(
        GenerateRecipesRequest request,
        int maxQueries = 4,
        int skip = 0,
        IReadOnlyList<string>? searchFocus = null,
        IngredientNormalizer? normalizer = null)
    {
        var limit = Math.Clamp(maxQueries, 1, 8);
        var queries = new List<string>();

        var subject = Subject(request, searchFocus);
        var qualifiers = Qualifiers(request);
        var cuisine = request.CuisinePreference.Trim();
        var surprise = cuisine.Length == 0 || cuisine.Equals("Surprise Me", StringComparison.OrdinalIgnoreCase);
        var concepts = BasketConcepts(request, normalizer);

        if (!surprise)
        {
            // The cuisine's own dishes first, best fit for this basket leading. Naming the dish is what
            // finds the dish: "tacos beef pepper recipe" returns tacos, where "Mexican beef pepper stew
            // recipe" returns whatever happens to have Mexican in its title.
            foreach (var dish in CuisineDishes.For(cuisine, concepts, request.MealType))
            {
                Add(queries, Compose(CuisineWordFor(dish), dish.Name, SubjectFor(dish, subject, normalizer), "recipe", qualifiers));
            }

            // The generic forms stay, after the dishes rather than instead of them. They are what a later
            // Show More batch reaches for once the named dishes are spent, and the fallback for a cuisine
            // this table says nothing about.
            foreach (var form in Forms)
            {
                Add(queries, Compose(cuisine, subject, form, qualifiers));
            }
        }
        else if (subject.Length == 0)
        {
            // Inspire Me with nothing at all: the cuisines are the only axis available, each anchored to a
            // dish so the query asks for a recipe rather than for a magazine round-up.
            foreach (var option in Cuisines)
            {
                var dish = CuisineDishes.For(option, concepts, request.MealType).FirstOrDefault();
                Add(queries, Compose(option, dish?.Name ?? string.Empty, "recipe", qualifiers));
            }
        }
        else
        {
            // Ingredients but no cuisine. The plain query first, because it is the one most likely to find
            // the obvious dish.
            Add(queries, Compose(string.Empty, subject, "recipe", qualifiers));

            // An ingredient that belongs to a kitchen is asked about in that kitchen, on its own.
            //
            // Measured on 25 November 2026, a basket of nopales, plantain, gochujang and samphire produced
            // "miso soup nopale plantain gochujang recipe" and five more like it: every ingredient pasted
            // into every cuisine's default dish. Those four come from four different kitchens and no
            // publisher has ever written a page about all of them. Asked separately -- nopales as Mexican,
            // gochujang as Korean, plantain as Caribbean -- each one reaches pages that exist.
            foreach (var (ingredient, kitchen) in SignaturePairs(request))
            {
                var dish = CuisineDishes.For(kitchen, concepts, request.MealType).FirstOrDefault();
                Add(queries, dish is null
                    ? Compose(kitchen, ingredient, "recipe", qualifiers)
                    : Compose(CuisineWordFor(dish, kitchen), dish.Name, ingredient, "recipe", qualifiers));
            }

            // Then the whole basket pushed into each kitchen, as before.
            foreach (var option in Cuisines)
            {
                var dish = CuisineDishes.For(option, concepts, request.MealType).FirstOrDefault();
                Add(queries, dish is null
                    ? Compose(option, subject, "recipe", qualifiers)
                    : Compose(CuisineWordFor(dish, option), dish.Name, SubjectFor(dish, subject, normalizer), "recipe", qualifiers));
            }
        }

        // A basket of several ingredients: ask for the main one on its own as well, which finds dishes
        // that do not happen to use everything in the kitchen.
        var main = request.MainIngredient.Trim();
        if (main.Length > 0 && !string.Equals(main, subject, StringComparison.OrdinalIgnoreCase))
        {
            Add(queries, Compose(surprise ? string.Empty : cuisine, main, "recipe", qualifiers));
        }

        if (queries.Count == 0)
        {
            Add(queries, "easy dinner recipe");
        }

        // The whole plan is built first and sliced here, so one batch is a window onto it rather than the
        // only thing it could produce. With skip at zero this is exactly the first batch it always was.
        return queries.Skip(Math.Max(0, skip)).Take(limit).ToList();
    }

    /// <summary>What the dish is about: the main ingredient, or the strongest few from the basket.</summary>
    private static string Subject(GenerateRecipesRequest request, IReadOnlyList<string>? searchFocus)
    {
        var main = request.MainIngredient.Trim();
        if (main.Length > 0)
        {
            return main;
        }

        // The chosen few, where the caller worked out which they are. Three at most either way: a query
        // naming the whole pantry matches nothing, because no published recipe uses everything in
        // somebody's fridge.
        if (searchFocus is { Count: > 0 })
        {
            return string.Join(' ', searchFocus.Take(3));
        }

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

    private static string Compose(params string[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Each ingredient that belongs to a kitchen, paired with it, most telling ingredient first.
    ///
    /// The ingredient is passed on in the customer's own words rather than as a concept, because an
    /// ingredient the table recognises by name is often one the normalizer does not: "gochujang" is in the
    /// knowledge base and not in the concept table, and it is what a publisher titles the page with.
    /// </summary>
    private static IEnumerable<(string Ingredient, string Cuisine)> SignaturePairs(GenerateRecipesRequest request)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in request.Ingredients
                     .Select(item => item.Name?.Trim() ?? string.Empty)
                     .Where(name => name.Length > 0))
        {
            foreach (var cuisine in CuisineDishes.CuisinesForIngredient(name).Take(2))
            {
                if (seen.Add($"{name}|{cuisine}"))
                {
                    yield return (name, cuisine);
                }
            }
        }
    }

    /// <summary>The basket's ingredients as normalizer concepts, for scoring which dishes fit.</summary>
    private static ISet<string> BasketConcepts(GenerateRecipesRequest request, IngredientNormalizer? normalizer)
    {
        var concepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (normalizer is null)
        {
            return concepts;
        }

        foreach (var name in request.Ingredients.Select(item => item.Name).Append(request.MainIngredient))
        {
            var concept = normalizer.Normalize(name ?? string.Empty);
            if (concept.Length > 0)
            {
                concepts.Add(concept);
            }
        }

        return concepts;
    }

    /// <summary>
    /// Whether to put the cuisine's name in front of a dish.
    ///
    /// A dish only its own cuisine cooks already says which cuisine it is: "tacos" needs no help, and adding
    /// the word pushes the search towards pages with Mexican in the title instead of pages about tacos --
    /// which is what the client saw. A dish several cuisines share does need it, because "stir fry" alone
    /// could be Thai or Chinese and "traybake" could be any of three.
    /// </summary>
    private static string CuisineWordFor(CuisineDishes.Dish dish, string? cuisine = null) =>
        CuisineDishes.IsShared(dish.Name) ? (cuisine ?? CuisineDishes.CuisineOf(dish) ?? string.Empty) : string.Empty;

    /// <summary>
    /// The ingredients worth naming alongside a dish.
    ///
    /// The dish name already implies its carrier -- tacos imply tortillas, risotto implies rice, pad thai
    /// implies rice noodles -- so repeating it only crowds the query. The protein and the vegetables stay,
    /// because those are what make this basket's version of the dish.
    /// </summary>
    private static string SubjectFor(CuisineDishes.Dish dish, string subject, IngredientNormalizer? normalizer)
    {
        if (normalizer is null || subject.Length == 0)
        {
            return subject;
        }

        var kept = subject
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !normalizer.IsCarrierBase(word))
            .ToList();

        // Never strip the subject away entirely: a dish with nothing named beside it is a weaker query than
        // one that repeats a word.
        return kept.Count == 0 ? subject : string.Join(' ', kept);
    }

    private static void Add(List<string> queries, string query)
    {
        var value = query.Trim();
        if (value.Length == 0)
        {
            return;
        }

        if (!queries.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(value);
        }
    }
}
