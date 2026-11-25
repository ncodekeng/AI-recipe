namespace Recipe.Api.Services;

/// <summary>
/// What each cuisine actually cooks, and which ingredients each dish wants.
///
/// It exists because the planner could not name a dish. With a cuisine chosen it varied only across generic
/// forms -- recipe, traybake, stew, salad, soup -- so a basket of beef, taco bread and peppers with Mexican
/// selected was searched for as "Mexican beef bread pepper stew recipe". Measured on 25 November 2026 that
/// returned Mexican Picadillo, Mexican Stuffed Peppers and Mexican Beef With Mini Cheese Breads, and no
/// tacos at all. Naming the dish is what finds the dish: asking instead for "tacos beef pepper recipe"
/// returns tacos, because that is what the publisher wrote the page for.
///
/// This is search-planning guidance and not a list of permitted dishes. Nothing here is matched against a
/// result, nothing is rejected for being absent from it, and a dish nobody listed is returned exactly as
/// readily as one that is -- the queries are a starting point for discovery, and the publishers decide what
/// comes back. A cuisine missing from this table still searches; it falls back to the generic forms.
///
/// SuitsConcepts are IngredientNormalizer canonicals. A dish with none suits any basket of that cuisine and
/// is offered when nothing more specific fits. Keep the names as a cook would say them, including regional
/// terms, because that is what publishers title pages with.
/// </summary>
public static class CuisineDishes
{
    /// <param name="Name">What to search for, as a cook or a publisher would write it.</param>
    /// <param name="SuitsConcepts">
    /// Ingredient concepts that make this dish a good fit. Empty means generally applicable.
    /// </param>
    public sealed record Dish(string Name, params string[] SuitsConcepts);

