using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// The concept table decides whether a recipe counts as containing what the customer asked for.
/// RecipeRankingService drops any recipe whose main ingredient it cannot find, so a word missing from here
/// does not degrade a result -- it deletes one, and the customer is shown three recipes instead of four
/// with nothing to explain the gap.
///
/// Both gaps below were found that way, in live searches, and each cost a real result:
///   - a brisket search found four brisket recipes and returned three, because "brisket" was not a cut
///     of beef as far as this table was concerned (24 November 2026);
///   - a veg search returned two recipes instead of four, one loss being "Spanish Eggplant (Aubergine)",
///     whose own title says aubergine but whose ingredient line says eggplant (25 November 2026).
/// </summary>
public sealed class IngredientNormalizerTests
{
    private readonly IngredientNormalizer _normalizer = new();

    [Theory]
    // A cut is the thing itself as far as a shopping trip is concerned, which is why "steak" was always
    // here. The rest were not, and a brisket search paid for it.
    [InlineData("beef brisket", "brisket")]
    [InlineData("beef", "beef brisket")]
    [InlineData("beef", "1 kg beef brisket, trimmed")]
    [InlineData("beef", "braising steak")]
    [InlineData("beef", "500g stewing beef")]
    [InlineData("beef", "minced beef")]
    // The naming split: en-GB customers, global publishers.
    [InlineData("aubergine", "eggplant")]
    [InlineData("aubergine", "2 large eggplants, cubed")]
    [InlineData("courgette", "zucchini")]
    [InlineData("rocket", "arugula")]
    [InlineData("chickpea", "garbanzo beans")]
    [InlineData("beetroot", "beets")]
    [InlineData("swede", "rutabaga")]
    [InlineData("double cream", "heavy cream")]
    [InlineData("plain flour", "all purpose flour")]
    public void Recognises_one_ingredient_under_either_name(string pantry, string required)
    {
        Assert.True(
            _normalizer.Matches(pantry, required),
            $"\"{required}\" is \"{pantry}\", and a recipe calling for it must not be dropped.");
    }

    [Theory]
    // The table has to stay sharp as well as broad. Each of these pairs would cost real variety if it
    // collapsed: a customer holding one of them cannot cook the other.
    [InlineData("beef", "chicken")]
    [InlineData("aubergine", "courgette")]
    [InlineData("chicken", "chicken stock")]
    [InlineData("beef", "beef stock")]
    [InlineData("coriander", "coriander seed")]
    public void Keeps_ingredients_that_are_not_the_same_thing_apart(string first, string second)
    {
        Assert.False(
            _normalizer.Matches(first, second),
            $"\"{first}\" and \"{second}\" are different ingredients.");
    }

    [Theory]
    // The collision that broke the client's taco search, and the family it belongs to. Each of these used
    // to be swallowed by a shorter, more generic alias listed higher in the table.
    [InlineData("Taco bread", "tortilla")]
    [InlineData("8 soft tortillas", "tortilla")]
    [InlineData("12 taco shells", "tortilla")]
    [InlineData("corn tortillas", "tortilla")]
    [InlineData("2 sweet potatoes, cubed", "sweet potato")]
    [InlineData("200g rice noodles", "rice noodle")]
    [InlineData("2 tbsp rice vinegar", "rice vinegar")]
    [InlineData("1 tsp garlic powder", "garlic powder")]
    [InlineData("1 tsp onion powder", "onion powder")]
    [InlineData("400ml coconut milk", "coconut milk")]
    [InlineData("2 tbsp peanut butter", "peanut butter")]
    [InlineData("2 tuna steaks", "fish")]
    // And the ones that already worked, asserted so the new ordering cannot quietly undo them.
    [InlineData("freshly ground black pepper", "black pepper")]
    [InlineData("3 red bell peppers", "bell pepper")]
    [InlineData("500ml chicken stock", "chicken stock")]
    [InlineData("6 boneless skinless chicken breasts", "chicken")]
    [InlineData("bunch of spring onions", "spring onion")]
    [InlineData("200g cream cheese", "cream cheese")]
    [InlineData("4 baby potatoes", "potato")]
    public void Resolves_an_ingredient_to_its_most_specific_concept(string line, string expected)
    {
        Assert.Equal(expected, _normalizer.Normalize(line));
    }

    [Theory]
    // The point of the fix: a tortilla is not bread, and a sweet potato is not a potato. A recipe calling
    // for one must never count as a match for the other, in either direction.
    [InlineData("taco bread", "bread")]
    [InlineData("tortillas", "sourdough bread")]
    [InlineData("sweet potato", "potatoes")]
    [InlineData("rice noodles", "basmati rice")]
    [InlineData("rice vinegar", "rice")]
    [InlineData("coconut milk", "whole milk")]
    [InlineData("garlic powder", "garlic cloves")]
    public void Keeps_a_specific_ingredient_apart_from_the_generic_one(string first, string second)
    {
        Assert.False(_normalizer.Matches(first, second), $"\"{first}\" is not \"{second}\"");
        Assert.False(_normalizer.Matches(second, first), $"\"{second}\" is not \"{first}\"");
    }

    [Fact]
    public void The_table_order_no_longer_decides_the_answer()
    {
        // The mechanism, stated directly. "bread" sits above anything describing a tortilla in the table,
        // and "pepper" is an alias of bell pepper while "black pepper" is its own concept further up. Both
        // resolve by how much the alias says, not by where it appears.
        Assert.Equal("tortilla", _normalizer.Normalize("taco bread"));
        Assert.Equal("bread", _normalizer.Normalize("crusty bread"));
        Assert.Equal("black pepper", _normalizer.Normalize("black pepper"));
        Assert.Equal("bell pepper", _normalizer.Normalize("peppers"));
    }

    [Fact]
    public void Fresh_coriander_and_the_ground_seed_stay_separate()
    {
        // Ordering inside the table, not a coincidence: the seed is matched before the herb, so a spice
        // is never collapsed into a bunch of leaves. UK titles call the herb plain "coriander".
        Assert.Equal("coriander seed", _normalizer.Normalize("1 tsp ground coriander"));
        Assert.Equal("coriander", _normalizer.Normalize("small bunch fresh coriander, chopped"));
        Assert.Equal("coriander", _normalizer.Normalize("cilantro"));
    }

    [Fact]
    public void Collapses_a_whole_ingredient_line_to_the_thing_being_bought()
    {
        // What the table is for: quantities, packaging and preparation are not part of the ingredient.
        Assert.Equal("beef", _normalizer.Normalize("1.5kg beef brisket, trimmed of excess fat"));
        Assert.Equal("aubergine", _normalizer.Normalize("2 aubergines (~600g - chopped in half lengthways)"));
        Assert.Equal("chicken", _normalizer.Normalize("6 boneless skinless chicken thighs"));
    }

    [Fact]
    public void An_unknown_ingredient_is_still_matched_on_its_own_words()
    {
        // Nothing can list every ingredient. A word the table has never heard of still has to match
        // itself, singular or plural, or an unfamiliar basket would lose every recipe.
        Assert.True(_normalizer.Matches("samphire", "samphire"));
        Assert.True(_normalizer.Matches("plantain", "2 ripe plantains"));
        Assert.False(_normalizer.Matches("samphire", "plantain"));
    }
}
