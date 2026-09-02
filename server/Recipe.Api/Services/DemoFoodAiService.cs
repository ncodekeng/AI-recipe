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
