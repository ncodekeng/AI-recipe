using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// The cases here are not invented. Every title is one the live search actually returned on
/// 23-24 November 2026, which is what made both faults visible in the first place.
/// </summary>
public sealed class RecipeDishConceptTests
{
    // ---- Listing pages offered as recipes -------------------------------------------------------

    [Theory]
    // Returned by a live brisket search, alongside three real recipes.
    [InlineData("41 Best Brisket Recipes & Ideas | Food Network")]
    [InlineData("Beef brisket recipes")]
    [InlineData("Beef brisket recipes - Jamie Oliver")]
    [InlineData("Beef brisket recipes - BBC Good Food")]
    // Shapes of the same thing.
    [InlineData("15 Best Chicken Recipes")]
    [InlineData("Our favourite chicken recipes")]
    [InlineData("Top 10 beef recipes")]
    [InlineData("Easy midweek recipe ideas")]
    [InlineData("Brisket recipe collection")]
    public void Rejects_a_page_that_lists_recipes_rather_than_being_one(string title)
    {
        Assert.True(
            RecipeDishConcept.IsListingPage(title, "https://www.example.test/recipes/some-slug-here"),
            $"\"{title}\" is a roundup; someone tapping it gets a page of links, not something to cook.");
    }

    [Theory]
    // Real recipes the same searches returned. These must survive.
    [InlineData("Mary Berry Beef Brisket Recipe")]
    [InlineData("Jamie Oliver Slow-Cooked Brisket Recipe")]
    [InlineData("Beef Brisket With Red Wine & Shallots")]
    [InlineData("Barbecue Beef Brisket")]
    [InlineData("One pot tomato basil chicken rice")]
    [InlineData("Old Fashioned Chicken and Rice")]
    [InlineData("Easy cheesy eggy bread")]
    public void Keeps_a_single_recipe(string title)
    {
        Assert.False(
            RecipeDishConcept.IsListingPage(title, "https://www.example.test/recipes/slow-roast-beef-brisket"),
            $"\"{title}\" is a real recipe and must not be discarded.");
    }

    [Theory]
    [InlineData("https://www.example.test/recipes")]            // the section root
    [InlineData("https://www.example.test/recipes/beef")]       // a category beneath it
    [InlineData("https://www.example.test/search?q=brisket")]
    [InlineData("https://www.example.test/recipes?q=brisket")]
    [InlineData("https://www.example.test/tag/brisket")]
    [InlineData("https://www.example.test/category/beef")]
    [InlineData("https://www.example.test/collections/slow-cooked")]
    public void Rejects_an_index_url_whatever_its_title_claims(string url)
    {
        Assert.True(
            RecipeDishConcept.IsListingPage("Slow-Roast Beef Brisket", url),
            $"{url} is an index path, so the page cannot be a single recipe however it is titled.");
    }

    [Theory]
    [InlineData("https://www.lanascooking.com/chicken-and-rice/")]
    [InlineData("https://www.example.test/recipes/slow-roast-beef-brisket")]
    [InlineData("https://www.example.test/dinner/beef/barbecue-brisket")]
    public void Keeps_a_url_that_names_a_dish(string url)
    {
        Assert.False(RecipeDishConcept.IsListingPage("Barbecue Beef Brisket", url));
    }

    // ---- One dinner from four publishers --------------------------------------------------------

    [Theory]
    // Exactly what a live search for chicken, rice and tomato returned: four results, one meal.
    [InlineData("Garlic and Tomato Chicken with Rice", "One pot tomato basil chicken rice")]
    [InlineData("Garlic and Tomato Chicken with Rice", "One Pot Chicken and Tomato Rice")]
    [InlineData("One pot tomato basil chicken rice", "One Pot Chicken and Tomato Rice")]
    public void Treats_publishers_versions_of_one_dinner_as_one_dish(string first, string second)
    {
        Assert.True(
            RecipeDishConcept.IsSameDish(first, second),
            $"\"{second}\" is the same dinner as \"{first}\" and should not fill a second slot.");
    }

    [Fact]
    public void Known_limit_a_near_duplicate_sharing_only_its_base_is_not_caught_here()
    {
        // The fourth result of that same search was "One Pot Italian Chicken and Rice". It is arguably the
        // same dinner again, but it shares only "chicken" and "rice" with the others -- "italian" against
        // "garlic"/"tomato" -- and scores 0.4. Catching it would mean a threshold low enough to merge
        // dishes that genuinely differ, trading duplication for lost variety, which is the worse failure.
        //
        // It is handled instead by excludedDishes in the search prompt: later batches are told which
        // dinners have already been found and asked for something different, so this pairing should not
        // arrive in the first place. Asserted so the limit is recorded rather than discovered later.
        Assert.False(RecipeDishConcept.IsSameDish(
            "Garlic and Tomato Chicken with Rice",
            "One Pot Italian Chicken and Rice"));
    }

