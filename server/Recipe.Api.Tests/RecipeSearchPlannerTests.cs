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
        var queries = RecipeSearchPlanner.Plan(
            Request(main: "chicken", cuisine: "Japanese"),
            maxQueries: 4);

        Assert.All(queries, query => Assert.Contains("Japanese", query, StringComparison.OrdinalIgnoreCase));
        Assert.True(queries.Count >= 2, "the dish form should still vary inside the cuisine");
    }

    [Fact]
    public void Surprise_me_fans_out_across_cuisines()
    {
        var queries = RecipeSearchPlanner.Plan(
            Request(main: "beef brisket", cuisine: "Surprise Me"),
            maxQueries: 5);

        // The point of Surprise Me: the same joint, cooked several different ways.
        var cuisines = new[] { "Italian", "Indian", "Mexican", "Japanese", "Thai", "Chinese", "French" };
        var hits = cuisines.Count(c => queries.Any(q => q.Contains(c, StringComparison.OrdinalIgnoreCase)));
        Assert.True(hits >= 2, $"expected several cuisines, got: {string.Join(" | ", queries)}");
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    public void The_query_count_is_bounded(int requested)
    {
        var queries = RecipeSearchPlanner.Plan(Request(main: "beef"), maxQueries: requested);
        Assert.InRange(queries.Count, 1, 8);
    }

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
