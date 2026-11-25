using System.Text.RegularExpressions;
using Recipe.Api.Models;

namespace Recipe.Api.Services;

public sealed partial class IngredientNormalizer
{
    private static readonly (string Canonical, string[] Aliases)[] Concepts =
    [
        ("black pepper", ["freshly ground black pepper", "ground black pepper", "black peppercorns", "black pepper"]),
        ("olive oil", ["extra virgin olive oil", "virgin olive oil", "olive oil"]),
        ("cooking oil", ["vegetable oil", "sunflower oil", "canola oil", "rapeseed oil", "cooking oil"]),
        ("bell pepper", ["red bell peppers", "green bell peppers", "yellow bell peppers", "orange bell peppers", "bell peppers", "red bell pepper", "green bell pepper", "yellow bell pepper", "orange bell pepper", "sweet peppers", "sweet pepper", "red peppers", "green peppers", "yellow peppers", "capsicums", "capsicum", "peppers", "pepper"]),
        ("chicken stock", ["chicken stock cubes", "chicken stock cube", "chicken broth", "chicken stock"]),
        ("beef stock", ["beef stock cubes", "beef stock cube", "beef broth", "beef stock"]),
        ("fish stock", ["fish stock cubes", "fish stock cube", "fish broth", "fish stock"]),
        ("vegetable stock", ["vegetable stock cubes", "vegetable stock cube", "vegetable broth", "vegetable stock"]),
        ("chicken", ["boneless skinless chicken breasts", "skinless chicken breasts", "chicken breasts", "chicken breast", "chicken thighs", "chicken thigh", "chicken"]),
        ("spring onion", ["spring onions", "spring onion", "green onions", "green onion", "scallions", "scallion"]),
        ("onion", ["red onions", "white onions", "yellow onions", "red onion", "white onion", "yellow onion", "shallots", "shallot", "onions", "onion"]),
        ("garlic", ["garlic cloves", "garlic clove", "garlic bulb", "garlic"]),
        ("tomato", ["cherry tomatoes", "plum tomatoes", "roma tomatoes", "chopped tomatoes", "tomatoes", "tomato"]),
        ("potato", ["baby potatoes", "new potatoes", "potatoes", "potato"]),
        ("carrot", ["carrots", "carrot"]),
        ("broccoli", ["broccoli florets", "broccoli"]),
        ("spinach", ["baby spinach", "spinach leaves", "spinach"]),
        ("mushroom", ["button mushrooms", "chestnut mushrooms", "mushrooms", "mushroom"]),
        // The same vegetable under both of its names. PLATE's market is en-GB and its customers type
        // "aubergine", but the publishers it searches are global. Measured on 25 November 2026, a veg
        // search dropped "Spanish Eggplant (Aubergine)" -- a page whose own title says aubergine -- because
        // its ingredient line said eggplant and nothing here connected the two, so RecipeRankingService
        // judged the main ingredient absent and the customer got two recipes instead of four. Only that
        // pair was measured; the rest are the same naming split waiting to happen.
        ("aubergine", ["aubergines", "eggplants", "eggplant", "aubergine"]),
        ("courgette", ["courgettes", "zucchinis", "zucchini", "courgette"]),
        ("rocket", ["rocket leaves", "arugula", "rocket"]),
        ("beetroot", ["beetroots", "beets", "beet", "beetroot"]),
        ("chickpea", ["chickpeas", "chick peas", "garbanzo beans", "garbanzos", "chickpea"]),
        ("swede", ["swedes", "rutabaga", "swede"]),
        ("mangetout", ["snow peas", "sugar snap peas", "mangetout"]),
        // Fresh coriander is cilantro, but the ground seed is a different ingredient altogether. It is
        // listed first so it matches first, instead of collapsing a spice into the herb.
        ("coriander seed", ["ground coriander", "coriander seeds", "coriander powder", "coriander seed"]),
        ("coriander", ["fresh coriander", "coriander leaves", "coriander leaf", "cilantro", "coriander"]),
        // ---- Found by auditing the table for the collision that broke the taco search ----------------
        //
        // Each of these used to be swallowed by a shorter, more generic alias, which is now impossible
        // because the most specific alias wins. They are listed because each one changes what the dish IS,
        // or changes the answer to "do I already have this?", which is what the whole table is for.
        //
        // A tortilla is not bread. That collision is the one the client hit: a basket of beef, taco bread
        // and peppers was searched for as beef, BREAD and peppers.
        ("tortilla", ["corn tortillas", "flour tortillas", "soft tortillas", "tortilla wraps",
            "taco bread", "taco shells", "taco shell", "tortillas", "tortilla"]),
        // A sweet potato is not a potato: different vegetable, different dish, different shelf.
        ("sweet potato", ["sweet potatoes", "sweet potato", "yams", "yam"]),
        // Rice noodles are neither rice nor, for a shopping trip, interchangeable with pasta.
        ("rice noodle", ["rice noodles", "rice noodle", "rice vermicelli"]),
        // Rice vinegar is not rice, and the dried spices are not the fresh bulb or the fresh onion.
        ("rice vinegar", ["rice wine vinegar", "rice vinegar"]),
        ("garlic powder", ["garlic powder", "garlic granules"]),
        ("onion powder", ["onion powder", "onion granules"]),
        // Coconut milk is a tin from the cupboard, not dairy and not a fresh coconut.
        ("coconut milk", ["coconut milk", "coconut cream"]),
        // Peanut butter keeps "peanut" in its canonical name. Allergy and dietary exclusion are enforced by
        // FoodSafetyRules, which has its own terms and its own matching and never consults this table, so
        // nothing here can widen or narrow an allergy decision -- but the name stays honest regardless.
        ("peanut butter", ["peanut butter"]),
        // Pulses, which the table had no concept for at all: a basket of lentils matched nothing, and the
        // cuisine dish table needs them to know a dal or a cassoulet is worth searching for.
        ("bean", ["kidney beans", "black beans", "cannellini beans", "butter beans", "borlotti beans",
            "haricot beans", "baked beans", "mixed beans", "beans", "bean"]),
        ("lentil", ["red lentils", "green lentils", "puy lentils", "brown lentils", "lentils", "lentil"]),
        ("avocado", ["avocados", "avocado"]),
        ("lemon", ["lemon juice", "lemon zest", "lemons", "lemon"]),
        ("lime", ["lime juice", "lime zest", "limes", "lime"]),
        ("salmon", ["salmon fillets", "salmon fillet", "salmon"]),
        ("shrimp", ["king prawns", "prawns", "prawn", "shrimps", "shrimp"]),
        // "tuna steak" is spelled out because the bare "steak" alias under beef is one word and longer than
        // "tuna", so without it a tuna steak resolved to beef.
        ("fish", ["white fish fillets", "white fish", "fish fillets", "fish fillet", "tuna steaks",
            "tuna steak", "haddock", "tilapia", "cod", "tuna", "fish"]),
        // The cuts belong here for the same reason "steak" already does: a customer holding a brisket and a
        // recipe calling for brisket are the same shopping trip. Without them "beef brisket" collapsed to
        // the concept "beef" while a line reading just "brisket" stayed "brisket", the two never matched,
        // and RecipeRankingService dropped the recipe for not containing the main ingredient -- measured on
        // 24 November 2026, four brisket recipes found and three returned, on every brisket search.
        ("beef", ["ground beef", "minced beef", "beef mince", "beef steak", "steak", "beef brisket",
            "brisket", "silverside", "topside", "braising steak", "stewing beef", "short rib", "beef"]),
        ("pork", ["pork chops", "pork chop", "pork tenderloin", "pork mince", "pork"]),
        ("lamb", ["lamb shanks", "lamb shank", "lamb chops", "lamb chop", "lamb mince", "lamb"]),
        ("egg", ["free range eggs", "free-range eggs", "eggs", "egg"]),
        ("bread", ["toasted bread", "breadcrumbs", "bread crumbs", "baguette", "bruschetta", "toast", "bread"]),
        ("rice", ["basmati rice", "jasmine rice", "brown rice", "white rice", "cooked rice", "risotto rice", "rice"]),
        ("pasta", ["spaghetti", "linguine", "tagliatelle", "penne", "fusilli", "macaroni", "noodles", "pasta"]),
        ("feta", ["feta cheese", "feta"]),
        ("cheddar", ["cheddar cheese", "cheddar"]),
        ("mozzarella", ["mozzarella cheese", "mozzarella"]),
        ("parmesan", ["parmesan cheese", "parmigiano reggiano", "parmesan"]),
        ("cream cheese", ["cream cheese"]),
        ("yogurt", ["greek yoghurt", "greek yogurt", "yoghurt", "yogurt"]),
        ("mayonnaise", ["mayonnaise", "mayo"]),
        ("cheese", ["cheese"]),
        // The store-cupboard half of the same split, for the same reason.
        ("double cream", ["heavy whipping cream", "heavy cream", "double cream"]),
        ("plain flour", ["all purpose flour", "plain flour"]),
        ("caster sugar", ["superfine sugar", "caster sugar"]),
        ("bicarbonate of soda", ["baking soda", "bicarbonate of soda", "bicarb"]),
        ("salt", ["sea salt", "kosher salt", "table salt", "salt"]),
        ("water", ["boiling water", "cold water", "warm water", "water"])
    ];

