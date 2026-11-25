namespace Recipe.Api.Services;

/// <summary>
/// Whether a page is a dinner or a part of one, read from the publisher's own schema.org recipeCategory.
///
/// It exists for the difference between a dish and a component. For a basket of beef, tortillas and peppers,
/// beef tacos and quesadillas are dinners; taco meat is a filling and will not feed anyone on its own, and
/// a taco seasoning blend is a jar of spices. All three are legitimate recipes, all three mention tacos, and
/// only the first kind answers "what shall I cook tonight".
///
/// Read from metadata rather than from the title on purpose. A title is marketing -- "Easy Taco Night",
/// "The Only Taco Meat You Need" -- and keyword-matching it is what produced the fault this whole piece of
/// work started from. recipeCategory is the publisher's own statement about what they published.
///
/// Absence is not evidence. Most pages set no category at all, so <see cref="Unknown"/> must never be
/// treated as a component: that would push every publisher who skips the field down the rankings.
/// </summary>
public enum RecipeCourse
{
    /// <summary>No category stated, or one that says nothing about the course. Never penalised.</summary>
    Unknown,

    /// <summary>A meal in its own right: main course, dinner, lunch, breakfast, one-pot.</summary>
    Main,

    /// <summary>A part of a meal: a sauce, a filling, a side, a dressing, a spice blend, a stock.</summary>
    Component,

    /// <summary>A meal, but not the one being asked for: a dessert, a drink, a bake.</summary>
    OtherCourse,
}

public static class RecipeCourses
{
    // Matched against the publisher's recipeCategory, longest needle first so "main course" is read before
    // "course" and "side dish" before "dish". Everything here is a phrase publishers actually write.
    private static readonly (string Needle, RecipeCourse Course)[] Categories =
    [
        // A meal in its own right.
        ("main course", RecipeCourse.Main), ("main dish", RecipeCourse.Main), ("maincourse", RecipeCourse.Main),
        ("main", RecipeCourse.Main), ("entree", RecipeCourse.Main), ("entrée", RecipeCourse.Main),
        ("dinner", RecipeCourse.Main), ("supper", RecipeCourse.Main), ("lunch", RecipeCourse.Main),
        ("breakfast", RecipeCourse.Main), ("brunch", RecipeCourse.Main), ("one pot", RecipeCourse.Main),
        ("one-pot", RecipeCourse.Main), ("traybake", RecipeCourse.Main), ("casserole", RecipeCourse.Main),
        ("curry", RecipeCourse.Main), ("soup", RecipeCourse.Main), ("stew", RecipeCourse.Main),
        ("salad", RecipeCourse.Main), ("sandwich", RecipeCourse.Main), ("pasta", RecipeCourse.Main),
        // A part of a meal.
        ("side dish", RecipeCourse.Component), ("side", RecipeCourse.Component),
        ("sauce", RecipeCourse.Component), ("gravy", RecipeCourse.Component),
        ("dressing", RecipeCourse.Component), ("marinade", RecipeCourse.Component),
        ("condiment", RecipeCourse.Component), ("dip", RecipeCourse.Component),
        ("salsa", RecipeCourse.Component), ("spice", RecipeCourse.Component),
        ("seasoning", RecipeCourse.Component), ("rub", RecipeCourse.Component),
        ("stock", RecipeCourse.Component), ("broth", RecipeCourse.Component),
        ("filling", RecipeCourse.Component), ("topping", RecipeCourse.Component),
        ("garnish", RecipeCourse.Component), ("accompaniment", RecipeCourse.Component),
        ("preserve", RecipeCourse.Component), ("jam", RecipeCourse.Component),
        ("chutney", RecipeCourse.Component), ("pickle", RecipeCourse.Component),
        ("butter", RecipeCourse.Component), ("pastry", RecipeCourse.Component),
        ("appetizer", RecipeCourse.Component), ("appetiser", RecipeCourse.Component),
        ("starter", RecipeCourse.Component), ("snack", RecipeCourse.Component),
        // A meal, but a different one.
        ("dessert", RecipeCourse.OtherCourse), ("pudding", RecipeCourse.OtherCourse),
        ("cake", RecipeCourse.OtherCourse), ("cookie", RecipeCourse.OtherCourse),
        ("biscuit", RecipeCourse.OtherCourse), ("drink", RecipeCourse.OtherCourse),
        ("cocktail", RecipeCourse.OtherCourse), ("smoothie", RecipeCourse.OtherCourse),
        ("beverage", RecipeCourse.OtherCourse), ("bread", RecipeCourse.OtherCourse),
    ];

    private static readonly (string Needle, RecipeCourse Course)[] ByLongestNeedle =
        Categories.OrderByDescending(entry => entry.Needle.Length).ToArray();

    /// <summary>What the publisher said this is. Unknown when they said nothing useful.</summary>
    public static RecipeCourse Classify(string? recipeCategory)
    {
        var value = recipeCategory?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return RecipeCourse.Unknown;
        }

        foreach (var (needle, course) in ByLongestNeedle)
        {
            if (value.Contains(needle, StringComparison.Ordinal))
            {
                return course;
            }
        }

        return RecipeCourse.Unknown;
    }

    /// <summary>
    /// Does the customer's chosen meal type want a dish rather than a part of one?
    ///
    /// Somebody asking for dinner does not want a jar of spice rub. Somebody asking for a snack or a side
    /// does, so the distinction is only applied where it is the customer's own ask. An empty meal type is
    /// treated as wanting a meal, because that is what the app's main search is for.
    /// </summary>
    public static bool WantsAWholeDish(string? mealType)
    {
        var value = mealType?.Trim();
        if (string.IsNullOrEmpty(value) || value.Equals("Any", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Classify(value) switch
        {
            RecipeCourse.Component => false,
            RecipeCourse.OtherCourse => false,
            _ => true,
        };
    }
}
