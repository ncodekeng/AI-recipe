using Recipe.Api.Models;

namespace Recipe.Api.Services;

public sealed class RecipeSafetyValidator
{
    public RecipeGenerationResponse Validate(
        RecipeGenerationResponse response,
        GenerateRecipesRequest request)
    {
        var accepted = new List<RecipeSuggestion>();
        var blockedCount = 0;

        foreach (var recipe in response.Recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.Title) ||
                recipe.Ingredients.Count == 0 ||
                (recipe.Steps.Count == 0 && string.IsNullOrWhiteSpace(recipe.SourceUrl)))
            {
                blockedCount++;
                continue;
            }

            var conflicts = FoodSafetyRules.FindConflicts(
                recipe.Ingredients.Select(item => item.Name).Concat(recipe.Steps),
                request.Allergens,
                request.DietaryPreference,
                request.AvoidIngredients);

            if (conflicts.Count > 0)
            {
                blockedCount++;
                continue;
            }

            accepted.Add(recipe);
        }

        if (accepted.Count == 0)
        {
            throw new RecipeSafetyException(
                "No recipe passed the selected safety checks. Review the restrictions or try different ingredients.");
        }

        const string safetyNote =
            "No known conflicts were found from the listed ingredients. Always verify product labels, substitutions, and cross-contamination warnings.";
        var notice = response.Notice;
        if (blockedCount > 0)
        {
            var validationNotice = blockedCount == 1
                ? "One recipe was hidden because it did not pass the selected restrictions."
                : $"{blockedCount} recipes were hidden because they did not pass the selected restrictions.";
            notice = string.IsNullOrWhiteSpace(notice)
                ? validationNotice
                : $"{notice} {validationNotice}";
        }

        return response with
        {
            Recipes = accepted,
            SafetyNote = safetyNote,
            Notice = notice
        };
    }
}

public sealed class RecipeSafetyException(string message) : Exception(message);
