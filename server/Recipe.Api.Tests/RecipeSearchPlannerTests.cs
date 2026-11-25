using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// The planner exists because the old search issued one broad query. Measured on 24 November 2026, a
/// basket of raw beef brisket produced exactly `beef brisket recipes` and four chefs' versions of the
/// same slow-cooked joint came back. One query reaches one neighbourhood of the web.
/// </summary>
public sealed class RecipeSearchPlannerTests
{
    [Fact]
    public void One_ingredient_produces_several_different_queries()
    {
        var queries = RecipeSearchPlanner.Plan(Request(main: "beef brisket"), maxQueries: 5);

        Assert.True(queries.Count >= 3, "a single query can only find one kind of dinner");
        Assert.Equal(queries.Count, queries.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(queries, query => Assert.Contains("beef brisket", query, StringComparison.OrdinalIgnoreCase));
        // Several different kitchens, which is what turns one joint into several dinners.
        Assert.True(
            queries.Count(q => q.Contains("recipe", StringComparison.OrdinalIgnoreCase)) >= 3,
            string.Join(" | ", queries));
    }

    [Fact]
    public void A_chosen_cuisine_is_kept_in_every_query()
    {
        // A cuisine is a requirement, not a hint. Varying it would return dishes the customer said no to.
        //
        // What "kept" means changed on 25 November 2026. Every query used to carry the word, which is what
        // pushed image search towards pages with the cuisine in the title instead of pages about the dish.
        // Now a query either names the cuisine or names one of its dishes -- "katsu curry" is as Japanese as
        // "Japanese", and more use to a search engine.
        var normalizer = new IngredientNormalizer();
        var request = Request(main: "chicken", cuisine: "Japanese");
        var queries = RecipeSearchPlanner.Plan(request, maxQueries: 4, normalizer: normalizer);

        var japaneseDishes = CuisineDishes.For("Japanese", new HashSet<string> { "chicken" })
            .Select(dish => dish.Name)
            .ToList();
        Assert.All(queries, query => Assert.True(
            query.Contains("Japanese", StringComparison.OrdinalIgnoreCase) ||
            japaneseDishes.Any(dish => query.Contains(dish, StringComparison.OrdinalIgnoreCase)),
            $"neither the cuisine nor one of its dishes: {query}"));
        Assert.True(queries.Count >= 2, "the dish should still vary inside the cuisine");
    }

    [Fact]
    public void Surprise_me_fans_out_across_cuisines()
    {
        var queries = RecipeSearchPlanner.Plan(
            Request(main: "beef brisket", cuisine: "Surprise Me"),
            maxQueries: 5);

        // The point of Surprise Me: the same joint, cooked several different ways. A kitchen is reached
        // either by its name or by one of its dishes -- "tacos" is as Mexican as the word Mexican, and a
        // search engine does more with it. So the test counts kitchens reached, however they were reached.
        var all = string.Join(" | ", queries);
        var reached = Kitchens.Count(kitchen =>
            all.Contains(kitchen.Cuisine, StringComparison.OrdinalIgnoreCase) ||
            kitchen.Dishes.Any(dish => all.Contains(dish, StringComparison.OrdinalIgnoreCase)));
        Assert.True(reached >= 2, $"expected several kitchens, got: {all}");

        // And with a normalizer the dish is chosen to suit the basket rather than being the typical one.
        var fitted = string.Join(" | ", RecipeSearchPlanner.Plan(
            Request(main: "beef brisket", cuisine: "Surprise Me"),
            maxQueries: 6,
            normalizer: new IngredientNormalizer()));
        var beefDishes = new[] { "ragu", "lasagne", "keema", "tacos", "chilli", "bulgogi", "goulash", "kebab", "meatballs" };
        Assert.True(
            beefDishes.Count(dish => fitted.Contains(dish, StringComparison.OrdinalIgnoreCase)) >= 2,
            $"expected several beef dishes, got: {fitted}");
    }

    [Fact]
    public void An_empty_basket_still_plans_something_to_search_for()
    {
        // Inspire Me with nothing chosen at all. The cuisines are the only axis available.
        var queries = RecipeSearchPlanner.Plan(
            Request(discover: true),
            maxQueries: 4);

        Assert.NotEmpty(queries);
        Assert.All(queries, query => Assert.False(string.IsNullOrWhiteSpace(query)));

        // Each query has to name a dish, not just a cuisine. Measured on 24 November 2026, "italian
        // recipe" and its siblings returned 19 round-up pages out of 31 candidates and Inspire Me came
        // back with one recipe, because a query that broad matches magazine listicles.
        string[] dishes =
            ["pasta", "curry", "tacos", "rice bowl", "stir fry", "casserole", "traybake", "bibimbap",
             "stew", "pie", "kebab", "dumplings", "meatballs", "jollof"];
        Assert.All(queries, query => Assert.True(
            dishes.Any(dish => query.Contains(dish, StringComparison.OrdinalIgnoreCase)),
            $"no dish named in: {query}"));
    }

    [Fact]
    public void Meal_type_diet_and_time_reach_the_query()
    {
        // These are exactly the fields that were being dropped before the request model was fixed, so
        // they are asserted here as well as at the model boundary.
        var queries = RecipeSearchPlanner.Plan(
            Request(main: "chicken", mealType: "Breakfast", diet: "Vegetarian", maxMinutes: 30),
            maxQueries: 3);

        var all = string.Join(" | ", queries);
        Assert.Contains("breakfast", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vegetarian", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30 minutes", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Custom_instructions_are_searched_for_as_words()
    {
        var queries = RecipeSearchPlanner.Plan(
            Request(main: "lamb", custom: "something warming"),
            maxQueries: 2);

        Assert.Contains(queries, q => q.Contains("something warming", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Allergies_are_never_put_into_a_search_box()
    {
        // "no peanuts" as a search term finds peanut recipes. Allergies are enforced by FoodSafetyRules
        // against the real ingredient list after extraction, which is the only place the claim can
        // honestly be made.
        var request = Request(main: "satay chicken") with { Allergens = ["peanuts", "shellfish"] };
        var all = string.Join(" | ", RecipeSearchPlanner.Plan(request, maxQueries: 5));

        Assert.DoesNotContain("peanut", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shellfish", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_whole_pantry_is_not_put_into_one_query()
    {
        // No published recipe uses everything in somebody's fridge, so a query naming it all matches
        // nothing. The strongest few ingredients are enough.
        var request = Request() with
        {
            Ingredients = Enumerable.Range(0, 12)
                .Select(i => new IngredientInput($"ingredient{i}", "some"))
                .ToList(),
        };

        var queries = RecipeSearchPlanner.Plan(request, maxQueries: 3);

        Assert.All(queries, query =>
            Assert.True(
                Enumerable.Range(0, 12).Count(i => query.Contains($"ingredient{i}", StringComparison.OrdinalIgnoreCase)) <= 3,
                $"too many ingredients in one query: {query}"));
    }

    [Fact]
    public void A_basket_is_searched_for_what_is_substantial_in_it_not_what_is_first()
    {
        // Measured on 25 November 2026. A basket of salt, olive oil, water, chicken thighs and rice with no
        // main ingredient chosen searched for "salt olive oil water" -- the first three as they happened to
        // be listed -- and returned marinated olives, an olive oil bread dip and spaghetti aglio e olio. The
        // chicken and the rice were never searched for at all. A basket is whatever came off a shelf, in no
        // useful order, so the caller chooses the strongest few and the planner is told.
        var normalizer = new IngredientNormalizer();
        var request = Request() with
        {
            Ingredients =
            [
                new IngredientInput("salt", "1 tsp"),
                new IngredientInput("olive oil", "2 tbsp"),
                new IngredientInput("water", "500 ml"),
                new IngredientInput("chicken thighs", "6"),
                new IngredientInput("rice", "300 g"),
            ],
        };

        var focus = normalizer.SelectRecipeSearchFocus(request.Ingredients, 3, mainIngredient: string.Empty);
        var queries = RecipeSearchPlanner.Plan(request, maxQueries: 4, skip: 0, searchFocus: focus);

        var all = string.Join(" | ", queries);
        Assert.Contains("chicken", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rice", all, StringComparison.OrdinalIgnoreCase);
        // The pantry staples are not what anybody is cooking.
        Assert.DoesNotContain("salt", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("water", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("olive oil", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_chosen_main_ingredient_still_wins_over_the_rest_of_the_basket()
    {
        // The customer naming one ingredient as the point of the dish outranks any selection we would make.
        var normalizer = new IngredientNormalizer();
        var request = Request(main: "beef brisket") with
        {
            Ingredients =
            [
                new IngredientInput("potatoes", "1 kg"),
                new IngredientInput("beef brisket", "1 kg"),
            ],
        };

        var focus = normalizer.SelectRecipeSearchFocus(request.Ingredients, 3, mainIngredient: "beef brisket");
        var queries = RecipeSearchPlanner.Plan(request, maxQueries: 4, skip: 0, searchFocus: focus);

        Assert.All(queries, query => Assert.Contains("beef brisket", query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_later_slice_of_the_plan_asks_different_questions()
    {
        // "Show more results" depends on this. The plan is far longer than one batch -- fifteen cuisines,
        // fifteen signature dishes -- and a second tap that re-ran the first four queries would find the
        // same pages and have nothing new to offer.
        var request = Request(main: "beef brisket");

        var first = RecipeSearchPlanner.Plan(request, maxQueries: 4);
        var later = RecipeSearchPlanner.Plan(request, maxQueries: 4, skip: 4);

        Assert.Equal(4, first.Count);
        Assert.NotEmpty(later);
        Assert.Empty(first.Intersect(later, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_first_batch_is_unchanged_by_the_slicing()
    {
        // Skipping nothing has to mean exactly what it always did, or every first search changes.
        var request = Request(main: "chicken", cuisine: "Japanese");

        Assert.Equal(
            RecipeSearchPlanner.Plan(request, maxQueries: 4),
            RecipeSearchPlanner.Plan(request, maxQueries: 4, skip: 0));
    }

    [Fact]
    public void The_plan_runs_out_rather_than_starting_over()
    {
        // Wrapping around would quietly serve the first batch again as though it were new.
        Assert.Empty(RecipeSearchPlanner.Plan(Request(main: "beef brisket"), maxQueries: 4, skip: 500));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    public void The_query_count_is_bounded(int requested)
    {
        var queries = RecipeSearchPlanner.Plan(Request(main: "beef"), maxQueries: requested);
        Assert.InRange(queries.Count, 1, 8);
    }

    /// <summary>A kitchen is recognisable by its name or by any of its dishes.</summary>
    private static readonly (string Cuisine, string[] Dishes)[] Kitchens =
    [
        ("Italian", ["pasta", "risotto", "lasagne", "ragu"]),
        ("Indian", ["curry", "biryani", "dal", "keema"]),
        ("Mexican", ["tacos", "fajitas", "chilli", "picadillo"]),
        ("Japanese", ["donburi", "ramen", "teriyaki", "katsu"]),
        ("Thai", ["pad thai", "massaman", "satay", "tom yum"]),
        ("Chinese", ["fried rice", "chow mein", "char siu", "kung pao"]),
        ("French", ["casserole", "gratin", "ratatouille", "bourguignon"]),
    ];

    private static GenerateRecipesRequest Request(
        string main = "",
        string cuisine = "",
        string mealType = "",
        string diet = "Anything",
        string custom = "",
        int maxMinutes = 0,
        bool discover = false) => new()
    {
        Ingredients = main.Length > 0 ? [new IngredientInput(main, "as available")] : [],
        MainIngredient = main,
        CuisinePreference = cuisine,
        MealType = mealType,
        DietaryPreference = diet,
        CustomInstructions = custom,
        MaxCookingMinutes = maxMinutes,
        DiscoverByPreference = discover,
        MaxRecipes = 4,
        Servings = 2,
    };
}
