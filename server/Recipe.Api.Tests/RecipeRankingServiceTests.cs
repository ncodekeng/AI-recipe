using System.Text.Json;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class RecipeRankingServiceTests
{
    private readonly IngredientNormalizer _normalizer = new();

    [Theory]
    [InlineData("chicken", "chicken breast")]
    [InlineData("pepper", "red bell peppers")]
    [InlineData("onion", "yellow onions")]
    public void Normalizer_matches_related_ingredient_names(string pantry, string required)
    {
        Assert.True(_normalizer.Matches(pantry, required));
    }

    [Theory]
    [InlineData("yogurt tub", "Greek yoghurt")]
    [InlineData("mayonnaise jar", "mayo")]
    [InlineData("cream cheese box", "cream cheese")]
    [InlineData("bottle of soy sauce", "soy sauce")]
    [InlineData("butter block", "unsalted butter")]
    [InlineData("bottle of milk", "whole milk")]
    public void Normalizer_matches_scanned_container_names(string pantry, string required)
    {
        Assert.True(_normalizer.Matches(pantry, required));
    }

    [Fact]
    public void Reported_kitchen_memory_supports_a_simple_complete_recipe()
    {
        var service = new RecipeRankingService(_normalizer);
        var pantry = new[]
        {
            new IngredientInput("brown eggs", "8 eggs"),
            new IngredientInput("butter block", "1 block"),
            new IngredientInput("bottle of milk", "1 bottle"),
            new IngredientInput("sliced bread", "5 slices")
        };

        var match = service.CalculateMatch(
            pantry,
            [
                new RecipeIngredient("4", "eggs"),
                new RecipeIngredient("1 tbsp", "unsalted butter"),
                new RecipeIngredient("2 tbsp", "whole milk"),
                new RecipeIngredient("to taste", "salt")
            ]);

        Assert.Equal(100, match.MatchPercentage);
        Assert.Empty(match.MissingIngredients);
    }

    [Fact]
    public void Normalizer_does_not_treat_meat_as_stock()
    {
        Assert.False(_normalizer.Matches("chicken", "chicken stock"));
    }

    [Fact]
    public void Search_focus_uses_a_main_food_and_compatible_substantial_ingredients()
    {
        var ingredients = new[]
        {
            new IngredientInput("mustard jar", "1 jar"),
            new IngredientInput("bottle of juice", "2 bottles"),
            new IngredientInput("red bell pepper", "1"),
            new IngredientInput("onion", "1"),
            new IngredientInput("tomatoes", "2"),
            new IngredientInput("potatoes", "3"),
            new IngredientInput("spinach", "1 bunch"),
            new IngredientInput("raw chicken breast", "1 piece"),
            new IngredientInput("brown eggs", "8"),
            new IngredientInput("packaged sliced bread", "1 pack"),
            new IngredientInput("bottle of milk", "1 bottle")
        };

        Assert.Equal(
            ["chicken", "potato", "bread", "bell pepper", "onion", "tomato"],
            _normalizer.SelectRecipeSearchFocus(ingredients, 6));
        Assert.Equal(
            "egg",
            _normalizer.SelectRecipeSearchFocus(ingredients, 6, variation: 1)[0]);
        Assert.Equal(
            "potato",
            _normalizer.SelectRecipeSearchFocus(
                ingredients,
                6,
                mainIngredient: "potatoes")[0]);
    }

    [Fact]
    public void Match_finds_missing_ingredients_and_ignores_pantry_basics()
    {
        var service = new RecipeRankingService(_normalizer);
        var match = service.CalculateMatch(
            [new IngredientInput("Chicken", "2 pieces")],
            [
                new RecipeIngredient("2", "Chicken breast", 2, "piece"),
                new RecipeIngredient("2 cloves", "Garlic", 2, "clove"),
                new RecipeIngredient("to taste", "Sea salt"),
                new RecipeIngredient("1 tbsp", "Extra virgin olive oil"),
                new RecipeIngredient("100 ml", "Water")
            ]);

        Assert.Equal(2, match.RequiredIngredientCount);
        Assert.Equal(1, match.AvailableIngredientCount);
        Assert.Equal(70, match.MatchPercentage);
        Assert.Equal(1d, match.Coverage);
        Assert.Equal(0.5d, match.Availability);
        var missing = Assert.Single(match.MissingIngredients);
        Assert.Equal("Garlic", missing.Name);
        Assert.Equal(2, missing.Quantity);
        Assert.Equal("clove", missing.Unit);
    }

    [Fact]
    public void Ranking_prefers_a_complete_match_when_available()
    {
        var service = new RecipeRankingService(_normalizer);
        var pantry = new[]
        {
            new IngredientInput("chicken", "2"),
            new IngredientInput("pepper", "2"),
            new IngredientInput("onion", "1")
        };
        var recipes = new[]
        {
            Recipe("Chicken stuffed peppers", "chicken breast", "red bell pepper", "onion", "feta cheese"),
            Recipe("Chicken and onion", "chicken", "onion")
        };

        var ranked = service.Rank(recipes, pantry);

        Assert.Equal("Chicken and onion", ranked[0].Title);
        Assert.Equal(100, ranked[0].IngredientMatch);
        Assert.Empty(ranked[0].MissingIngredients!);
        Assert.Equal("feta cheese", Assert.Single(ranked[1].MissingIngredients!).Name);
    }

    [Fact]
    public void Top_pick_is_a_complete_match_when_available()
    {
        var service = new RecipeRankingService(_normalizer);
        var exact = Recipe("Provider-favourite omelette", "egg");
        var oneMissing = Recipe("Spinach omelette", "egg", "spinach");
        var twoMissing = Recipe("Feta spinach omelette", "egg", "spinach", "feta");

        var ranked = service.Rank(
            [exact, twoMissing, oneMissing],
            [new IngredientInput("egg", "4")]);

        Assert.Equal(exact.Id, ranked[0].Id);
        Assert.Equal(100, ranked[0].IngredientMatch);
        Assert.Empty(ranked[0].MissingIngredients!);
    }

    [Fact]
    public void Ranking_places_a_traditional_near_match_before_a_complete_match()
    {
        var service = new RecipeRankingService(_normalizer);
        var traditional = Recipe("Traditional chicken pie", "chicken", "onion", "pastry") with
        {
            Tags = ["Traditional"]
        };
        var complete = Recipe("Chicken and onion", "chicken", "onion");

        var ranked = service.Rank(
            [complete, traditional],
            [new IngredientInput("chicken", "2"), new IngredientInput("onion", "1")]);

        Assert.Equal(traditional.Id, ranked[0].Id);
        Assert.Single(ranked[0].MissingIngredients!);
        Assert.Equal(complete.Id, ranked[1].Id);
        Assert.Empty(ranked[1].MissingIngredients!);
    }

    [Fact]
    public void Ranking_does_not_promote_a_traditional_recipe_missing_more_than_three_items()
    {
        var service = new RecipeRankingService(_normalizer);
        var distantTraditional = Recipe(
            "Traditional feast",
            "chicken",
            "carrot",
            "celery",
            "leek",
            "potato") with
        {
            Tags = ["Traditional"]
        };
        var complete = Recipe("Roast chicken", "chicken");

        var ranked = service.Rank(
            [distantTraditional, complete],
            [new IngredientInput("chicken", "2")]);

        Assert.Equal(complete.Id, ranked[0].Id);
        Assert.Empty(ranked[0].MissingIngredients!);
        Assert.Equal(4, ranked[1].MissingIngredients!.Count);
    }

    [Fact]
    public void Top_pick_uses_fewest_missing_items_when_no_complete_match_exists()
    {
        var service = new RecipeRankingService(_normalizer);
        var oneMissing = Recipe("Spinach omelette", "egg", "spinach");
        var twoMissing = Recipe("Feta spinach omelette", "egg", "spinach", "feta");

        var ranked = service.Rank(
            [twoMissing, oneMissing],
            [new IngredientInput("egg", "4")]);

        Assert.Equal(oneMissing.Id, ranked[0].Id);
        Assert.Single(ranked[0].MissingIngredients!);
    }

    [Fact]
    public void Top_pick_diversifies_between_equally_close_matches()
    {
        var service = new RecipeRankingService(_normalizer);
        var seen = Recipe("Spinach omelette", "egg", "spinach");
        var fresh = Recipe("Mushroom omelette", "egg", "mushroom");

        var ranked = service.Rank(
            [seen, fresh],
            [new IngredientInput("egg", "4")],
            [seen.Id]);

        Assert.Equal(fresh.Id, ranked[0].Id);
    }

    [Fact]
    public void Cook_with_what_I_have_returns_only_zero_missing_matches()
    {
        var service = new RecipeRankingService(_normalizer);
        var complete = Recipe("Chicken and onion", "chicken", "onion");
        var nearMatch = Recipe("Chicken and spinach", "chicken", "spinach");

        var ranked = service.Rank(
            [nearMatch, complete],
            [new IngredientInput("chicken", "2"), new IngredientInput("onion", "1")],
            onlyUseAvailableIngredients: true);

        Assert.Equal(complete.Id, Assert.Single(ranked).Id);
        Assert.Empty(ranked[0].MissingIngredients!);
        Assert.Equal(100, ranked[0].IngredientMatch);
    }

    [Fact]
    public void Show_all_recipes_keeps_complete_and_near_matches()
    {
        var service = new RecipeRankingService(_normalizer);
        var complete = Recipe("Chicken and onion", "chicken", "onion");
        var nearMatch = Recipe("Chicken and spinach", "chicken", "spinach");

        var ranked = service.Rank(
            [nearMatch, complete],
            [new IngredientInput("chicken", "2"), new IngredientInput("onion", "1")]);

        Assert.Equal(2, ranked.Count);
        Assert.Contains(ranked, recipe => recipe.Id == complete.Id && recipe.MissingIngredients!.Count == 0);
        Assert.Contains(ranked, recipe => recipe.Id == nearMatch.Id && recipe.MissingIngredients!.Count == 1);
    }

    [Fact]
    public void Ranking_rejects_every_recipe_without_the_selected_main_ingredient()
    {
        var service = new RecipeRankingService(_normalizer);
        var chicken = Recipe("Chicken and onion", "chicken", "onion");
        var potato = Recipe("Potato and onion", "potato", "onion");

        var ranked = service.Rank(
            [potato, chicken],
            [
                new IngredientInput("chicken breast", "300 g"),
                new IngredientInput("potato", "3"),
                new IngredientInput("onion", "1")
            ],
            mainIngredient: "chicken breast");

        Assert.Equal(chicken.Id, Assert.Single(ranked).Id);
        Assert.Contains("100% match", ranked[0].MatchReason);
    }

    [Fact]
    public void Recipe_serializes_real_image_and_match_fields()
    {
        var service = new RecipeRankingService(_normalizer);
        var ranked = service.Rank(
            [Recipe("Salmon supper", "salmon", "lemon") with
            {
                ImageUrl = "https://upload.wikimedia.org/salmon.jpg",
                ImageSourceUrl = "https://commons.wikimedia.org/wiki/File:Salmon.jpg",
                ImageLicenseType = "CC BY 4.0",
                ImageLicenseUrl = "https://creativecommons.org/licenses/by/4.0/",
                ImageAttributionRequirements = "Credit the photographer and link the source and license.",
                ImageRightsStatus = RecipeImageRightsStatuses.VerifiedCommercial,
                ImageProvider = "Wikimedia Commons",
                ImageCreator = "Example Photographer",
                ImageCommercialUseAllowed = true,
                ImageAttributionRequired = true,
                ImageVerified = true
            }],
            [new IngredientInput("salmon fillets", "2")]);

        var json = JsonSerializer.Serialize(ranked[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"imageUrl\":\"https://upload.wikimedia.org/salmon.jpg\"", json);
        Assert.Contains("\"imageSourceUrl\":\"https://commons.wikimedia.org/wiki/File:Salmon.jpg\"", json);
        Assert.Contains("\"imageLicenseType\":\"CC BY 4.0\"", json);
        Assert.Contains("\"imageLicenseUrl\":\"https://creativecommons.org/licenses/by/4.0/\"", json);
        Assert.Contains("\"imageAttributionRequirements\"", json);
        Assert.Contains("\"imageRightsStatus\":\"VerifiedCommercial\"", json);
        Assert.Contains("\"availableIngredients\"", json);
        Assert.Contains("\"missingIngredients\"", json);
        Assert.Contains("\"ingredientMatch\":70", json);
        Assert.Contains("\"matchReason\":\"70% match", json);
        Assert.Contains("\"imageProvider\":\"Wikimedia Commons\"", json);
        Assert.Contains("\"imageVerified\":true", json);
    }

    [Fact]
    public void Keeps_a_recipe_whose_main_ingredient_survives_only_in_the_publishers_own_line()
    {
        // Measured on 25 November 2026: "Easy chicken casserole" was dropped from a chicken search because
        // its chicken line reduced to the name "bone-in", and the customer got three recipes instead of
        // four. The ingredient name is ours and derived; the line is the publisher's and says chicken.
        var casserole = new RecipeSuggestion(
            Guid.NewGuid(),
            "Easy chicken casserole",
            "A provider recipe.",
            90,
            "Easy",
            "British",
            4,
            0,
            ["Dinner"],
            [
                new RecipeIngredient("1.5kg", "bone-in", null, null, "1.5kg chicken thighs, bone-in"),
                new RecipeIngredient("1", "onion", null, null, "1 onion, chopped"),
            ],
            [],
            "coral",
            SourceUrl: "https://publisher.example.test/casserole");

        var ranked = new RecipeRankingService(new IngredientNormalizer()).Rank(
            [casserole],
            [new IngredientInput("chicken thighs", "6")],
            mainIngredient: "chicken");

        Assert.Single(ranked);
    }

    [Fact]
    public void Keeps_a_recipe_whose_ingredient_name_reduced_to_a_noise_word()
    {
        // Measured on 25 November 2026: "Creamy Tuscan Chicken" was dropped from a chicken search because
        // its chicken line extracted the name "boneless". That is a noise word, so it normalized to the
        // empty string and the whole ingredient was discarded before the main-ingredient test ran -- a
        // different fault from a name that merely normalizes to the wrong thing.
        var tuscan = new RecipeSuggestion(
            Guid.NewGuid(),
            "Creamy Tuscan Chicken",
            "A provider recipe.",
            35,
            "Easy",
            "Italian",
            4,
            0,
            ["Dinner"],
            [
                new RecipeIngredient("4", "boneless", null, null, "4 boneless, skinless chicken breasts"),
                new RecipeIngredient("1/2 c.", "cherry tomatoes", null, null, "1/2 c. cherry tomatoes"),
            ],
            [],
            "coral",
            SourceUrl: "https://publisher.example.test/tuscan");

        var ranked = new RecipeRankingService(new IngredientNormalizer()).Rank(
            [tuscan],
            [new IngredientInput("chicken thighs", "6")],
            mainIngredient: "chicken");

        var recipe = Assert.Single(ranked);
        // And the chicken counts as something the customer has, not something they are missing.
        Assert.Contains(
            recipe.AvailableIngredients ?? [],
            ingredient => (ingredient.OriginalText ?? string.Empty).Contains("chicken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Still_drops_a_recipe_that_does_not_contain_the_main_ingredient_at_all()
    {
        // The guard on the rule above: reading the publisher's line must widen what counts as present,
        // not stop the check working. A brisket search returning a taco casserole is the worse result.
        var ranked = new RecipeRankingService(new IngredientNormalizer()).Rank(
            [
                new RecipeSuggestion(
                    Guid.NewGuid(), "Three bean chilli", "A provider recipe.", 40, "Easy", "Mexican", 4, 0,
                    ["Dinner"],
                    [
                        new RecipeIngredient("2 tins", "mixed beans", null, null, "2 tins mixed beans, drained"),
                        new RecipeIngredient("1", "onion", null, null, "1 onion, chopped"),
                    ],
                    [],
                    "coral",
                    SourceUrl: "https://publisher.example.test/chilli"),
            ],
            [new IngredientInput("beef brisket", "1 kg")],
            mainIngredient: "beef brisket");

        Assert.Empty(ranked);
    }

    private static RecipeSuggestion Recipe(string title, params string[] ingredients) => new(
        Guid.NewGuid(),
        title,
        "A provider recipe.",
        30,
        "Easy",
        "European",
        2,
        0,
        ["Dinner"],
        ingredients.Select(name => new RecipeIngredient("as needed", name)).ToList(),
        [],
        "coral",
        SourceName: "Publisher",
        SourceUrl: "https://publisher.example.test/recipe");
}
