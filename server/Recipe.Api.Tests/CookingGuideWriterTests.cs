using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

/// <summary>
/// The guide is the one part of a recipe PLATE writes itself. Everything around it -- the title, the
/// ingredients, the timings, the yield -- is stated by the publisher and read from their own markup, and the
/// publisher's method is theirs and is never copied.
///
/// So the property that matters most here is not that a guide gets written. It is that a recipe survives when
/// one cannot be: the facts are already real, and a dish with ingredients, times and a photo is worth showing
/// without a method. Losing four verified recipes because one model call failed would be the worse bug.
/// </summary>
public sealed class CookingGuideWriterTests
{
    [Fact]
    public async Task Writes_a_guide_for_a_dish_that_has_none()
    {
        var handler = new CaptureHandler(ModelResponse(Guides((0, [
            "Heat the oven to 160C.",
            "Brown the brisket on all sides in a hot casserole, about 8 minutes.",
            "Add the ale and stock, cover, and cook for 4 hours until the meat pulls apart.",
            "Rest for 15 minutes, then slice against the grain and serve.",
        ]))));

        var result = await Writer(handler).AddCookingGuidesAsync(Batch(Dish("Slow cooker brisket")), default);

        var recipe = result.Recipes[0];
        Assert.Equal(4, recipe.Steps.Count);
        Assert.StartsWith("Heat the oven", recipe.Steps[0], StringComparison.Ordinal);
        // The label the customer sees depends on this exact value: RecipeDetail.jsx shows "AI cooking guide"
        // beside a link to the source, rather than "Method", so the method is never passed off as theirs.
        Assert.Equal(RecipeDirectionsKinds.AiGenerated, recipe.DirectionsKind);
    }

    [Fact]
    public async Task A_failed_call_returns_every_recipe_without_a_guide()
    {
        // The whole reason the guide is a separate step. Four verified recipes must not be lost to one
        // failed model call.
        var handler = new CaptureHandler("service unavailable", HttpStatusCode.ServiceUnavailable);
        var batch = Batch(Dish("Brisket"), Dish("Ragu"), Dish("Pie"), Dish("Tacos"));

        var result = await Writer(handler).AddCookingGuidesAsync(batch, default);

        Assert.Equal(4, result.Recipes.Count);
        Assert.All(result.Recipes, recipe =>
        {
            Assert.Empty(recipe.Steps);
            Assert.Equal(RecipeDirectionsKinds.Unavailable, recipe.DirectionsKind);
        });
        // Still the publisher's facts, untouched.
        Assert.All(result.Recipes, recipe => Assert.NotEmpty(recipe.Ingredients));
    }

    [Fact]
    public async Task One_call_covers_the_whole_batch()
    {
        // Four separate calls would cost four times the latency for the same work, which is the mistake the
        // old pipeline made by generating each batch in sequence.
        var handler = new CaptureHandler(ModelResponse(Guides(
            (0, Steps()), (1, Steps()), (2, Steps()), (3, Steps()))));

        var result = await Writer(handler).AddCookingGuidesAsync(
            Batch(Dish("Brisket"), Dish("Ragu"), Dish("Pie"), Dish("Tacos")),
            default);

        Assert.Equal(1, handler.CallCount);
        Assert.All(result.Recipes, recipe => Assert.Equal(RecipeDirectionsKinds.AiGenerated, recipe.DirectionsKind));
    }

    [Fact]
    public async Task Keeps_a_method_that_came_with_the_recipe()
    {
        // Edamam and the grounded path can both supply a method. Overwriting one with ours would throw away
        // better data and pay a model for the privilege.
        var handler = new CaptureHandler(ModelResponse(Guides((0, Steps()))));
        var provided = Dish("Brisket") with
        {
            Steps = ["The publisher's own step."],
            DirectionsKind = RecipeDirectionsKinds.Provider,
        };

        var result = await Writer(handler).AddCookingGuidesAsync(Batch(provided), default);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(["The publisher's own step."], result.Recipes[0].Steps);
        Assert.Equal(RecipeDirectionsKinds.Provider, result.Recipes[0].DirectionsKind);
    }

    [Fact]
    public async Task A_dish_the_model_skipped_is_still_returned()
    {
        // A partial answer is worth keeping. The dish that got a guide gets one; the other is returned as it
        // was rather than dropped or given someone else's steps.
        var handler = new CaptureHandler(ModelResponse(Guides((1, Steps()))));

        var result = await Writer(handler).AddCookingGuidesAsync(
            Batch(Dish("Brisket"), Dish("Ragu")),
            default);

        Assert.Empty(result.Recipes[0].Steps);
        Assert.Equal(RecipeDirectionsKinds.Unavailable, result.Recipes[0].DirectionsKind);
        Assert.NotEmpty(result.Recipes[1].Steps);
        Assert.Equal(RecipeDirectionsKinds.AiGenerated, result.Recipes[1].DirectionsKind);
        // The guide went to the dish it was written for, not to the first one in the list.
        Assert.Equal("Ragu", result.Recipes[1].Title);
    }

