using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// Planning a search from the cuisine AND the basket, rather than from the cuisine's name.
///
/// The client's first test failed on this. With Mexican chosen and a basket of beef, taco bread and peppers,
/// the plan was "Mexican beef bread pepper recipe" and its traybake, stew and salad variants, because the
/// only thing a chosen cuisine varied was the generic form. Measured on 25 November 2026 that returned
/// Mexican Picadillo, Mexican Beef With Mini Cheese Breads, Mexican Stuffed Peppers and a fajita traybake:
/// every title carrying the word Mexican, and not a taco among them.
///
/// Two separate faults. "Taco bread" normalized to "bread", so the basket's most informative ingredient
/// became its least informative one; that is covered in IngredientNormalizerTests. And the planner could
/// not name a dish, which is what these cover.
/// </summary>
public sealed class CuisineDishPlanningTests
{
    private readonly IngredientNormalizer _normalizer = new();

    /// <summary>The client's basket, exactly as reported.</summary>
    private static GenerateRecipesRequest ClientBasket() => new()
    {
        Ingredients =
        [
            new IngredientInput("Beef", "500 g"),
            new IngredientInput("Taco bread", "8"),
            new IngredientInput("Peppers", "3"),
        ],
        MainIngredient = string.Empty,
        CuisinePreference = "Mexican",
        DietaryPreference = "Anything",
        MaxCookingMinutes = 45,
        MaxRecipes = 4,
        Servings = 4,
    };

