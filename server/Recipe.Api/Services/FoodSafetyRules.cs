namespace Recipe.Api.Services;

internal static class FoodSafetyRules
{
    public static bool IsExcluded(
        string ingredient,
        IReadOnlyList<string> allergens,
        string dietaryPreference)
    {
        var value = ingredient.ToLowerInvariant();
        var excludedTerms = allergens.SelectMany(AllergenTerms).ToList();

        if (dietaryPreference.Equals("Vegan", StringComparison.OrdinalIgnoreCase))
        {
            excludedTerms.AddRange([
                "egg", "milk", "cheese", "cream", "butter", "yogurt",
                "chicken", "beef", "pork", "lamb", "fish", "salmon", "tuna", "shrimp", "prawn"
            ]);
        }
        else if (dietaryPreference.Equals("Vegetarian", StringComparison.OrdinalIgnoreCase))
        {
            excludedTerms.AddRange([
                "chicken", "beef", "pork", "lamb", "fish", "salmon", "tuna", "shrimp", "prawn"
            ]);
        }
        else if (dietaryPreference.Equals("Pescatarian", StringComparison.OrdinalIgnoreCase))
        {
            excludedTerms.AddRange(["chicken", "beef", "pork", "lamb"]);
        }

        if (dietaryPreference.Equals("Gluten-free", StringComparison.OrdinalIgnoreCase))
        {
            excludedTerms.AddRange(AllergenTerms("Wheat"));
        }

        return excludedTerms.Any(value.Contains);
    }

    private static IEnumerable<string> AllergenTerms(string allergen) => allergen.ToLowerInvariant() switch
    {
        "peanuts" => ["peanut"],
        "tree nuts" => ["almond", "walnut", "cashew", "pistachio", "pecan", "hazelnut", "macadamia"],
        "milk" => ["milk", "cheese", "cream", "butter", "yogurt", "yoghurt", "whey", "casein"],
        "eggs" => ["egg", "mayonnaise", "mayo"],
        "wheat" => ["wheat", "flour", "bread", "pasta", "couscous", "semolina"],
        "soy" => ["soy", "tofu", "tempeh", "edamame", "miso"],
        "fish" => ["fish", "salmon", "tuna", "cod", "anchovy"],
        "shellfish" => ["shellfish", "shrimp", "prawn", "crab", "lobster", "scallop"],
        "sesame" => ["sesame", "tahini"],
        _ => [allergen.ToLowerInvariant()]
    };
}