    [Fact]
    public async Task A_guide_too_short_to_cook_from_is_refused()
    {
        // No guide reads as "the method is on the publisher's page". A one-line guide reads as a broken app.
        var handler = new CaptureHandler(ModelResponse(Guides((0, ["Cook it."]))));

        var result = await Writer(handler).AddCookingGuidesAsync(Batch(Dish("Brisket")), default);

        Assert.Empty(result.Recipes[0].Steps);
        Assert.Equal(RecipeDirectionsKinds.Unavailable, result.Recipes[0].DirectionsKind);
    }

    [Fact]
    public async Task Steps_the_model_numbered_are_not_numbered_twice()
    {
        // The app renders these in an ordered list, so a "1." left in the text shows up as "1. 1. Heat...".
        var handler = new CaptureHandler(ModelResponse(Guides((0, [
            "1. Heat the oven to 160C.",
            "2) Brown the brisket for 8 minutes.",
            "Step 3: Cook for 4 hours until it pulls apart.",
            "200g of the flour is dusted over at the end.",
        ]))));

        var steps = (await Writer(handler).AddCookingGuidesAsync(Batch(Dish("Brisket")), default)).Recipes[0].Steps;

        Assert.Equal("Heat the oven to 160C.", steps[0]);
        Assert.Equal("Brown the brisket for 8 minutes.", steps[1]);
        Assert.Equal("Cook for 4 hours until it pulls apart.", steps[2]);
        // A step that genuinely opens with a quantity keeps it: nothing separates the digits from the word.
        Assert.Equal("200g of the flour is dusted over at the end.", steps[3]);
    }

    [Fact]
    public async Task The_publishers_page_is_never_sent_to_the_model()
    {
        // Nothing to fetch and nothing to copy from. The model is given the facts already read and is asked
        // to write for that dish, so it cannot be led into reproducing the publisher's method.
        var handler = new CaptureHandler(ModelResponse(Guides((0, Steps()))));
        var dish = Dish("Brisket") with { SourceUrl = "https://www.bbcgoodfood.com/recipes/slow-cooker-brisket" };

        await Writer(handler).AddCookingGuidesAsync(Batch(dish), default);

        Assert.DoesNotContain("bbcgoodfood", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", handler.Body, StringComparison.OrdinalIgnoreCase);
        // What it is given instead: the dish and the ingredient lines the publisher stated.
        Assert.Contains("Brisket", handler.Body, StringComparison.Ordinal);
        Assert.Contains("1.5kg beef brisket", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_asked_when_no_model_is_configured()
    {
        var handler = new CaptureHandler(ModelResponse(Guides((0, Steps()))));
        var writer = new CookingGuideWriter(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new FoodAiOptions()),
            NullLogger<CookingGuideWriter>.Instance);

        var result = await writer.AddCookingGuidesAsync(Batch(Dish("Brisket")), default);

        Assert.Equal(0, handler.CallCount);
        Assert.Empty(result.Recipes[0].Steps);
    }

    [Fact]
    public async Task An_unreadable_answer_costs_the_guide_and_nothing_else()
    {
        var handler = new CaptureHandler(ModelResponse("Sorry, I cannot help with that."));

        var result = await Writer(handler).AddCookingGuidesAsync(Batch(Dish("Brisket")), default);

        Assert.Single(result.Recipes);
        Assert.Empty(result.Recipes[0].Steps);
        Assert.Equal(RecipeDirectionsKinds.Unavailable, result.Recipes[0].DirectionsKind);
    }

    private static CookingGuideWriter Writer(HttpMessageHandler handler) =>
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
            }),
            NullLogger<CookingGuideWriter>.Instance);

    private static RecipeGenerationResponse Batch(params RecipeSuggestion[] recipes) =>
        new(recipes, "Google Search", "notice");

    private static RecipeSuggestion Dish(string title) => new(
        Guid.NewGuid(),
        title,
        "Published by example.com.",
        240,
        "Medium",
        "British",
        4,
        0,
        [],
        [new RecipeIngredient("1.5kg", "beef brisket", 1.5, "kg", "1.5kg beef brisket")],
        [],
        "amber",
        SourceVerified: true,
        PublisherPageVerified: true,
        DirectionsKind: RecipeDirectionsKinds.Unavailable);

    private static string[] Steps() =>
    [
        "Heat the oven to 160C.",
        "Brown the meat for 8 minutes.",
        "Cover and cook for 4 hours.",
        "Rest, slice and serve.",
    ];

    private static string Guides(params (int Id, string[] Steps)[] guides) =>
        JsonSerializer.Serialize(new
        {
            guides = guides.Select(guide => new { id = guide.Id, steps = guide.Steps }),
        });

    /// <summary>The Azure Responses API shape, which is where the guide text arrives.</summary>
    private static string ModelResponse(string text) =>
        JsonSerializer.Serialize(new
        {
            output = new[]
            {
                new { content = new[] { new { type = "output_text", text } } },
            },
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
