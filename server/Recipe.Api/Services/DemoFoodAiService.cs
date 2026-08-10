using Recipe.Api.Models;

namespace Recipe.Api.Services;

public sealed class DemoFoodAiService
{
    private static readonly (string Token, string Name, string Quantity)[] KnownIngredients =
    [
        ("tomato", "Tomatoes", "4 medium"),
        ("carrot", "Carrots", "3"),
        ("egg", "Eggs", "6"),
        ("spinach", "Baby spinach", "1 bag"),
        ("cheese", "Cheddar cheese", "200 g"),
        ("onion", "Red onion", "1"),
        ("garlic", "Garlic", "1 bulb"),
        ("pepper", "Bell peppers", "2"),
        ("potato", "Potatoes", "5"),
        ("mushroom", "Mushrooms", "250 g"),
        ("avocado", "Avocado", "2"),
        ("lemon", "Lemon", "1"),
        ("broccoli", "Broccoli", "1 head"),
        ("chicken", "Chicken breast", "2 pieces"),
        ("salmon", "Salmon fillets", "2"),
        ("rice", "Cooked rice", "2 cups"),
        ("pasta", "Pasta", "300 g")
    ];

    private static readonly (string Name, string Quantity)[] DemoSelection =
    [
        ("Tomatoes", "4 medium"),
        ("Eggs", "6"),
        ("Baby spinach", "1 bag"),
        ("Cheddar cheese", "200 g"),
        ("Red onion", "1"),
        ("Garlic", "1 bulb"),
        ("Bell peppers", "2"),
        ("Mushrooms", "250 g")
    ];

    public IngredientAnalysisResponse AnalyzeIngredients(IReadOnlyList<UploadedPhoto> photos)
    {
        var ingredients = new List<DetectedIngredient>();

        foreach (var photo in photos)
        {
            var normalizedName = photo.FileName.ToLowerInvariant();
            foreach (var match in KnownIngredients.Where(item => normalizedName.Contains(item.Token)))
            {
                AddIfMissing(ingredients, match.Name, match.Quantity, 96, photo.FileName);
            }
        }

        var targetCount = Math.Min(8, 4 + Math.Max(0, photos.Count - 1) * 2);
        for (var index = 0; ingredients.Count < targetCount && index < DemoSelection.Length; index++)
        {
            var suggestion = DemoSelection[index];
            var source = photos[index % photos.Count].FileName;
            AddIfMissing(ingredients, suggestion.Name, suggestion.Quantity, 93 - (index * 3), source);
        }

        return new IngredientAnalysisResponse(
            ingredients,
            "Demo",
            "Demo recognition is active. Add Azure settings to analyze the actual pixels.");
    }

