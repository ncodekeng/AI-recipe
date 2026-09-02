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
        ("bell pepper", ["red bell peppers", "green bell peppers", "yellow bell peppers", "orange bell peppers", "bell peppers", "red bell pepper", "green bell pepper", "yellow bell pepper", "orange bell pepper", "sweet peppers", "sweet pepper", "red peppers", "green peppers", "yellow peppers", "capsicums", "capsicum", "pepper"]),
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
        ("avocado", ["avocados", "avocado"]),
        ("lemon", ["lemon juice", "lemon zest", "lemons", "lemon"]),
        ("lime", ["lime juice", "lime zest", "limes", "lime"]),
        ("salmon", ["salmon fillets", "salmon fillet", "salmon"]),
        ("shrimp", ["king prawns", "prawns", "prawn", "shrimps", "shrimp"]),
        ("fish", ["white fish fillets", "white fish", "fish fillets", "fish fillet", "haddock", "tilapia", "cod", "tuna", "fish"]),
        ("beef", ["ground beef", "minced beef", "beef mince", "beef steak", "steak", "beef"]),
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
        ("cheese", ["cheese"]),
        ("salt", ["sea salt", "kosher salt", "table salt", "salt"]),
        ("water", ["boiling water", "cold water", "warm water", "water"])
    ];

    private static readonly HashSet<string> PantryStaples =
        ["salt", "black pepper", "water", "cooking oil", "olive oil"];

    private static readonly HashSet<string> KnownConcepts =
        Concepts.Select(concept => concept.Canonical).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> NoiseWords =
    [
        "a", "an", "and", "as", "boneless", "chopped", "diced", "fresh", "freshly", "gram", "grams",
        "g", "kg", "large", "medium", "minced", "ml", "litre", "litres", "optional", "peeled", "piece",
        "pieces", "pinch", "raw", "roughly", "skinless", "sliced", "small", "tablespoon", "tablespoons",
        "tbsp", "teaspoon", "teaspoons", "tsp", "to", "taste", "cup", "cups"
    ];

    public string Normalize(string value)
    {
        var cleaned = WhitespaceRegex().Replace(NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), " "), " ").Trim();
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        foreach (var (canonical, aliases) in Concepts)
        {
            if (aliases.Any(alias => ContainsPhrase(cleaned, alias)))
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

    private static bool ContainsPhrase(string text, string phrase) =>
        $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

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
}

public sealed record RecipeMatch(
    IReadOnlyList<RecipeIngredient> AvailableIngredients,
    IReadOnlyList<RecipeIngredient> MissingIngredients,
    int RequiredIngredientCount,
    int AvailableIngredientCount,
    int MatchPercentage);
