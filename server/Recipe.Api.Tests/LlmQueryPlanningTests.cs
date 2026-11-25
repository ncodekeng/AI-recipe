using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// Asking a model what to search for, under investigation against the deterministic planner.
///
/// The properties that have to hold whatever the benchmark concludes: allergies never reach a search box, a
/// model that cannot answer costs nobody a search, and nothing but search text comes back -- no recipe, no
/// URL, no browsing.
/// </summary>
public sealed class LlmQueryPlanningTests
{
    private readonly IngredientNormalizer _normalizer = new();

    [Fact]
    public async Task An_allergy_is_never_sent_to_the_model()
    {
        // "no peanuts" in a search box finds peanut recipes, and a model handed an allergy would write
        // exactly that query. FoodSafetyRules enforces allergies against the real ingredient list after
        // extraction, which is the only place the claim can honestly be made.
        var handler = new CaptureHandler(Answer("satay chicken recipe", "chicken curry recipe"));
        var request = Request() with
        {
            Allergens = ["peanuts", "shellfish"],
            AvoidIngredients = ["coriander"],
        };

        await Planner(handler).PlanAsync(request, 4, CancellationToken.None);

        Assert.DoesNotContain("peanut", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shellfish", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allerg", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_model_is_given_no_tools_and_asked_for_no_recipe()
    {
        var handler = new CaptureHandler(Answer("beef tacos recipe"));

        await Planner(handler).PlanAsync(Request(), 4, CancellationToken.None);

        // No browsing, and nothing that would make it write a recipe.
        Assert.DoesNotContain("web_search", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"tools\"", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ingredient amounts", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceUrl", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_basket_and_the_preferences_do_reach_the_model()
    {
        var handler = new CaptureHandler(Answer("beef tacos recipe"));
        var request = Request() with
        {
            CuisinePreference = "Mexican",
            MealType = "Dinner",
            CustomInstructions = "something the children will eat",
            MaxCookingMinutes = 30,
        };

        await Planner(handler).PlanAsync(request, 4, CancellationToken.None);

        Assert.Contains("Taco bread", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mexican", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dinner", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("children will eat", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_model_that_cannot_answer_costs_nobody_a_search(HttpStatusCode status)
    {
        // The deterministic plan is always computed first, so a failure here makes a search ordinary rather
        // than failed. Returning empty is how the caller knows to keep what it has.
        var handler = new CaptureHandler("upstream trouble", status);

        var queries = await Planner(handler).PlanAsync(Request(), 4, CancellationToken.None);

        Assert.Empty(queries);
    }

    [Fact]
    public async Task An_unreadable_answer_also_costs_nothing()
    {
        var handler = new CaptureHandler(Raw("I'm sorry, I can't help with that."));

        Assert.Empty(await Planner(handler).PlanAsync(Request(), 4, CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_is_asked_when_no_model_is_configured()
    {
        var handler = new CaptureHandler(Answer("beef tacos recipe"));
        var planner = new LlmQueryPlanner(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new FoodAiOptions()),
            NullLogger<LlmQueryPlanner>.Instance);

        Assert.Empty(await planner.PlanAsync(Request(), 4, CancellationToken.None));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Search_operators_and_quoting_are_stripped_out()
    {
        // Asked for plain search text, a model still sometimes returns a quoted phrase or an operator,
        // neither of which helps an image search.
        var handler = new CaptureHandler(Answer(
            "\"beef tacos\" recipe",
            "site:bbcgoodfood.com beef fajitas",
            "beef quesadillas OR beef burritos",
            "   beef   enchiladas   recipe   "));

        var queries = await Planner(handler).PlanAsync(Request(), 6, CancellationToken.None);

        Assert.All(queries, query =>
        {
            Assert.DoesNotContain("\"", query, StringComparison.Ordinal);
            Assert.DoesNotContain("site:", query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("  ", query, StringComparison.Ordinal);
        });
        Assert.Contains("beef enchiladas recipe", queries);
    }

    [Fact]
    public async Task The_number_of_queries_stays_bounded()
    {
        var handler = new CaptureHandler(Answer(
            "one recipe", "two recipe", "three recipe", "four recipe",
            "five recipe", "six recipe", "seven recipe", "eight recipe", "nine recipe"));

        Assert.Equal(4, (await Planner(handler).PlanAsync(Request(), 4, CancellationToken.None)).Count);
    }

    // ---- Which baskets are worth asking about ------------------------------------------------------

    [Fact]
    public void A_straightforward_basket_needs_no_model()
    {
        // Mexican with tortillas: the dish table already knows this means tacos, instantly and for nothing.
        var decision = Choose(Request() with { CuisinePreference = "Mexican" });

        Assert.False(decision.AskTheModel);
    }

    [Fact]
    public void The_customers_own_words_are_worth_asking_about()
    {
        var decision = Choose(Request() with
        {
            CuisinePreference = "Mexican",
            CustomInstructions = "something warming for a cold night",
        });

        Assert.True(decision.AskTheModel);
        Assert.Equal("custom instructions", decision.Reason);
    }

    [Fact]
    public void An_ingredient_the_dictionary_has_never_heard_of_is_worth_asking_about()
    {
        var decision = Choose(Request() with
        {
            CuisinePreference = "Mexican",
            Ingredients = [new IngredientInput("Beef", "500 g"), new IngredientInput("Nopales", "2 cups")],
        });

        Assert.True(decision.AskTheModel);
        Assert.Equal("unfamiliar ingredient", decision.Reason);
    }

    [Fact]
    public void A_complex_combination_is_worth_asking_about()
    {
        var decision = Choose(Request() with
        {
            CuisinePreference = "Mexican",
            Ingredients =
            [
                new IngredientInput("Beef", "500 g"),
                new IngredientInput("Tortillas", "8"),
                new IngredientInput("Peppers", "3"),
                new IngredientInput("Rice", "300 g"),
                new IngredientInput("Black beans", "1 tin"),
            ],
        });

        Assert.True(decision.AskTheModel);
        Assert.Equal("complex combination", decision.Reason);
    }

    [Fact]
    public void A_plan_that_names_no_dish_is_worth_asking_about()
    {
        // The thin coverage the client's own search suffered from: no cuisine, nothing matched, so the plan
        // fell back to generic forms.
        var decision = QueryPlanChoice.For(
            Request() with { CuisinePreference = string.Empty },
            _normalizer,
            ["beef tortilla pepper recipe"],
            complexBasketSize: 5);

        Assert.True(decision.AskTheModel);
        Assert.Equal("thin deterministic coverage", decision.Reason);
    }

    private QueryPlanChoice.Decision Choose(GenerateRecipesRequest request)
    {
        var focus = _normalizer.SelectRecipeSearchFocus(request.Ingredients, 3, mainIngredient: request.MainIngredient);
        var queries = RecipeSearchPlanner.Plan(request, 4, 0, focus, _normalizer);
        return QueryPlanChoice.For(request, _normalizer, queries, complexBasketSize: 5);
    }

    private static GenerateRecipesRequest Request() => new()
    {
        Ingredients =
        [
            new IngredientInput("Beef", "500 g"),
            new IngredientInput("Taco bread", "8"),
            new IngredientInput("Peppers", "3"),
        ],
        DietaryPreference = "Anything",
        MaxRecipes = 4,
        Servings = 4,
    };

    private static LlmQueryPlanner Planner(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new FoodAiOptions
            {
                AzureOpenAI = new AzureOpenAiOptions
                {
                    Endpoint = "https://example-openai.openai.azure.com/",
                    ApiKey = "test-key",
                    Deployment = "gpt-4.1-mini",
                },
                QueryPlanning = new QueryPlanningOptions { Mode = "Llm" },
            }),
            NullLogger<LlmQueryPlanner>.Instance);

    private static string Answer(params string[] queries) =>
        Raw(JsonSerializer.Serialize(new { queries }));

    /// <summary>The Azure Responses API shape, which is where the text arrives.</summary>
    private static string Raw(string text) =>
        JsonSerializer.Serialize(new
        {
            output = new[] { new { content = new[] { new { type = "output_text", text } } } },
        });

    private sealed class CaptureHandler(string payload, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }
}