    [Fact]
    public void Treats_three_write_ups_of_eggy_bread_as_one_dish()
    {
        Assert.True(RecipeDishConcept.IsSameDish("Bread with Egg and Cheese", "Cheesy Egg Bread Bake"));
        Assert.True(RecipeDishConcept.IsSameDish("Easy cheesy eggy bread", "Cheesy Egg Bread"));
    }

    [Theory]
    // Measured on 25 November 2026. Inspire Me returned the first pair as two of its four results, and a
    // search for vegetables returned the second. Each is one dinner written up at two lengths.
    [InlineData("Easy chicken curry", "Best Indian Chicken Curry With Yoghurt")]
    [InlineData("Aubergine parmigiana", "Aubergine Parmigiana (Melanzane alla Parmigiana)")]
    public void Treats_a_fuller_write_up_of_one_dinner_as_one_dish(string first, string second)
    {
        // Overlap scoring cannot see these. Every extra word the fuller title carries counts against the
        // score, so both pairs land on 0.50 against a 0.55 threshold and take two of the four slots.
        Assert.True(
            RecipeDishConcept.IsSameDish(first, second),
            $"\"{second}\" is \"{first}\" described more fully, so it should not fill a second slot.");
    }

    [Fact]
    public void A_title_reduced_to_one_ingredient_does_not_swallow_the_batch()
    {
        // The guard on the rule above. "Quick Baked Potatoes" reduces to the single word "potato", which
        // every potato dish contains; without the guard one bare title would absorb a whole batch.
        Assert.False(RecipeDishConcept.IsSameDish("Quick Baked Potatoes", "Easy Creamy Cheesy Potato Bake"));
        Assert.False(RecipeDishConcept.IsSameDish("Chicken", "Chicken and Rice"));
    }

    [Theory]
    // Genuinely different dinners from the same main ingredient. Merging these would hide real variety,
    // which is the opposite of the problem being fixed.
    [InlineData("Barbecue Beef Brisket", "Slow-Roast Beef Brisket")]
    [InlineData("Beef Brisket With Red Wine & Shallots", "Barbecue Beef Brisket")]
    [InlineData("Chicken Curry", "Chicken Caesar Salad")]
    [InlineData("Beef Stew", "Beef Tacos")]
    public void Keeps_different_dishes_apart(string first, string second)
    {
        Assert.False(
            RecipeDishConcept.IsSameDish(first, second),
            $"\"{first}\" and \"{second}\" are different dinners and both deserve a slot.");
    }

    [Fact]
    public void Ignores_the_publisher_credit_in_a_title()
    {
        // The suffix names the site, not the dish, so it must not make one dinner look like two.
        Assert.Equal(
            RecipeDishConcept.Normalize("Slow-Roast Beef Brisket"),
            RecipeDishConcept.Normalize("Slow-Roast Beef Brisket | BBC Good Food"));
        Assert.Equal(
            RecipeDishConcept.Normalize("Beef Brisket"),
            RecipeDishConcept.Normalize("Beef Brisket - Jamie Oliver"));
    }

    [Fact]
    public void Keeps_a_hyphenated_dish_whole()
    {
        // " - " with spaces is a publisher credit; a bare hyphen is part of the dish name and must not
        // truncate the title. "Slow" itself is filler, so what has to survive here is "lamb".
        Assert.Equal(
            RecipeDishConcept.Normalize("Slow Cooked Lamb"),
            RecipeDishConcept.Normalize("Slow-Cooked Lamb"));
        Assert.Contains("lamb", RecipeDishConcept.Normalize("Slow-Cooked Lamb"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Matches_the_spellings_one_ingredient_takes_across_titles()
    {
        // "cheese"/"cheesy" and "egg"/"eggy" appeared in three results of one live search.
        Assert.True(RecipeDishConcept.IsSameDish("Cheese and Egg Toast", "Cheesy Eggy Toast"));
        Assert.True(RecipeDishConcept.IsSameDish("Tomato Chicken Rice", "Tomatoes Chicken Rice"));
    }

    [Fact]
    public void A_cooking_method_named_in_only_one_title_means_a_different_dinner()
    {
        // The rule that overlap scoring alone cannot express: these differ by one word out of three and
        // are plainly two different meals.
        Assert.False(RecipeDishConcept.IsSameDish("Beef Brisket", "Barbecue Beef Brisket"));
        Assert.False(RecipeDishConcept.IsSameDish("Beef Brisket", "Slow-Roast Beef Brisket"));
        Assert.False(RecipeDishConcept.IsSameDish("Chicken Rice", "Chicken Rice Soup"));
    }

    [Fact]
    public void Says_nothing_rather_than_guessing_when_a_title_is_empty()
    {
        // Two uninformative titles must not be declared the same dish; that would silently drop recipes.
        Assert.False(RecipeDishConcept.IsSameDish("", ""));
        Assert.False(RecipeDishConcept.IsSameDish("Recipe", "Easy Recipe"));
        Assert.Equal(string.Empty, RecipeDishConcept.Normalize("The Best Easy Recipe"));
    }
}
