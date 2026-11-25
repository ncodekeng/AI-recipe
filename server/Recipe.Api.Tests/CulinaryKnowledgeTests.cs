using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// The expanded culinary knowledge base, and the ingredient recognition that goes with it.
///
/// A benchmark on 25 November 2026 showed a model planning the search beating this table on exactly the two
/// cuisines it covered worst -- Eastern European and Swedish -- by asking about vinegret, botwinka and
/// pyttipanna. The model was then asked once, offline, for the dishes each cuisine was missing; the answers
/// were reviewed and the good ones added. These tests hold the result.
///
/// The second half is ingredient recognition. A basket of nopales, plantain and gochujang used to produce
/// "miso soup nopale plantain gochujang recipe": a non-word, four kitchens, and no page on earth.
/// </summary>
public sealed class CulinaryKnowledgeTests
{
    private readonly IngredientNormalizer _normalizer = new();

    [Fact]
    public void The_table_covers_every_cuisine_the_app_offers_with_room_to_spare()
    {
        string[] offered =
        [
            "Italian", "Indian", "Mexican", "Japanese", "Thai", "Chinese", "French", "Mediterranean",
            "Korean", "Caribbean", "British", "Arabic", "Eastern European", "Swedish", "Ghanaian",
        ];

        foreach (var cuisine in offered)
        {
            var dishes = CuisineDishes.For(cuisine, new HashSet<string>());
            Assert.True(dishes.Count >= 13, $"{cuisine} has only {dishes.Count} dishes");
        }

        // It was 99 before the expansion.
        Assert.True(CuisineDishes.DishCount >= 200, $"only {CuisineDishes.DishCount} dishes");
    }

    [Theory]
    // The dishes the model found that the table was missing, and which the benchmark attributed its
    // advantage to. These are the whole reason for the expansion.
    [InlineData("Eastern European", "vinegret")]
    [InlineData("Eastern European", "botwinka")]
    [InlineData("Eastern European", "placki kartoflane")]
    [InlineData("Swedish", "pyttipanna")]
    [InlineData("Swedish", "raggmunk")]
    [InlineData("Swedish", "janssons frestelse")]
    public void The_dishes_the_model_found_are_in_the_table_now(string cuisine, string dish)
    {
        var names = CuisineDishes.For(cuisine, new HashSet<string>()).Select(d => d.Name);
        Assert.Contains(dish, names);
    }

    [Fact]
    public void A_regional_name_is_searched_for_as_well_as_the_english_one()
    {
        // A publisher who wrote "pytt i panna" never wrote "Swedish hash", and the query has to match what
        // they wrote.
        var hash = CuisineDishes.For("Swedish", new HashSet<string>())
            .Single(dish => dish.Name == "pyttipanna");

        Assert.Contains("pytt i panna", hash.RegionalAliases);
        Assert.Contains("swedish hash", hash.RegionalAliases);
    }

    [Theory]
    // A title in a regional name identifies its cuisine, which it could not do before aliases existed.
    [InlineData("Pytt i Panna Recipe (Swedish Hash)", "Swedish")]
    [InlineData("Authentic Golabki (Polish Cabbage Rolls)", "Eastern European")]
    [InlineData("Melanzane alla Parmigiana", "Italian")]
    [InlineData("Murgh Makhani", "Indian")]
    public void A_dish_named_regionally_still_identifies_its_cuisine(string title, string expected)
    {
        Assert.Equal(expected, CuisineDishes.FromDishName(title));
    }

    [Theory]
    // Ingredient recognition: an ingredient that belongs to a kitchen now says so. None of these is a
    // concept in the normalizer's table, which is exactly why the dish scoring could not see them.
    [InlineData("Gochujang", "Korean")]
    [InlineData("2 tbsp gochujang paste", "Korean")]
    [InlineData("Nopales", "Mexican")]
    [InlineData("masa harina", "Mexican")]
    [InlineData("Scotch bonnet", "Caribbean")]
    [InlineData("miso paste", "Japanese")]
    [InlineData("lingonberry jam", "Swedish")]
    [InlineData("sauerkraut", "Eastern European")]
    [InlineData("pomegranate molasses", "Arabic")]
    [InlineData("samphire", "British")]
    public void An_ingredient_that_belongs_to_a_kitchen_says_so(string ingredient, string expected)
    {
        Assert.Contains(expected, CuisineDishes.CuisinesForIngredient(ingredient));
    }

    [Theory]
    // And one that belongs nowhere stays silent, rather than being forced into a kitchen.
    [InlineData("Onion")]
    [InlineData("Tomatoes")]
    [InlineData("Salt")]
    [InlineData("Chicken thighs")]
    public void An_everyday_ingredient_points_nowhere(string ingredient)
    {
        Assert.Empty(CuisineDishes.CuisinesForIngredient(ingredient));
    }