    /// <summary>
    /// Every alias in the table, most specific first, so <see cref="Normalize"/> can stop at its first hit
    /// and still have found the best one. Built once: the table is a constant and this ordering never
    /// changes, while Normalize runs for every ingredient of every candidate recipe.
    /// </summary>
    private static readonly (string Alias, string Canonical)[] AliasesBySpecificity =
        Concepts
            .SelectMany(concept => concept.Aliases.Select(alias => (Alias: alias, concept.Canonical)))
            .OrderByDescending(entry => entry.Alias.Count(character => character == ' '))
            .ThenByDescending(entry => entry.Alias.Length)
            .ToArray();

    private static readonly HashSet<string> PantryStaples =
        ["salt", "black pepper", "water", "cooking oil", "olive oil"];

    private static readonly string[] ProteinSearchTerms =
    [
        "chicken", "beef", "lamb", "pork", "salmon", "fish", "shrimp", "egg", "turkey",
        "duck", "tofu", "tempeh", "bean", "lentil", "meat", "sausage"
    ];

    private static readonly string[] BaseSearchTerms =
        ["potato", "rice", "pasta", "bread", "noodle", "couscous", "quinoa", "tortilla"];

    private static readonly string[] DairySearchTerms =
        ["cheese", "milk", "yogurt", "butter", "cream"];

