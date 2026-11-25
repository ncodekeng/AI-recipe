using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// Telling a dinner from a part of one.
///
/// For a basket of beef, tortillas and peppers, beef tacos and quesadillas are dinners. Taco meat is a
/// filling that feeds nobody on its own, and taco stuffed peppers is a dinner that leaves the tortillas in
/// the cupboard. All three are real recipes and all three say taco.
///
/// Two generic signals, neither reading anything into a title: does the recipe use a staple base the
/// customer actually has, and does the publisher's own recipeCategory call it a meal. Both default to full
/// credit, because a basket without a base and a page without metadata are not evidence against anything.
/// </summary>
public sealed class DishSuitabilityTests
{
    private readonly IngredientNormalizer _normalizer = new();
    private readonly RecipeRankingService _ranking;

    public DishSuitabilityTests() => _ranking = new RecipeRankingService(_normalizer);

    private static readonly IngredientInput[] TacoBasket =
    [
        new("Beef", "500 g"),
        new("Taco bread", "8"),
        new("Peppers", "3"),
    ];

    [Fact]
    public void A_dish_built_on_the_basket_outranks_a_filling_made_from_it()
    {
        // The client's own example. Nothing here mentions Mexico, tacos or tortillas as a special case: the
        // tortilla is simply the basket's staple base, and one recipe uses it.
        var tacos = Dish("Beef Tacos", "Main Course", "beef mince", "tortillas", "bell peppers", "onion");
        var filling = Dish("Taco Meat", "Main Course", "beef mince", "onion", "chilli powder", "cumin");

        var ranked = _ranking.Rank([filling, tacos], TacoBasket, mainIngredient: "beef");

        Assert.Equal("Beef Tacos", ranked[0].Title);
        Assert.True(
            ranked[0].IngredientMatch > ranked[1].IngredientMatch,
            $"{ranked[0].IngredientMatch} should beat {ranked[1].IngredientMatch}");
    }

    [Fact]
    public void A_dinner_that_leaves_the_basket_base_unused_ranks_below_one_that_uses_it()
    {
        // Taco stuffed peppers is a whole dinner and perfectly good. It is just a worse answer for somebody
        // who bought tortillas.
        var tacos = Dish("Beef Tacos", "Main Course", "beef mince", "tortillas", "bell peppers");
        var stuffed = Dish("Taco Stuffed Peppers", "Main Course", "beef mince", "bell peppers", "rice", "cheddar");

        var ranked = _ranking.Rank([stuffed, tacos], TacoBasket, mainIngredient: "beef");

        Assert.Equal("Beef Tacos", ranked[0].Title);
    }

    [Theory]
    // What publishers write when the page is a part of a meal rather than a meal.
    [InlineData("Sauce", RecipeCourse.Component)]
    [InlineData("Side Dish", RecipeCourse.Component)]
    [InlineData("Condiments and Sauces", RecipeCourse.Component)]
    [InlineData("Spice Blend", RecipeCourse.Component)]
    [InlineData("Stock", RecipeCourse.Component)]
    [InlineData("Salad Dressing", RecipeCourse.Component)]
    [InlineData("Appetizer", RecipeCourse.Component)]
    // And when it is a meal.
    [InlineData("Main Course", RecipeCourse.Main)]
    [InlineData("main dish", RecipeCourse.Main)]
    [InlineData("Dinner", RecipeCourse.Main)]
    [InlineData("Breakfast", RecipeCourse.Main)]
    [InlineData("Entree", RecipeCourse.Main)]
    // A different meal entirely.
    [InlineData("Dessert", RecipeCourse.OtherCourse)]
    [InlineData("Cocktail", RecipeCourse.OtherCourse)]
    // Absence, and values that say nothing about the course.
    [InlineData("", RecipeCourse.Unknown)]
    [InlineData(null, RecipeCourse.Unknown)]
    [InlineData("Beef", RecipeCourse.Unknown)]
    [InlineData("Gluten Free", RecipeCourse.Unknown)]
    public void The_publishers_own_category_says_what_kind_of_thing_it_is(string? category, RecipeCourse expected)
    {
        Assert.Equal(expected, RecipeCourses.Classify(category));
    }

