using System.ComponentModel.DataAnnotations;
using Recipe.Api.Models;

namespace Recipe.Api.Tests;

public sealed class RecipeRequestValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(240)]
    public void Supports_extended_and_unlimited_cooking_times(int minutes)
    {
        var request = new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("chicken", "300 g")],
            MaxCookingMinutes = minutes
        };

        Assert.True(Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            [],
            validateAllProperties: true));
    }

    [Fact]
    public void Rejects_a_time_over_four_hours()
    {
        var request = new GenerateRecipesRequest
        {
            Ingredients = [new IngredientInput("chicken", "300 g")],
            MaxCookingMinutes = 241
        };

        Assert.False(Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            [],
            validateAllProperties: true));
    }
}