    public static readonly (string Cuisine, Dish[] Dishes)[] Table =
    [
        ("Italian", [
            new Dish("pasta", "pasta", "tomato"),
            new Dish("risotto", "rice", "mushroom"),
            new Dish("lasagne", "pasta", "beef"),
            new Dish("ragu", "beef", "tomato"),
            new Dish("parmigiana", "aubergine", "mozzarella"),
            new Dish("gnocchi", "potato"),
            new Dish("frittata", "egg"),
            new Dish("minestrone", "bean", "carrot"),
            new Dish("pizza", "mozzarella", "plain flour"),
            new Dish("traybake"),
        ]),
        ("Indian", [
            new Dish("curry"),
            new Dish("biryani", "rice", "chicken"),
            new Dish("dal", "lentil"),
            new Dish("tikka masala", "chicken", "yogurt"),
            new Dish("korma", "chicken", "coconut milk"),
            new Dish("saag", "spinach"),
            new Dish("bhaji", "onion", "aubergine"),
            new Dish("chana masala", "chickpea"),
            new Dish("keema", "lamb", "beef"),
            new Dish("pilau rice", "rice"),
        ]),
        ("Mexican", [
            new Dish("tacos", "tortilla", "beef"),
            new Dish("fajitas", "tortilla", "bell pepper"),
            new Dish("quesadillas", "tortilla", "cheese"),
            new Dish("enchiladas", "tortilla", "tomato"),
            new Dish("burritos", "tortilla", "rice"),
            new Dish("tostadas", "tortilla", "bean"),
            new Dish("nachos", "tortilla", "cheddar"),
            new Dish("chilli", "beef", "bean"),
            new Dish("picadillo", "beef", "potato"),
            new Dish("stuffed peppers", "bell pepper", "rice"),
        ]),
        ("Japanese", [
            new Dish("donburi rice bowl", "rice"),
            new Dish("ramen", "rice noodle", "pasta"),
            new Dish("teriyaki", "chicken", "salmon"),
            new Dish("katsu curry", "chicken", "plain flour"),
            new Dish("yakitori", "chicken"),
            new Dish("gyoza", "pork"),
            new Dish("okonomiyaki", "egg"),
            new Dish("miso soup"),
        ]),
        ("Thai", [
            new Dish("green curry", "coconut milk"),
            new Dish("red curry", "coconut milk"),
            new Dish("pad thai", "rice noodle", "shrimp"),
            new Dish("stir fry", "bell pepper"),
            new Dish("tom yum soup", "shrimp"),
            new Dish("larb", "pork", "lamb"),
            new Dish("massaman curry", "potato", "peanut butter"),
            new Dish("satay", "peanut butter", "chicken"),
        ]),
        ("Chinese", [
            new Dish("stir fry", "bell pepper"),
            new Dish("fried rice", "rice", "egg"),
            new Dish("chow mein", "rice noodle", "pasta"),
            new Dish("sweet and sour", "bell pepper", "pork"),
            new Dish("dumplings", "pork", "plain flour"),
            new Dish("char siu", "pork"),
            new Dish("kung pao", "chicken", "peanut butter"),
            new Dish("hot and sour soup", "mushroom"),
        ]),
        ("French", [
            new Dish("casserole"),
            new Dish("gratin", "potato", "double cream"),
            new Dish("ratatouille", "courgette", "aubergine"),
            new Dish("coq au vin", "chicken", "mushroom"),
            new Dish("quiche", "egg", "cheddar"),
            new Dish("cassoulet", "bean", "pork"),
            new Dish("boeuf bourguignon", "beef", "mushroom"),
            new Dish("tartiflette", "potato", "cheese"),
        ]),
        ("Mediterranean", [
            new Dish("traybake", "bell pepper", "courgette"),
            new Dish("moussaka", "aubergine", "lamb"),
            new Dish("shakshuka", "egg", "tomato"),
            new Dish("tagine", "chickpea", "lamb"),
            new Dish("souvlaki", "pork", "chicken"),
            new Dish("greek salad", "feta", "tomato"),
            new Dish("stuffed peppers", "bell pepper", "rice"),
            new Dish("paella", "rice", "shrimp"),
        ]),
        ("Korean", [
            new Dish("bibimbap", "rice", "egg"),
            new Dish("bulgogi", "beef"),
            new Dish("japchae", "rice noodle", "spinach"),
            new Dish("kimchi stew", "pork"),
            new Dish("tteokbokki"),
            new Dish("dakgalbi", "chicken", "sweet potato"),
            new Dish("korean fried chicken", "chicken"),
        ]),
        ("Caribbean", [
            new Dish("jerk chicken", "chicken"),
            new Dish("curry goat", "lamb"),
            new Dish("rice and peas", "rice", "bean"),
            new Dish("escovitch fish", "fish"),
            new Dish("brown stew chicken", "chicken"),
            new Dish("saltfish fritters", "fish", "plain flour"),
            new Dish("oxtail stew", "beef"),
        ]),
        ("British", [
            new Dish("pie", "beef", "plain flour"),
            new Dish("shepherds pie", "lamb", "potato"),
            new Dish("cottage pie", "beef", "potato"),
            new Dish("roast dinner", "potato", "carrot"),
            new Dish("toad in the hole", "egg", "plain flour"),
            new Dish("fish and chips", "fish", "potato"),
            new Dish("bubble and squeak", "potato"),
            new Dish("stew"),
            new Dish("traybake"),
        ]),
        ("Arabic", [
            new Dish("kebab", "lamb", "beef"),
            new Dish("shawarma", "chicken", "yogurt"),
            new Dish("kofta", "lamb", "beef"),
            new Dish("hummus", "chickpea"),
            new Dish("mujadara", "rice", "lentil"),
            new Dish("fattoush", "tomato", "bread"),
            new Dish("maqluba", "rice", "aubergine"),
            new Dish("shish taouk", "chicken"),
        ]),
        ("Eastern European", [
            new Dish("dumplings", "plain flour", "potato"),
            new Dish("goulash", "beef", "bell pepper"),
            new Dish("borscht", "beetroot"),
            new Dish("schnitzel", "pork", "plain flour"),
            new Dish("pierogi", "potato", "cheese"),
            new Dish("stuffed cabbage rolls", "beef", "rice"),
            new Dish("stroganoff", "beef", "mushroom"),
        ]),
        ("Swedish", [
            new Dish("meatballs", "beef", "pork"),
            new Dish("gravlax", "salmon"),
            new Dish("hash", "potato"),
            new Dish("pea soup", "bean"),
            new Dish("toast skagen", "shrimp", "bread"),
            new Dish("kalops beef stew", "beef", "carrot"),
        ]),
        ("Ghanaian", [
            new Dish("jollof rice", "rice", "tomato"),
            new Dish("groundnut soup", "peanut butter", "chicken"),
            new Dish("waakye", "rice", "bean"),
            new Dish("light soup", "fish", "tomato"),
            new Dish("kelewele"),
            new Dish("red red bean stew", "bean"),
        ]),
    ];

