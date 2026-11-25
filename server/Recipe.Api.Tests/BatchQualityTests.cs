using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// Judging a batch after the fact, which is the only thing that predicted where a model helped.
///
/// The Hybrid planner tried to spot a hard basket beforehand and could not: measured on 25 November 2026 its
/// triggers fired on 2 of 17 baskets, and neither was Eastern European or Swedish, the two the model
/// actually improved. Those baskets look ordinary from the request and only the results gave them away.
///
/// The fallback this feeds is off by default and unbenchmarked. These tests cover when it would fire.
/// </summary>
public sealed class BatchQualityTests
{
    private static readonly QueryPlanningOptions Defaults = new();

    [Fact]
    public void A_full_batch_of_the_right_cuisine_stands()
    {
        var batch = Batch(("Beef Tacos", "Mexican"), ("Beef Fajitas", "Mexican"),
                          ("Quesadillas", "Mexican"), ("Enchiladas", "Mexican"));

        Assert.False(BatchQuality.Assess(batch, Request("Mexican"), 4, Defaults).Weak);
    }

    [Fact]
    public void A_short_batch_is_weak()
    {
        var batch = Batch(("Beef Tacos", "Mexican"), ("Beef Fajitas", "Mexican"));

        var verdict = BatchQuality.Assess(batch, Request("Mexican"), 4, Defaults);

        Assert.True(verdict.Weak);
        Assert.Contains("2 of 4", verdict.Reason);
    }

    [Fact]
    public void A_batch_of_the_wrong_cuisine_is_weak()
    {
        // Four recipes, none of them the kitchen that was asked for.
        var batch = Batch(("Beef Stew", "British"), ("Beef Ragu", "Italian"),
                          ("Beef Curry", "Indian"), ("Beef Stroganoff", "Eastern European"));

        var verdict = BatchQuality.Assess(batch, Request("Mexican"), 4, Defaults);

        Assert.True(verdict.Weak);
        Assert.Contains("Mexican", verdict.Reason);
    }

    [Fact]
    public void Publishers_who_stated_no_cuisine_are_not_counted_against_the_batch()
    {
        // Most publishers leave recipeCuisine empty. Counting silence as a miss would condemn a good batch.
        var batch = Batch(("Beef Tacos", "Not stated"), ("Beef Fajitas", null),
                          ("Quesadillas", ""), ("Enchiladas", "Mexican"));

        Assert.False(BatchQuality.Assess(batch, Request("Mexican"), 4, Defaults).Weak);
    }

    [Fact]
    public void A_cuisine_nobody_asked_for_is_never_judged()
    {
        var batch = Batch(("Beef Stew", "British"), ("Beef Ragu", "Italian"),
                          ("Beef Curry", "Indian"), ("Beef Tacos", "Mexican"));

        Assert.False(BatchQuality.Assess(batch, Request(""), 4, Defaults).Weak);
        Assert.False(BatchQuality.Assess(batch, Request("Surprise Me"), 4, Defaults).Weak);
    }

    [Fact]
    public void A_broader_label_from_the_publisher_still_counts()
    {
        // "Asian" for a Korean dish is the publisher being vague, not wrong.
        var batch = Batch(("Bibimbap", "Korean"), ("Japchae", "Korean"),
                          ("Bulgogi", "Korean"), ("Kimchi Jjigae", "Asian"));

        Assert.False(BatchQuality.Assess(batch, Request("Korean"), 4, Defaults).Weak);
    }

    [Fact]
    public void The_fallback_is_off_until_somebody_turns_it_on()
    {
        // The spec is explicit: not enabled by default until benchmarked.
        var planning = new QueryPlanningOptions();

        Assert.False(planning.ResultQualityFallback);
        Assert.False(planning.FallbackOnFirstBatch);
        Assert.Equal("Deterministic", planning.Mode);
    }

    private static GenerateRecipesRequest Request(string cuisine) => new()
    {
        Ingredients = [new IngredientInput("Beef", "500 g")],
        CuisinePreference = cuisine,
        DietaryPreference = "Anything",
        MaxRecipes = 4,
        Servings = 4,
    };

    private static List<RecipeSuggestion> Batch(params (string Title, string? Cuisine)[] recipes) =>
        recipes.Select(item => new RecipeSuggestion(
            Guid.NewGuid(),
            item.Title,
            "A publisher recipe.",
            30,
            "Easy",
            item.Cuisine ?? string.Empty,
            4,
            0,
            [],
            [new RecipeIngredient("500 g", "beef")],
            [],
            "coral",
            SourceUrl: $"https://publisher.example.test/{Guid.NewGuid()}")).ToList();
}