    private static readonly string[] LowPrioritySearchTerms =
    [
        "juice", "dressing", "mayonnaise", "mustard", "soy sauce", "hot sauce", "stock",
        "broth", "pickled vegetable", "spice mix", "seasoning", "turmeric powder"
    ];

    private static readonly HashSet<string> KnownConcepts =
        Concepts.Select(concept => concept.Canonical).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> NoiseWords =
    [
        "a", "an", "and", "as", "block", "bottle", "bottles", "bowl", "box", "can", "carton", "container",
        "boneless", "chopped", "diced", "fresh", "freshly", "gram", "grams", "g", "jar", "kg", "large",
        "medium", "minced", "ml", "of", "litre", "litres", "optional", "pack", "package", "packaged",
        "peeled", "piece", "pieces", "pinch", "raw", "roughly", "skinless", "sliced", "small", "tablespoon",
        "tablespoons", "tbsp", "teaspoon", "teaspoons", "tin", "tsp", "to", "taste", "tub", "wheel", "cup", "cups"
    ];

    public string Normalize(string value)
    {
        var cleaned = WhitespaceRegex().Replace(NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), " "), " ").Trim();
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        // The most specific alias wins, not the one that happens to sit highest in the table.
        //
        // This used to return the first concept whose alias appeared anywhere in the line, so a line was
        // resolved by the table's running order. Measured on 25 November 2026, "Taco bread" became "bread",
        // because the bread concept is listed above anything describing a tortilla -- and the planner then
        // searched for "Mexican beef bread pepper recipe" and returned Mexican Beef With Mini Cheese Breads.
        // The single most informative word in the basket had become the least informative one.
        //
        // Ordering the aliases by how much they say -- words first, then length -- makes the table's order
        // irrelevant, which is the actual fix: "taco bread" beats "bread", "chicken stock" beats "chicken",
        // "black pepper" beats "pepper", and nobody has to remember to insert a new concept in the right
        // place. ContainsPhrase already matches whole words only, so "beet" never matches "beetroot".
        foreach (var (alias, canonical) in AliasesBySpecificity)
        {
            if (ContainsPhrase(cleaned, alias))
            {
                return canonical;
            }
        }

        var tokens = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !NoiseWords.Contains(token) && !double.TryParse(token, out _))
            .Select(Singularize)
            .ToList();
        return string.Join(' ', tokens);
    }

    public bool Matches(string pantryIngredient, string requiredIngredient)
    {
        var pantry = Normalize(pantryIngredient);
        var required = Normalize(requiredIngredient);
        if (pantry.Length == 0 || required.Length == 0)
        {
            return false;
        }

        if (pantry.Equals(required, StringComparison.Ordinal))
        {
            return true;
        }

        if (KnownConcepts.Contains(pantry) && KnownConcepts.Contains(required))
        {
            return false;
        }

        return pantry.Length >= 4 && required.Length >= 4 &&
               (ContainsPhrase(pantry, required) || ContainsPhrase(required, pantry));
    }

    public bool IsPantryStaple(string ingredient) => PantryStaples.Contains(Normalize(ingredient));

    /// <summary>
    /// Is this the starch a dish is built on -- rice, pasta, potatoes, bread, noodles, tortillas?
    ///
    /// Used by the search planner, which drops the carrier from a query that already names the dish: tacos
    /// imply tortillas and risotto implies rice, so repeating them only crowds the query out.
    /// </summary>
    public bool IsCarrierBase(string ingredient) => HasAnyPhrase(Normalize(ingredient), BaseSearchTerms);

    /// <summary>
    /// How much an ingredient defines a dish, for weighting how much of a basket a recipe really uses.
    ///
    /// Counting basket items equally treats the tortillas and the onion as the same contribution, and they
    /// are not: the tortillas decide what the dinner IS, and almost every savoury recipe uses an onion. The
    /// weights are the ones the search focus already ranks by -- a staple base highest, then produce and
    /// proteins, then dairy, then condiments and seasonings -- so there is one notion of what matters in a
    /// basket rather than two.
    /// </summary>
    public int DistinctivenessWeight(string ingredient) => RecipeSearchPriority(Normalize(ingredient));

    public IReadOnlyList<string> SelectRecipeSearchFocus(
        IEnumerable<IngredientInput> ingredients,
        int maxCount,
        int variation = 0,
        string? mainIngredient = null)
    {
        var canonical = ingredients
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select((item, index) => new SearchIngredient(Normalize(item.Name), index))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.Index).First())
            .ToList();
        if (canonical.Count == 0)
        {
            return [];
        }

        var useful = canonical
            .Where(item => !IsPantryStaple(item.Name))
            .ToList();
        if (useful.Count == 0)
        {
            useful = canonical;
        }

        var limit = Math.Clamp(maxCount, 1, 12);
        var normalizedMain = Normalize(mainIngredient ?? string.Empty);
        var selectedMain = useful.FirstOrDefault(item =>
            item.Name.Equals(normalizedMain, StringComparison.Ordinal));
        var proteins = useful
            .Where(item => HasAnyPhrase(item.Name, ProteinSearchTerms))
            .OrderBy(item => item.Index)
            .ToList();
        var supporting = useful
            .Where(item => !HasAnyPhrase(item.Name, ProteinSearchTerms))
            .OrderByDescending(item => RecipeSearchPriority(item.Name))
            .ThenBy(item => item.Index)
            .ToList();

        var result = new List<SearchIngredient>(limit);
        if (selectedMain is not null)
        {
            result.Add(selectedMain);
        }

        SearchIngredient? primaryProtein = null;
        if (selectedMain is null && proteins.Count > 0)
        {
            primaryProtein = proteins[Math.Max(0, variation) % proteins.Count];
            result.Add(primaryProtein);
        }

        result.AddRange(supporting
            .Where(item => item != selectedMain)
            .Take(limit - result.Count));
        if (result.Count < limit)
        {
            result.AddRange(proteins
                .Where(item => item != primaryProtein && item != selectedMain)
                .Take(limit - result.Count));
        }

        return result.Select(item => item.Name).ToList();
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static bool HasAnyPhrase(string value, IEnumerable<string> phrases) =>
        phrases.Any(phrase => ContainsPhrase(value, phrase));

    private static int RecipeSearchPriority(string ingredient)
    {
        if (HasAnyPhrase(ingredient, BaseSearchTerms)) return 300;
        if (HasAnyPhrase(ingredient, LowPrioritySearchTerms)) return 20;
        if (HasAnyPhrase(ingredient, DairySearchTerms)) return 120;
        return 200;
    }

    private static string Singularize(string token)
    {
        if (token.Length > 4 && token.EndsWith("oes", StringComparison.Ordinal))
        {
            return token[..^2];
        }

        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal))
        {
            return token[..^3] + "y";
        }

        return token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal)
            ? token[..^1]
            : token;
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record SearchIngredient(string Name, int Index);
}