    [Fact]
    public void A_page_with_no_category_is_never_penalised_for_it()
    {
        // Most publishers set no category at all. Treating silence as "this is a sauce" would bury them.
        var stated = Dish("Beef Tacos", "Main Course", "beef mince", "tortillas", "bell peppers");
        var silent = Dish("Beef Tortilla Wraps", null, "beef mince", "tortillas", "bell peppers");

        var match = _ranking.CalculateMatch(TacoBasket, silent.Ingredients, "beef", silent.PublisherCategory);
        var statedMatch = _ranking.CalculateMatch(TacoBasket, stated.Ingredients, "beef", stated.PublisherCategory);

        Assert.Equal(statedMatch.DishSuitability, match.DishSuitability);
        Assert.Equal(1d, match.DishSuitability);
    }

    [Fact]
    public void A_component_is_only_marked_down_when_a_whole_meal_was_asked_for()
    {
        // Somebody asking for a snack or a side wants exactly the thing a dinner search would mark down, so
        // the distinction only applies where it is the customer's own ask.
        var sauce = Dish("Taco Sauce", "Sauce", "tomatoes", "chilli powder", "onion");

        var forDinner = _ranking.CalculateMatch(TacoBasket, sauce.Ingredients, null, sauce.PublisherCategory, "Dinner");
        var forASnack = _ranking.CalculateMatch(TacoBasket, sauce.Ingredients, null, sauce.PublisherCategory, "Snack");

        Assert.True(forASnack.DishSuitability > forDinner.DishSuitability);
        Assert.True(RecipeCourses.WantsAWholeDish("Dinner"));
        Assert.True(RecipeCourses.WantsAWholeDish(""));
        Assert.False(RecipeCourses.WantsAWholeDish("Snack"));
    }

    [Fact]
    public void A_basket_with_no_staple_base_is_not_marked_down_for_having_none()
    {
        // Beef and peppers alone: there is no base to build on, so nothing can be docked for missing it.
        IngredientInput[] basket = [new("Beef", "500 g"), new("Peppers", "3")];
        var stew = Dish("Beef and pepper stew", "Main Course", "beef", "bell peppers", "onion");

        var match = _ranking.CalculateMatch(basket, stew.Ingredients, "beef", stew.PublisherCategory, "Dinner");

        Assert.Equal(1d, match.DishSuitability);
    }

    [Fact]
    public void A_distinctive_ingredient_counts_for_more_than_an_everyday_one()
    {
        // Almost every savoury recipe uses an onion; the tortillas decide what the dinner is. Counting
        // basket items equally makes those the same contribution.
        Assert.True(
            _normalizer.DistinctivenessWeight("tortillas") > _normalizer.DistinctivenessWeight("onion"),
            "a staple base should outweigh an aromatic");
        Assert.True(
            _normalizer.DistinctivenessWeight("beef") > _normalizer.DistinctivenessWeight("soy sauce"),
            "a protein should outweigh a condiment");
    }

    [Fact]
    public void Nothing_here_is_specific_to_one_cuisine()
    {
        // The same rule across unrelated kitchens: the recipe using the basket's base wins each time.
        (IngredientInput[] Basket, string Main, string WithBase, string WithoutBase)[] cases =
        [
            ([new("Rice", "300 g"), new("Chicken thighs", "6")], "chicken",
                "Chicken fried rice", "Chicken wing sauce"),
            ([new("Pasta", "400 g"), new("Beef", "500 g")], "beef",
                "Beef ragu pasta bake", "Beef stock"),
            ([new("Potatoes", "1 kg"), new("Lamb mince", "500 g")], "lamb",
                "Shepherds pie", "Lamb rub"),
        ];

        foreach (var (basket, main, withBase, withoutBase) in cases)
        {
            var baseDish = Dish(withBase, "Main Course", main, basket[0].Name, "onion");
            var other = Dish(withoutBase, "Main Course", main, "onion", "garlic");

            var ranked = _ranking.Rank([other, baseDish], basket, mainIngredient: main);

            Assert.Equal(withBase, ranked[0].Title);
        }
    }

    private static RecipeSuggestion Dish(string title, string? category, params string[] ingredients) => new(
        Guid.NewGuid(),
        title,
        "A publisher recipe.",
        30,
        "Easy",
        "Not stated",
        4,
        0,
        [],
        ingredients.Select(name => new RecipeIngredient("as listed", name, null, null, name)).ToList(),
        [],
        "coral",
        SourceUrl: $"https://publisher.example.test/{Guid.NewGuid()}",
        PublisherCategory: category);
}