    public RecipeGenerationResponse GenerateRecipes(GenerateRecipesRequest request)
    {
        var allergens = request.Allergens
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usable = request.Ingredients
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => !FoodSafetyRules.IsExcluded(item.Name, allergens, request.DietaryPreference))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with
            {
                Name = group.First().Name.Trim(),
                Quantity = string.IsNullOrWhiteSpace(group.First().Quantity) ? "as needed" : group.First().Quantity.Trim()
            })
            .Take(8)
            .ToList();

        if (usable.Count == 0)
        {
            usable.Add(new IngredientInput("seasonal vegetables", "about 500 g"));
        }

        var primary = CleanTitleIngredient(usable[0].Name);
        var secondary = CleanTitleIngredient(usable.ElementAtOrDefault(1)?.Name ?? "herbs");
        var maxMinutes = Math.Max(10, request.MaxCookingMinutes);
        var recipes = new List<RecipeSuggestion>
        {
            CreateSkilletRecipe(primary, secondary, usable, request.Servings, Math.Min(25, maxMinutes), allergens, request.DietaryPreference),
            CreateTrayRecipe(primary, usable, request.Servings, Math.Min(35, maxMinutes), allergens, request.DietaryPreference),
            CreateBowlRecipe(primary, secondary, usable, request.Servings, Math.Min(20, maxMinutes), allergens, request.DietaryPreference)
        };

        return new RecipeGenerationResponse(
            recipes,
            "Demo",
            "Always check every ingredient label and adapt the recipe with a qualified professional for severe allergies.",
            "These polished sample recipes are generated locally for the prototype. Connect Azure OpenAI for personalized results.");
    }

    private static RecipeSuggestion CreateSkilletRecipe(
        string primary,
        string secondary,
        IReadOnlyList<IngredientInput> available,
        int servings,
        int minutes,
        IReadOnlyList<string> allergens,
        string preference)
    {
        var recipeIngredients = BuildIngredientList(available, 5);
        recipeIngredients.Add(new RecipeIngredient("1 tbsp", "olive oil"));
        recipeIngredients.Add(new RecipeIngredient("to taste", "salt and black pepper"));

        return new RecipeSuggestion(
            Guid.NewGuid(),
            $"Golden {primary} & {secondary} skillet",
            "A quick, comforting one-pan supper with crisp edges, tender vegetables and a bright finish.",
            minutes,
            "Easy",
            "Modern European",
            servings,
            94,
            BuildTags(preference, allergens, "One pan"),
            recipeIngredients,
            [
                "Prep the ingredients into evenly sized pieces so everything cooks at the same pace.",
                "Warm the olive oil in a wide pan over medium-high heat, then cook the firmer ingredients until lightly golden.",
                "Fold in the remaining ingredients and cook until just tender, adding a splash of water if the pan looks dry.",
                "Season carefully, taste, and serve straight from the pan while the edges are still crisp."
            ],
            "coral");
    }

    private static RecipeSuggestion CreateTrayRecipe(
        string primary,
        IReadOnlyList<IngredientInput> available,
        int servings,
        int minutes,
        IReadOnlyList<string> allergens,
        string preference)
    {
        var recipeIngredients = BuildIngredientList(available.Reverse().ToList(), 5);
        recipeIngredients.Add(new RecipeIngredient("2 tbsp", "olive oil"));
        recipeIngredients.Add(new RecipeIngredient("1 tsp", "smoked paprika"));

        return new RecipeSuggestion(
            Guid.NewGuid(),
            $"Roasted {primary} kitchen tray",
            "Deeply roasted flavours and almost no washing up—ideal when you want dinner to take care of itself.",
            minutes,
            "Easy",
            "Mediterranean",
            servings,
            88,
            BuildTags(preference, allergens, "Low effort"),
            recipeIngredients,
            [
                "Heat the oven to 220°C / 425°F and line a large baking tray.",
                "Toss the ingredients with olive oil, paprika, salt and pepper, keeping the tray in a single layer.",
                "Roast until caramelised and cooked through, turning the ingredients halfway through.",
                "Rest for two minutes, then finish with any fresh herbs or citrus you have."
            ],
            "saffron");
    }

    private static RecipeSuggestion CreateBowlRecipe(
        string primary,
        string secondary,
        IReadOnlyList<IngredientInput> available,
        int servings,
        int minutes,
        IReadOnlyList<string> allergens,
        string preference)
    {
        var recipeIngredients = BuildIngredientList(available, 4);
        recipeIngredients.Add(new RecipeIngredient("1 cup", "cooked rice or quinoa"));
        recipeIngredients.Add(new RecipeIngredient("1 tbsp", "lemon juice"));

        return new RecipeSuggestion(
            Guid.NewGuid(),
            $"Bright {primary} & {secondary} bowl",
            "Fresh, colourful and flexible, with warm ingredients over grains and a simple lemon dressing.",
            minutes,
            "Easy",
            "Californian",
            servings,
            82,
            BuildTags(preference, allergens, "Fresh"),
            recipeIngredients,
            [
                "Prepare the rice or quinoa and divide it between warm serving bowls.",
                "Slice the ingredients into bite-sized pieces; quickly sauté anything that should not be eaten raw.",
                "Arrange everything over the grains, keeping contrasting colours in separate sections.",
                "Whisk lemon juice with olive oil, salt and pepper, then spoon it over the bowls just before serving."
            ],
            "sage");
    }

    private static List<RecipeIngredient> BuildIngredientList(IEnumerable<IngredientInput> available, int count) =>
        available.Take(count)
            .Select(item => new RecipeIngredient(item.Quantity, item.Name))
            .ToList();

    private static IReadOnlyList<string> BuildTags(string preference, IReadOnlyList<string> allergens, string feature)
    {
        var tags = new List<string> { feature };
        if (!string.IsNullOrWhiteSpace(preference) && !preference.Equals("Anything", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(preference);
        }

        if (allergens.Count > 0)
        {
            tags.Add("Allergen-aware");
        }

        return tags;
    }

    private static string CleanTitleIngredient(string value)
    {
        var result = value.Trim();
        return result.Length == 0
            ? "market vegetable"
            : char.ToUpperInvariant(result[0]) + result[1..].ToLowerInvariant();
    }

    private static void AddIfMissing(
        ICollection<DetectedIngredient> ingredients,
        string name,
        string quantity,
        int confidence,
        string source)
    {
        if (ingredients.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ingredients.Add(new DetectedIngredient(Guid.NewGuid(), name, quantity, confidence, source));
    }
}