public sealed record RecipeMatch(
    IReadOnlyList<RecipeIngredient> AvailableIngredients,
    IReadOnlyList<RecipeIngredient> MissingIngredients,
    int RequiredIngredientCount,
    int AvailableIngredientCount,
    int MatchPercentage,
    double Coverage,
    double Availability,
    /// <summary>
    /// How much of the customer's basket this recipe actually uses, uncapped by how short the recipe is.
    ///
    /// Separate from <see cref="Coverage"/>, which divides by the smaller of the basket and the recipe and
    /// so reads 1.0 for any recipe short enough to use everything it could. Measured on 25 November 2026
    /// that scored a one-ingredient beef broth at 100% against 84% for a three-ingredient bake using the
    /// whole basket. This is the number the ranking leads on.
    /// </summary>
    double BasketUtilisation,
    /// <summary>
    /// Whether this is a whole dish built on something the customer has, in [0,1].
    ///
    /// 0.6 of it is using one of the basket's staple bases where the basket has one; 0.4 is the publisher
    /// calling it a main course rather than a sauce or a side, where the customer asked for a meal. Both
    /// halves default to full credit, because neither missing metadata nor a basket without a base says
    /// anything against a recipe. This is what separates beef tacos from taco meat.
    /// </summary>
    double DishSuitability,
    bool MainIngredientPresent,
    string MatchReason);