    [Fact]
    public void An_unfamiliar_ingredient_is_searched_for_in_the_customers_own_words()
    {
        // Normalize("Nopales") is "nopale" -- the fallback singulariser strips a trailing s from any word --
        // and nobody publishes a recipe under that. The concept is still what matching uses; only the
        // search term changes.
        Assert.Equal("nopale", _normalizer.Normalize("Nopales"));

        var focus = _normalizer.SelectRecipeSearchFocus(
            [new IngredientInput("Nopales", "2 cups"), new IngredientInput("Chicken thighs", "6")],
            3,
            mainIngredient: string.Empty);

        Assert.Contains("Nopales", focus);
        Assert.DoesNotContain("nopale", focus);
        // A known ingredient still goes in by its canonical name, which finds more pages than the variety.
        Assert.Contains("chicken", focus);
    }

    [Fact]
    public void Ingredients_from_different_kitchens_are_asked_about_separately()
    {
        // Measured on 25 November 2026: this basket produced "miso soup nopale plantain gochujang recipe"
        // and five more like it, every ingredient pasted into every cuisine's default dish. Those four come
        // from four kitchens and no publisher has written a page about all of them.
        var request = new GenerateRecipesRequest
        {
            Ingredients =
            [
                new IngredientInput("Nopales", "2 cups"),
                new IngredientInput("Plantain", "3"),
                new IngredientInput("Gochujang", "2 tbsp"),
                new IngredientInput("Samphire", "100 g"),
            ],
            CuisinePreference = string.Empty,
            DietaryPreference = "Anything",
            MealType = "Dinner",
            MaxRecipes = 4,
            Servings = 4,
        };

        var focus = _normalizer.SelectRecipeSearchFocus(request.Ingredients, 3, mainIngredient: string.Empty);
        var queries = RecipeSearchPlanner.Plan(request, 8, 0, focus, _normalizer);
        var all = string.Join(" | ", queries);

        // The mangled word is gone.
        Assert.DoesNotContain("nopale ", all, StringComparison.Ordinal);

        // Each ingredient is asked about in the kitchen it belongs to, on its own.
        Assert.Contains(queries, q =>
            q.Contains("Gochujang", StringComparison.OrdinalIgnoreCase) &&
            !q.Contains("Nopales", StringComparison.OrdinalIgnoreCase) &&
            !q.Contains("Plantain", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q =>
            q.Contains("Nopales", StringComparison.OrdinalIgnoreCase) &&
            !q.Contains("Gochujang", StringComparison.OrdinalIgnoreCase));

        // And those queries name that kitchen's dishes rather than another one's.
        Assert.Contains(queries, q =>
            q.Contains("Gochujang", StringComparison.OrdinalIgnoreCase) &&
            (q.Contains("bibimbap", StringComparison.OrdinalIgnoreCase) ||
             q.Contains("bulgogi", StringComparison.OrdinalIgnoreCase) ||
             q.Contains("tteokbokki", StringComparison.OrdinalIgnoreCase) ||
             q.Contains("jjigae", StringComparison.OrdinalIgnoreCase) ||
             q.Contains("Korean", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void A_dish_for_another_meal_is_offered_after_one_for_this_meal()
    {
        // Kedgeree is a breakfast and a shepherd's pie is a dinner. Both are British and both suit a basket
        // of potatoes; for a dinner search the dinner should lead. It is marked down, not removed: a
        // breakfast is a worse answer for dinner, not an invalid one.
        ISet<string> basket = new HashSet<string> { "potato", "fish", "rice", "egg", "onion" };

        var forDinner = CuisineDishes.For("British", basket, "Dinner").Select(d => d.Name).ToList();
        var kedgeree = forDinner.IndexOf("kedgeree");
        var fishAndChips = forDinner.IndexOf("fish and chips");

        Assert.True(fishAndChips < kedgeree, $"fish and chips placed {fishAndChips}, kedgeree {kedgeree}");
        Assert.Contains("kedgeree", forDinner);
    }

    [Fact]
    public void The_table_is_still_guidance_and_not_a_permitted_list()
    {
        // Nothing here is matched against a result or rejects one. A cuisine the table has never heard of
        // still plans a search; it falls back to the generic forms.
        var request = new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("Beef", "500 g")],
            CuisinePreference = "Peruvian",
            DietaryPreference = "Anything",
            MaxRecipes = 4,
            Servings = 4,
        };

        var queries = RecipeSearchPlanner.Plan(request, 4, 0, ["beef"], _normalizer);

        Assert.NotEmpty(queries);
        Assert.Empty(CuisineDishes.For("Peruvian", new HashSet<string> { "beef" }));
        Assert.All(queries, q => Assert.Contains("Peruvian", q, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_dish_declares_ingredients_that_the_normalizer_knows()
    {
        // A core ingredient the normalizer cannot produce can never be matched, so the dish would be dead
        // weight. This catches a typo in the table before it costs a search.
        var unknown = new List<string>();
        foreach (var (cuisine, dishes) in CuisineDishes.Table)
        {
            foreach (var dish in dishes)
            {
                foreach (var concept in dish.CoreIngredients.Concat(dish.CompatibleIngredients))
                {
                    if (!_normalizer.IsKnownConcept(concept))
                    {
                        unknown.Add($"{cuisine}/{dish.Name}: {concept}");
                    }
                }
            }
        }

        Assert.Empty(unknown);
    }
}
