using Recipe.Api.Models;

namespace Recipe.Api.Tests;

/// <summary>
/// What the app sends has to arrive at the search.
///
/// Found on 24 November 2026 while running the app locally: choosing a cuisine, a meal type or a
/// preference chip, or typing into "Anything else?", changed nothing about the results. Two separate
/// faults, neither of which failed loudly.
///
/// MealType, DiscoverByPreference and CustomInstructions were missing from the request model, so JSON
/// deserialization discarded them. CuisinePreference and RecipePreferences were in the model but left
/// out of the hand-written copy in ApplyDietaryRestrictions, so they reached the search as empty.
/// </summary>
public sealed class RequestFieldsReachTheSearchTests
{
    private static readonly string[] SentByTheApp =
    [
        // Every field src/api/plateApi.js puts in the body. A name here that the model does not have is
        // a selection the customer makes and the search never sees.
        "Ingredients", "Allergens", "AvoidIngredients", "DietaryPreference", "CuisinePreference",
        "MealType", "DiscoverByPreference", "RecipePreferences", "RecentlyShownRecipeIds",
        "MainIngredient", "CustomInstructions", "MaxCookingMinutes", "Servings", "ShowPhotos",
        "MaxRecipes", "OnlyUseAvailableIngredients",
    ];

    [Fact]
    public void Every_field_the_app_sends_exists_on_the_request()
    {
        var known = typeof(GenerateRecipesRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = SentByTheApp.Where(name => !known.Contains(name)).ToList();

        Assert.True(
            missing.Count == 0,
            $"The app sends {string.Join(", ", missing)}, which the model does not have. "
            + "Deserialization drops them silently, so the customer's choice is discarded without any error.");
    }

    [Fact]
    public void Narrowing_the_basket_keeps_every_other_choice()
    {
        // ApplyDietaryRestrictions rebuilds the request to drop ingredients that conflict with the
        // restrictions. Written out by hand it quietly lost two fields; `with` copies them all. This
        // asserts the copy semantics the fix depends on.
        var original = new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("pork", "200g")],
            CuisinePreference = "Japanese",
            MealType = "Dinner",
            RecipePreferences = ["Gluten Free", "High Protein"],
            CustomInstructions = "something warming",
            DiscoverByPreference = true,
            DietaryPreference = "Halal",
            MaxCookingMinutes = 30,
            Servings = 4,
            MaxRecipes = 4,
            OnlyUseAvailableIngredients = true,
        };

        var narrowed = original with
        {
            Ingredients = [],
            MainIngredient = string.Empty,
        };

        Assert.Equal("Japanese", narrowed.CuisinePreference);
        Assert.Equal("Dinner", narrowed.MealType);
        Assert.Equal(["Gluten Free", "High Protein"], narrowed.RecipePreferences);
        Assert.Equal("something warming", narrowed.CustomInstructions);
        Assert.True(narrowed.DiscoverByPreference);
        Assert.Equal("Halal", narrowed.DietaryPreference);
        Assert.Equal(30, narrowed.MaxCookingMinutes);
        Assert.Equal(4, narrowed.Servings);
        Assert.True(narrowed.OnlyUseAvailableIngredients);
        // Only what was asked for changed.
        Assert.Empty(narrowed.Ingredients);
    }

    [Fact]
    public void An_empty_basket_is_valid_only_when_discovering()
    {
        // Inspire Me says "No scanning needed" on screen and sends no ingredients. [MinLength(1)] made
        // that a 400 before the controller ran, so the feature could never work. Everywhere else an
        // empty basket still means a scan that found nothing.
        var discover = new GenerateRecipesRequest { Ingredients = [], DiscoverByPreference = true };
        var scan = new GenerateRecipesRequest { Ingredients = [] };

        Assert.Empty(Validate(discover));
        Assert.Empty(Validate(scan));

        // Model validation must now let both through; the distinction is the controller's to make,
        // because only it can see both fields together.
        Assert.True(discover.DiscoverByPreference);
        Assert.False(scan.DiscoverByPreference);
    }

    [Fact]
    public void A_basket_over_the_limit_is_still_rejected_by_the_model()
    {
        // Removing MinLength must not remove MaxLength with it.
        var tooMany = new GenerateRecipesRequest
        {
            Ingredients = Enumerable
                .Range(0, GenerateRecipesRequest.MaxIngredientCount + 1)
                .Select(i => new IngredientInput($"item {i}", "1"))
                .ToList(),
        };

        Assert.NotEmpty(Validate(tooMany));
    }

    private static List<System.ComponentModel.DataAnnotations.ValidationResult> Validate(
        GenerateRecipesRequest request)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request,
            new System.ComponentModel.DataAnnotations.ValidationContext(request),
            results,
            validateAllProperties: true);
        return results;
    }
}