    [Fact]
    public void The_clients_basket_asks_for_tortilla_dishes()
    {
        var queries = Plan(ClientBasket());
        var all = string.Join(" | ", queries);

        // The acceptance criterion: the plan names tortilla-based dishes, tacos leading.
        Assert.StartsWith("tacos", queries[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fajitas", all, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            new[] { "quesadilla", "enchilada", "burrito", "tostada", "nacho" }
                .Any(dish => all.Contains(dish, StringComparison.OrdinalIgnoreCase)),
            all);

        // And it no longer asks for bread, or for a Mexican stew, or for a Mexican salad.
        Assert.DoesNotContain("bread", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stew", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salad", all, StringComparison.OrdinalIgnoreCase);

        // The ingredients still reach the query; the carrier does not, because the dish name implies it.
        Assert.Contains("beef", queries[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pepper", queries[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tortilla", queries[0], StringComparison.OrdinalIgnoreCase);

        // The time limit is still honoured in the query text.
        Assert.Contains("under 45 minutes", queries[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_dish_only_its_own_cuisine_cooks_does_not_carry_the_cuisine_name()
    {
        // The client's own diagnosis: the word Mexican in the query pushes image search towards pages with
        // Mexican in the title rather than pages about the dish. A taco needs no such help.
        var queries = Plan(ClientBasket());

        Assert.DoesNotContain("Mexican", queries[0], StringComparison.OrdinalIgnoreCase);
        Assert.False(CuisineDishes.IsShared("tacos"));
    }

    [Fact]
    public void A_dish_several_cuisines_share_does_carry_the_cuisine_name()
    {
        // "stir fry" is Thai and Chinese; "traybake" is Italian, Mediterranean and British. Those are
        // ambiguous on their own, so the kitchen has to be named.
        Assert.True(CuisineDishes.IsShared("stir fry"));
        Assert.True(CuisineDishes.IsShared("traybake"));
        Assert.True(CuisineDishes.IsShared("stuffed peppers"));

        var request = ClientBasket() with
        {
            CuisinePreference = "Chinese",
            Ingredients =
            [
                new IngredientInput("Chicken thighs", "6"),
                new IngredientInput("Peppers", "3"),
            ],
        };

        var stirFry = Plan(request).First(query => query.Contains("stir fry", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Chinese", stirFry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tacos_are_not_forced_on_a_mexican_basket_that_has_no_tortillas()
    {
        // The guard against hardcoding the client's basket. Mexican with chicken and rice should lead with
        // something that uses chicken and rice, not with tacos because tacos are Mexican.
        var request = ClientBasket() with
        {
            Ingredients =
            [
                new IngredientInput("Chicken thighs", "6"),
                new IngredientInput("Rice", "300 g"),
            ],
        };

        var queries = Plan(request);

        Assert.DoesNotContain("tacos", queries[0], StringComparison.OrdinalIgnoreCase);
        Assert.True(
            queries[0].Contains("burrito", StringComparison.OrdinalIgnoreCase) ||
            queries[0].Contains("stuffed peppers", StringComparison.OrdinalIgnoreCase),
            queries[0]);
    }

    [Theory]
    // One representative basket per supported cuisine, with the dish it ought to reach for. The point is
    // that every cuisine is served by the same mechanism, not that these particular dishes are the only
    // right answers.
    [InlineData("Italian", "Pasta", "Tomatoes", "pasta")]
    [InlineData("Italian", "Aubergine", "Mozzarella", "parmigiana")]
    [InlineData("Indian", "Red lentils", "Onion", "dal")]
    [InlineData("Indian", "Chickpeas", "Tomatoes", "chana masala")]
    [InlineData("Mexican", "Tortillas", "Beef", "tacos")]
    [InlineData("Japanese", "Chicken thighs", "Rice", "teriyaki")]
    [InlineData("Thai", "Rice noodles", "Prawns", "pad thai")]
    [InlineData("Chinese", "Rice", "Eggs", "fried rice")]
    [InlineData("French", "Potatoes", "Double cream", "gratin")]
    [InlineData("Mediterranean", "Aubergine", "Lamb mince", "moussaka")]
    [InlineData("Korean", "Rice", "Eggs", "bibimbap")]
    [InlineData("Caribbean", "Rice", "Kidney beans", "rice and peas")]
    [InlineData("British", "Lamb mince", "Potatoes", "shepherds pie")]
    [InlineData("Arabic", "Chickpeas", "Lemon", "hummus")]
    [InlineData("Eastern European", "Beetroot", "Carrots", "borscht")]
    [InlineData("Swedish", "Salmon fillets", "Lemon", "gravlax")]
    [InlineData("Ghanaian", "Rice", "Tomatoes", "jollof rice")]
    public void Every_cuisine_plans_from_its_own_dishes(
        string cuisine,
        string first,
        string second,
        string expectedDish)
    {
        var request = ClientBasket() with
        {
            CuisinePreference = cuisine,
            MaxCookingMinutes = 0,
            Ingredients = [new IngredientInput(first, "some"), new IngredientInput(second, "some")],
        };

        var all = string.Join(" | ", Plan(request, maxQueries: 8));

        Assert.Contains(expectedDish, all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_cuisine_the_app_offers_has_dishes()
    {
        // The app's picker and this table have to agree, or a cuisine silently falls back to generic forms.
        string[] offered =
        [
            "Italian", "Indian", "Mexican", "Japanese", "Thai", "Chinese", "French", "Mediterranean",
            "Korean", "Caribbean", "British", "Arabic", "Eastern European", "Swedish", "Ghanaian",
        ];

        foreach (var cuisine in offered)
        {
            Assert.NotEmpty(CuisineDishes.For(cuisine, new HashSet<string>()));
        }
    }

    [Fact]
    public void An_unknown_cuisine_still_plans_something()
    {
        // Nothing may depend on the table being complete: a cuisine it has never heard of falls back to the
        // generic forms rather than returning no plan at all.
        var request = ClientBasket() with { CuisinePreference = "Peruvian" };

        var queries = Plan(request);

        Assert.NotEmpty(queries);
        Assert.All(queries, query => Assert.Contains("Peruvian", query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Show_more_reaches_dishes_the_first_batch_did_not_ask_for()
    {
        var request = ClientBasket();

        var first = Plan(request, maxQueries: 4);
        var later = RecipeSearchPlanner.Plan(
            request,
            maxQueries: 4,
            skip: 4,
            searchFocus: Focus(request),
            normalizer: _normalizer);

        Assert.Empty(first.Intersect(later, StringComparer.OrdinalIgnoreCase));
        // Still Mexican dishes, not a fallback to generic forms after one batch.
        Assert.NotEmpty(later);
    }

    [Fact]
    public void A_basket_with_no_cuisine_chosen_asks_each_kitchen_for_its_best_fit()
    {
        // Surprise Me with ingredients. Each cuisine should be asked about a dish that suits the basket,
        // rather than being named and left to the search engine to interpret.
        var request = ClientBasket() with { CuisinePreference = string.Empty, MaxCookingMinutes = 0 };

        var all = string.Join(" | ", Plan(request, maxQueries: 8));

        // The basket is beef, tortillas and peppers, so each kitchen is asked about what it makes from
        // those: Mexican about tacos, Italian about a beef dish. Not about the word "Italian".
        Assert.Contains("tacos", all, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            all.Contains("lasagne", StringComparison.OrdinalIgnoreCase) ||
            all.Contains("ragu", StringComparison.OrdinalIgnoreCase),
            all);
    }

    [Theory]
    // What publishers actually write in recipeCuisine, mapped onto what the app offers.
    [InlineData("Mexican", "Mexican")]
    [InlineData("Tex-Mex", "Mexican")]
    [InlineData("Sichuan", "Chinese")]
    [InlineData("Greek", "Mediterranean")]
    [InlineData("Jamaican", "Caribbean")]
    [InlineData("Polish", "Eastern European")]
    [InlineData("Nordic", "Swedish")]
    [InlineData("italian-american", "Italian")]
    // Cuisines the picker has no entry for are still true statements about a recipe.
    [InlineData("Vietnamese", "Vietnamese")]
    [InlineData("American", "American")]
    public void A_publishers_cuisine_is_read_as_what_it_means(string stated, string expected)
    {
        Assert.Equal(expected, CuisineDishes.Canonicalise(stated));
    }

    [Theory]
    // Measured from live pages on 25 November 2026. recipeCuisine carries a great deal that is not a
    // cuisine, and passing it through labelled a recipe "Gluten Free" cuisine.
    [InlineData("Cheese Cuisine")]
    [InlineData("Gluten Free")]
    [InlineData("Dairy-Free")]
    [InlineData("Dessert")]
    [InlineData("Vegan")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_publisher_value_that_is_not_a_cuisine_is_not_treated_as_one(string stated)
    {
        Assert.Null(CuisineDishes.Canonicalise(stated));
    }

    [Theory]
    // Where the publisher says nothing, the dish can still give it away -- but only a dish one cuisine
    // alone cooks. A stir fry could be Thai or Chinese, so it says nothing.
    [InlineData("Easy Beef Tacos", "Mexican")]
    [InlineData("Pasta alla Sorrentina", "Italian")]
    [InlineData("The Best Red Lentil Dahl", null)]
    [InlineData("Bibimbap (Korean Mixed Rice Bowl)", "Korean")]
    [InlineData("Chicken Stir Fry", null)]
    [InlineData("Weeknight Chicken Dinner", null)]
    public void A_dish_one_cuisine_alone_cooks_identifies_it(string title, string? expected)
    {
        Assert.Equal(expected, CuisineDishes.FromDishName(title));
    }

    private IReadOnlyList<string> Plan(GenerateRecipesRequest request, int maxQueries = 4) =>
        RecipeSearchPlanner.Plan(request, maxQueries, 0, Focus(request), _normalizer);

    private IReadOnlyList<string> Focus(GenerateRecipesRequest request) =>
        _normalizer.SelectRecipeSearchFocus(request.Ingredients, 3, mainIngredient: request.MainIngredient);
}
