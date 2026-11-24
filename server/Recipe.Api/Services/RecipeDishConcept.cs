using System.Text.RegularExpressions;

namespace Recipe.Api.Services;

/// <summary>
/// Tells one dish from another, and a recipe from a page that merely lists recipes.
///
/// Both exist because a search for one basket kept coming back as the same dinner several times over.
/// Measured on 24 November 2026, asking for four recipes from chicken, rice and tomato returned
/// "Garlic and Tomato Chicken with Rice", "One pot tomato basil chicken rice", "One Pot Chicken and
/// Tomato Rice" and "One Pot Italian Chicken and Rice" -- four publishers, one dish. Deduplication ran on
/// the source URL alone, so four pages about one dinner counted as four results.
///
/// The same measurement turned up a second fault: "41 Best Brisket Recipes &amp; Ideas" and a bare
/// "Beef brisket recipes" were returned as if they were recipes. They are category indexes. Someone who
/// taps one gets a list of links rather than something to cook.
/// </summary>
public static class RecipeDishConcept
{
    // Words that describe how a recipe is written up rather than what the dish is. Removing them lets
    // "One Pot Chicken and Tomato Rice" and "Garlic and Tomato Chicken with Rice" be recognised as the
    // same dinner, which is the whole point.
    //
    // Deliberately conservative about cooking methods. "slow" and "roast" are here, but "barbecue",
    // "curry" and "stew" are not: they change what the dish IS, and merging them would hide real variety
    // rather than reveal duplication.
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "with", "in", "of", "for", "on", "to", "from", "your", "my", "our",
        "easy", "best", "quick", "simple", "perfect", "ultimate", "classic", "authentic", "homemade",
        "delicious", "amazing", "favourite", "favorite", "great", "super", "real", "proper", "traditional",
        "recipe", "recipes", "style", "styled", "make", "makes", "made", "making", "how",
        "minute", "minutes", "min", "mins", "hour", "hours",
        "one", "two", "1", "2", "pot", "pan", "tray", "sheet", "dish", "bowl", "skillet",
        "oven", "slow", "cooker", "instant", "air", "fryer", "pressure", "batch", "cook", "cooked",
        "cooking", "bake", "baked", "fashioned", "old", "new",
        "weeknight", "night", "dinner", "lunch", "supper", "meal", "serves", "serving", "servings",
        "ever", "ideas", "idea", "guide", "simply", "really", "very", "so",
    };

    // Words that decide what a dish IS rather than how it is written up. Two titles that disagree on
    // these are different dinners however much else they share, which is what separates "Barbecue Beef
    // Brisket" from "Slow-Roast Beef Brisket" -- two words apart, but nobody would call them one recipe.
    //
    // Overlap scoring alone cannot make that call: those two share "beef" and "brisket" and differ by one
    // token, scoring higher than "Garlic and Tomato Chicken with Rice" against "One pot tomato basil
    // chicken rice", which genuinely IS one dinner. Method and form have to be compared separately.
    private static readonly HashSet<string> Distinguishers = new(StringComparer.OrdinalIgnoreCase)
    {
        "barbecue", "bbq", "grill", "grilled", "fried", "fry", "stir", "roast", "roasted", "smoked",
        "braised", "poached", "steamed", "pickled", "marinated", "stuffed", "battered", "crumbed",
        "curry", "stew", "soup", "broth", "salad", "sandwich", "toastie", "burger", "wrap", "taco",
        "burrito", "pie", "tart", "quiche", "frittata", "omelette", "pancake", "fritter", "risotto",
        "paella", "casserole", "lasagne", "lasagna", "pizza", "kebab", "skewer", "noodle", "noodles",
        "ramen", "pho", "sushi", "dumpling", "pasty", "chilli", "chili", "ragu", "bolognese",
        "carbonara", "tagine", "biryani", "pilaf", "pilau", "stroganoff", "schnitzel", "meatball",
        "meatballs", "nugget", "nuggets", "wellington", "hotpot", "crumble", "gratin", "parmigiana",
    };

    // Tokens are stemmed before comparison, so the lookup set has to be stemmed the same way or
    // "barbecue" would never match the "barbecu" a title reduces to.
    private static readonly HashSet<string> DistinguisherStems =
        Distinguishers.Select(Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // A title that is a list of recipes rather than one. Checked after the publisher suffix is removed,
    // so "Beef brisket recipes - Jamie Oliver" is caught by the same rule as a bare "Beef brisket recipes".
    private static readonly Regex CountedRoundup = new(@"^\s*\d+\s", RegexOptions.Compiled);
    private static readonly Regex RoundupPhrase = new(
        @"\b(best|top|favourite|favorite)\b[^.]*\brecipes\b|\brecipe\s+(ideas|collection|roundup)\b|\bcollection\b|\bround[\s-]?up\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EndsInRecipesPlural = new(@"\brecipes\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NonWord = new(@"[^\p{L}\p{N}\s]+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    // Category, tag and search paths. A real recipe lives at a path of its own; these are indexes.
    private static readonly string[] IndexPathMarkers =
    [
        "/search", "/tag/", "/tags/", "/category/", "/categories/", "/collection", "/topics/", "/cuisine/",
    ];

    /// <summary>
    /// Is this a roundup, category index or search-results page rather than a single recipe?
    /// </summary>
    public static bool IsListingPage(string? title, string? sourceUrl)
    {
        var dish = StripPublisherSuffix(title);

        if (!string.IsNullOrWhiteSpace(dish))
        {
            // "41 Best Brisket Recipes & Ideas"
            if (CountedRoundup.IsMatch(dish) && dish.Contains("recipe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // "Best brisket recipes", "Recipe ideas", "Our favourite chicken recipes"
            if (RoundupPhrase.IsMatch(dish))
            {
                return true;
            }

            // A bare plural: "Beef brisket recipes". A single recipe is titled "... Recipe", not "... Recipes".
            if (EndsInRecipesPlural.IsMatch(dish))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(sourceUrl) || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');

        // A query string on a recipe page is usually a search: /recipes?q=brisket
        if (!string.IsNullOrEmpty(uri.Query))
        {
            return true;
        }

        if (IndexPathMarkers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // The section root itself -- /recipes, /recipes/beef -- rather than a page beneath it. A real
        // recipe URL carries a slug naming the dish, which is longer than one short segment.
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return true;
        }

        return segments.Length <= 2
            && segments[0].Equals("recipes", StringComparison.OrdinalIgnoreCase)
            && (segments.Length == 1 || segments[1].Length <= 12);
    }

    /// <summary>
    /// The dish a title describes, reduced to its content words so that two write-ups of one dinner match.
    /// Returns an empty string when a title says nothing identifying.
    /// </summary>
    public static string Normalize(string? title)
    {
        var tokens = Tokens(title);
        return tokens.Count == 0 ? string.Empty : string.Join(' ', tokens);
    }

    /// <summary>
    /// Are these two titles the same dish?
    ///
    /// Two tests, because one does not work. First the dish must agree on method and form: a barbecue and
    /// a roast are different dinners even when every other word matches. Only then does word overlap
    /// decide, at 0.55, a threshold measured against real search output rather than picked -- "Garlic and
    /// Tomato Chicken with Rice" against "One pot tomato basil chicken rice" scores 0.6 and is one dinner
    /// written up twice, which is exactly the duplication worth removing.
    /// </summary>
    public static bool IsSameDish(string? first, string? second)
    {
        var a = Tokens(first);
        var b = Tokens(second);
        if (a.Count == 0 || b.Count == 0)
        {
            // Nothing identifying to compare. Treating that as "same" would silently drop recipes.
            return false;
        }

        // A method or form named in one title and not the other means a different dinner, whatever the
        // overlap says. Checked before scoring so no threshold can override it.
        var methodsA = a.Where(DistinguisherStems.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var methodsB = b.Where(DistinguisherStems.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!methodsA.SetEquals(methodsB))
        {
            return false;
        }

        if (a.SetEquals(b))
        {
            return true;
        }

        var shared = a.Count(token => b.Contains(token));
        var union = a.Count + b.Count - shared;
        return union > 0 && (double)shared / union >= 0.55;
    }

    private static HashSet<string> Tokens(string? title)
    {
        var dish = StripPublisherSuffix(title);
        if (string.IsNullOrWhiteSpace(dish))
        {
            return [];
        }

        var cleaned = WhitespaceRun.Replace(NonWord.Replace(dish, " "), " ").Trim();
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Single letters carry no dish identity and would inflate the overlap score.
            if (token.Length > 1 && !Filler.Contains(token))
            {
                tokens.Add(Stem(token));
            }
        }

        return tokens;
    }

    /// <summary>
    /// Enough stemming to match the forms one word actually takes across recipe titles: "cheese" and
    /// "cheesy", "tomato" and "tomatoes", "egg" and "eggy". Without it, "Bread with Egg and Cheese" and
    /// "Cheesy Egg Bread Bake" -- which a live search returned as two of its three results -- score as
    /// different dishes on a word they share.
    ///
    /// Deliberately crude. A real stemmer would be a dependency and a source of surprises; this only has
    /// to make two spellings of one ingredient meet, and it never shortens a word below three letters,
    /// where trimming starts merging words that have nothing to do with each other.
    /// </summary>
    private static string Stem(string token)
    {
        var value = token.ToLowerInvariant();
        if (value.Length > 3 && value.EndsWith("es", StringComparison.Ordinal))
        {
            value = value[..^2];
        }
        else if (value.Length > 3 && value.EndsWith('s'))
        {
            value = value[..^1];
        }

        if (value.Length > 3 && (value.EndsWith('e') || value.EndsWith('y')))
        {
            value = value[..^1];
        }

        return value;
    }

    /// <summary>
    /// Drops a trailing publisher credit -- "... - Jamie Oliver", "... | BBC Good Food" -- which names the
    /// site rather than the dish and would otherwise make two write-ups of one dinner look different.
    /// </summary>
    private static string StripPublisherSuffix(string? title)
    {
        var value = (title ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var cut = value.LastIndexOfAny(['|', '–', '—']);
        if (cut > 0)
        {
            value = value[..cut];
        }

        // " - " only with spaces around it: hyphenated dishes such as "Slow-Roast Beef" must survive.
        var dash = value.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            value = value[..dash];
        }

        return value.Trim();
    }
}