    /// <summary>
    /// The dishes of one cuisine, best fit for this basket first.
    ///
    /// Scored by how many of the dish's ingredients the customer actually has. A dish that declares none
    /// scores below any dish that matches something and above one that matches nothing, so a basket the
    /// table says nothing specific about still gets that cuisine's everyday dishes rather than nothing.
    /// Ties keep table order, which is roughly how typical each dish is.
    /// </summary>
    public static IReadOnlyList<Dish> For(string? cuisine, ISet<string> basketConcepts)
    {
        if (string.IsNullOrWhiteSpace(cuisine))
        {
            return [];
        }

        var dishes = Table
            .FirstOrDefault(entry => entry.Cuisine.Equals(cuisine.Trim(), StringComparison.OrdinalIgnoreCase))
            .Dishes;
        if (dishes is null || dishes.Length == 0)
        {
            return [];
        }

        // With nothing in the basket there is nothing to fit, so the table's own order decides -- which is
        // how typical each dish is of its cuisine. Scoring here would be worse than not scoring: the half
        // point for suiting anything would put the generic dishes first, so Inspire Me would open on an
        // Italian traybake instead of pasta.
        if (basketConcepts.Count == 0)
        {
            return dishes;
        }

        return dishes
            .Select((dish, index) => (dish, index, score: Score(dish, basketConcepts)))
            .OrderByDescending(entry => entry.score)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.dish)
            .ToList();
    }

    /// <summary>Whole points for an ingredient the customer has; a half point for suiting anything.</summary>
    private static double Score(Dish dish, ISet<string> basketConcepts) =>
        dish.SuitsConcepts.Length == 0
            ? 0.5
            : dish.SuitsConcepts.Count(basketConcepts.Contains);

    /// <summary>Every cuisine the table covers, for tests and for the review export.</summary>
    public static IReadOnlyList<string> Cuisines => Table.Select(entry => entry.Cuisine).ToList();

    /// <summary>
    /// Dish names more than one cuisine cooks. Those need their cuisine named in a query; the rest do not,
    /// because the dish already says which kitchen it came from. Derived from the table rather than listed,
    /// so adding a cuisine that also makes dumplings needs no second edit.
    /// </summary>
    private static readonly HashSet<string> SharedDishNames =
        Table
            .SelectMany(entry => entry.Dishes.Select(dish => dish.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsShared(string dishName) => SharedDishNames.Contains(dishName);

    /// <summary>Which cuisine a dish came from. Only meaningful for a name one cuisine alone uses.</summary>
    public static string? CuisineOf(Dish dish) =>
        Table.FirstOrDefault(entry => entry.Dishes.Contains(dish)).Cuisine;

    // What publishers actually write in recipeCuisine, mapped onto the cuisines the app offers. A publisher
    // saying "Tex-Mex" or "Sichuan" is telling us something real, and the app has no picker entry for it.
    //
    // Deliberately lossy and deliberately incomplete. Several of these group traditions that a cook would
    // rightly separate -- Greek and Spanish are not one cuisine, and Nigerian is not Ghanaian -- but the
    // app's own picker already groups them, so this maps onto what it offers rather than inventing entries
    // it has no screen for. A value that matches nothing here is kept as the publisher wrote it.
    private static readonly (string Needle, string Cuisine)[] CuisineAliases =
    [
        ("tex-mex", "Mexican"), ("tex mex", "Mexican"), ("mexican", "Mexican"),
        ("italian", "Italian"), ("sicilian", "Italian"), ("tuscan", "Italian"),
        ("indian", "Indian"), ("punjabi", "Indian"), ("bengali", "Indian"), ("south asian", "Indian"),
        ("japanese", "Japanese"), ("thai", "Thai"),
        ("chinese", "Chinese"), ("cantonese", "Chinese"), ("sichuan", "Chinese"), ("szechuan", "Chinese"),
        ("french", "French"), ("provencal", "French"),
        ("mediterranean", "Mediterranean"), ("greek", "Mediterranean"), ("spanish", "Mediterranean"),
        ("turkish", "Mediterranean"), ("portuguese", "Mediterranean"),
        ("korean", "Korean"),
        ("caribbean", "Caribbean"), ("jamaican", "Caribbean"), ("cuban", "Caribbean"), ("trinidadian", "Caribbean"),
        ("british", "British"), ("english", "British"), ("scottish", "British"), ("welsh", "British"),
        ("irish", "British"),
        ("arabic", "Arabic"), ("lebanese", "Arabic"), ("middle eastern", "Arabic"), ("moroccan", "Arabic"),
        ("persian", "Arabic"), ("israeli", "Arabic"), ("syrian", "Arabic"),
        ("eastern european", "Eastern European"), ("polish", "Eastern European"),
        ("hungarian", "Eastern European"), ("ukrainian", "Eastern European"),
        ("russian", "Eastern European"), ("czech", "Eastern European"), ("georgian", "Eastern European"),
        ("swedish", "Swedish"), ("scandinavian", "Swedish"), ("nordic", "Swedish"), ("danish", "Swedish"),
        ("norwegian", "Swedish"), ("finnish", "Swedish"),
        ("ghanaian", "Ghanaian"), ("west african", "Ghanaian"), ("nigerian", "Ghanaian"),
    ];

    // Cuisines the app has no picker entry for but publishers legitimately name. Kept as written, because
    // "Vietnamese" is a true statement about a recipe even though nothing here maps it.
    private static readonly (string Needle, string Cuisine)[] OtherRealCuisines =
    [
        ("american", "American"), ("vietnamese", "Vietnamese"), ("malaysian", "Malaysian"),
        ("indonesian", "Indonesian"), ("filipino", "Filipino"), ("german", "German"),
        ("austrian", "Austrian"), ("swiss", "Swiss"), ("belgian", "Belgian"), ("dutch", "Dutch"),
        ("australian", "Australian"), ("cajun", "Cajun"), ("creole", "Creole"),
        ("ethiopian", "Ethiopian"), ("peruvian", "Peruvian"), ("brazilian", "Brazilian"),
        ("argentin", "Argentinian"), ("asian", "Asian"), ("african", "African"),
        ("european", "European"), ("latin", "Latin American"),
    ];

    /// <summary>
    /// A publisher's own recipeCuisine, where it actually names a cuisine.
    ///
    /// Returns null for anything that does not, which is a great deal of what the field contains: measured
    /// on 25 November 2026, live pages gave "Cheese Cuisine", "Gluten Free", "Asia" and "Asian cuisines".
    /// Those used to be passed through to the customer as the recipe's cuisine, which is worse than saying
    /// nothing -- "Gluten Free" as a cuisine is actively misleading. Null sends the caller to the dish's own
    /// identity instead, and then to admitting it is not stated.
    /// </summary>
    public static string? Canonicalise(string? publisherCuisine)
    {
        var value = publisherCuisine?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var lowered = value.ToLowerInvariant();
        foreach (var (needle, cuisine) in CuisineAliases.Concat(OtherRealCuisines))
        {
            if (lowered.Contains(needle, StringComparison.Ordinal))
            {
                return cuisine;
            }
        }

        return null;
    }

    /// <summary>
    /// The cuisine a title gives away by naming one of its dishes, or null when it does not.
    ///
    /// Only dishes one cuisine alone cooks can identify it: a page called "Chicken Stir Fry" could be Thai
    /// or Chinese, so it says nothing. The longest match wins, so "green curry" is read before "curry".
    /// </summary>
    public static string? FromDishName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var padded = $" {new string(title.ToLowerInvariant().Select(c => char.IsLetter(c) ? c : ' ').ToArray())} ";

        return Table
            .SelectMany(entry => entry.Dishes.Select(dish => (entry.Cuisine, dish.Name)))
            .Where(entry => !IsShared(entry.Name))
            .Where(entry => padded.Contains($" {entry.Name} ", StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Name.Length)
            .Select(entry => entry.Cuisine)
            .FirstOrDefault();
    }
}
