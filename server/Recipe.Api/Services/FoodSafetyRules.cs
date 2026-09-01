using System.Text.RegularExpressions;

namespace Recipe.Api.Services;

internal static partial class FoodSafetyRules
{
    private static readonly IReadOnlyDictionary<string, string[]> AllergenTerms =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Peanuts"] = ["peanut", "peanuts", "groundnut", "groundnuts", "satay sauce"],
            ["Tree nuts"] =
            [
                "almond", "almonds", "walnut", "walnuts", "cashew", "cashews", "pistachio",
                "pistachios", "pecan", "pecans", "hazelnut", "hazelnuts", "macadamia",
                "macadamias", "brazil nut", "brazil nuts", "marzipan", "praline", "pesto"
            ],
            ["Milk"] =
            [
                "milk", "cheese", "cream", "butter", "ghee", "yogurt", "yoghurt", "whey",
                "casein", "caseinate", "lactose", "buttermilk", "creme fraiche", "custard"
            ],
            ["Eggs"] =
            [
                "egg", "eggs", "albumen", "mayonnaise", "mayo", "aioli", "meringue",
                "hollandaise"
            ],
            ["Gluten cereals"] =
            [
                "wheat", "flour", "bread", "breadcrumbs", "pasta", "couscous", "semolina",
                "barley", "rye", "spelt", "seitan", "bulgur", "soy sauce"
            ],
            ["Soy"] = ["soy", "soya", "tofu", "tempeh", "edamame", "miso", "soy sauce"],
            ["Fish"] =
            [
                "fish", "salmon", "tuna", "cod", "anchovy", "anchovies", "sardine", "sardines",
                "fish sauce", "worcestershire sauce"
            ],
            ["Crustaceans"] = ["shrimp", "shrimps", "prawn", "prawns", "crab", "crabs", "lobster", "lobsters", "crayfish"],
            ["Molluscs"] = ["mussel", "mussels", "oyster", "oysters", "scallop", "scallops", "squid", "octopus", "clam", "clams"],
            ["Sesame"] = ["sesame", "tahini"],
            ["Celery"] = ["celery", "celeriac"],
            ["Mustard"] = ["mustard"],
            ["Lupin"] = ["lupin", "lupine"],
            ["Sulphites"] = ["sulphite", "sulphites", "sulfite", "sulfites", "sulphur dioxide", "sulfur dioxide"]
        };

    private static readonly string[] MeatTerms =
    [
        "chicken", "beef", "pork", "lamb", "turkey", "duck", "bacon", "ham", "prosciutto",
        "pancetta", "sausage", "sausages", "gelatin", "gelatine", "lard"
    ];

    private static readonly string[] FishAndSeafoodTerms =
        AllergenTerms["Fish"]
            .Concat(AllergenTerms["Crustaceans"])
            .Concat(AllergenTerms["Molluscs"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly string[] AnimalProductTerms =
        MeatTerms
            .Concat(FishAndSeafoodTerms)
            .Concat(AllergenTerms["Milk"])
            .Concat(AllergenTerms["Eggs"])
            .Concat(["honey"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly string[] HalalStyleExclusions =
    [
        "pork", "bacon", "ham", "prosciutto", "pancetta", "lard", "gelatin", "gelatine",
        "alcohol", "wine", "beer", "lager", "ale", "cider", "brandy", "rum", "liqueur",
        "mirin", "sake", "cooking wine"
    ];

    public static bool IsExcluded(
        string ingredient,
        IReadOnlyList<string> allergens,
        string dietaryPreference) =>
        FindConflicts([ingredient], allergens, dietaryPreference, []).Count > 0;

    public static IReadOnlyList<string> FindConflicts(
        IEnumerable<string> recipeText,
        IReadOnlyList<string> allergens,
        string dietaryPreference,
        IReadOnlyList<string> avoidIngredients)
    {
        var normalizedText = recipeText
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .ToList();
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var allergen in allergens.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var canonical = CanonicalAllergen(allergen);
            var terms = AllergenTerms.TryGetValue(canonical, out var mapped)
                ? mapped
                : [allergen.Trim()];
            AddMatches(normalizedText, terms, canonical, conflicts);
        }

        var preference = dietaryPreference.Trim();
        if (preference.Equals("Vegan", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(normalizedText, AnimalProductTerms, "Vegan", conflicts);
        }
        else if (preference.Equals("Vegetarian", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(normalizedText, MeatTerms.Concat(FishAndSeafoodTerms), "Vegetarian", conflicts);
        }
        else if (preference.Equals("Pescatarian", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(normalizedText, MeatTerms, "Pescatarian", conflicts);
        }
        else if (preference.Equals("Gluten-free", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(normalizedText, AllergenTerms["Gluten cereals"], "Gluten-free", conflicts);
        }
        else if (preference.Equals("Dairy-free", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(normalizedText, AllergenTerms["Milk"], "Dairy-free", conflicts);
        }
        else if (preference.Equals("Halal-style", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(normalizedText, HalalStyleExclusions, "Halal-style", conflicts);
        }
        else if (preference.Equals("Kosher-style", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(
                normalizedText,
                HalalStyleExclusions.Take(7).Concat(FishAndSeafoodTerms),
                "Kosher-style",
                conflicts);

            var containsMeat = normalizedText.Any(text => MeatTerms.Any(term => ContainsPhrase(text, term)));
            var containsDairy = normalizedText.Any(text => AllergenTerms["Milk"].Any(term => ContainsPhrase(text, term)));
            if (containsMeat && containsDairy)
            {
                conflicts.Add("Kosher-style: meat and dairy combination");
            }
        }

        foreach (var avoided in avoidIngredients.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddMatches(normalizedText, [avoided.Trim()], "Avoid", conflicts);
        }

        return conflicts.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddMatches(
        IReadOnlyList<string> normalizedText,
        IEnumerable<string> terms,
        string restriction,
        ISet<string> conflicts)
    {
        foreach (var term in terms)
        {
            if (normalizedText.Any(text => ContainsPhrase(text, term)))
            {
                conflicts.Add($"{restriction}: {term}");
            }
        }
    }

    private static string CanonicalAllergen(string value) => value.Trim().ToLowerInvariant() switch
    {
        "tree nut" or "tree nuts" => "Tree nuts",
        "milk" or "dairy" => "Milk",
        "egg" or "eggs" => "Eggs",
        "wheat" or "gluten" or "gluten cereals" => "Gluten cereals",
        "peanut" or "peanuts" => "Peanuts",
        "soy" or "soya" => "Soy",
        "fish" => "Fish",
        "shellfish" or "crustacean" or "crustaceans" => "Crustaceans",
        "mollusc" or "molluscs" => "Molluscs",
        "sesame" => "Sesame",
        "celery" => "Celery",
        "mustard" => "Mustard",
        "lupin" or "lupine" => "Lupin",
        "sulphites" or "sulfites" => "Sulphites",
        _ => value.Trim()
    };

    private static bool ContainsPhrase(string normalizedText, string phrase)
    {
        var normalizedPhrase = Normalize(phrase);
        return normalizedPhrase.Length > 0 &&
               $" {normalizedText} ".Contains($" {normalizedPhrase} ", StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        WhitespaceRegex().Replace(NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
