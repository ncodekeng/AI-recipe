using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class AzureGroundedRecipeClientTests
{
    [Fact]
    public async Task Uses_web_search_and_maps_only_a_cited_recipe()
    {
        const string sourceUrl = "https://publisher.example.test/lamb-stew";
        var handler = new CapturingHandler(Response(
            sourceUrl,
            sourceUrl,
            "Cotes du Rhone"));
        var client = CreateClient(handler);

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.Equal("Azure Web Search", response.Provider);
        Assert.Equal(sourceUrl, recipe.SourceUrl);
        Assert.Equal("publisher.example.test", recipe.SourceName);
        Assert.Equal("Cotes du Rhone", recipe.WinePairing);
        Assert.Equal("Real recipe from publisher.example.test.", recipe.Description);
        Assert.Null(recipe.ImageUrl);
        Assert.Equal(RecipeDirectionsKinds.AiGenerated, recipe.DirectionsKind);
        Assert.Equal(2, recipe.Steps.Count);
        Assert.StartsWith("Brown the lamb", recipe.Steps[0], StringComparison.Ordinal);

        Assert.Equal("https://test.openai.azure.com/openai/v1/responses", handler.RequestUri?.ToString());
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Contains("\"type\":\"web_search\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"country\":\"GB\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"tool_choice\":\"required\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"parallel_tool_calls\":false", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("web_search_call.action.sources", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"store\":false", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("lamb", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cookingGuideSteps", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("not publisher instructions", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traditional 1-to-3-missing match", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one small batch", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"description\"", handler.RequestBody, StringComparison.Ordinal);
        using var requestDocument = JsonDocument.Parse(handler.RequestBody);
        using var inputDocument = JsonDocument.Parse(requestDocument.RootElement.GetProperty("input").GetString()!);
        Assert.Equal(3, inputDocument.RootElement.GetProperty("desiredCandidateCount").GetInt32());
        Assert.Equal(3, inputDocument.RootElement.GetProperty("minimumCandidateCount").GetInt32());
        Assert.False(requestDocument.RootElement.TryGetProperty("text", out _));
        Assert.False(requestDocument.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task Rejects_a_recipe_url_not_returned_by_web_search()
    {
        var handler = new CapturingHandler(Response(
            "https://publisher.example.test/cited",
            "https://invented.example.test/not-cited",
            string.Empty));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<RecipeSafetyException>(() =>
            client.FindRecipesAsync(Request(), CancellationToken.None));

        Assert.Contains("verifiable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suppresses_wine_pairing_for_halal_style_requests()
    {
        const string sourceUrl = "https://publisher.example.test/halal-lamb";
        var handler = new CapturingHandler(Response(
            sourceUrl,
            sourceUrl,
            "Syrah"));
        var client = CreateClient(handler);
        var request = Request("Halal-style");

        var response = await client.FindRecipesAsync(request, CancellationToken.None);

        Assert.Null(Assert.Single(response.Recipes).WinePairing);
        Assert.Contains("do not search for a wine pairing", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Supplies_a_rough_fallback_pairing_when_azure_omits_one()
    {
        const string sourceUrl = "https://publisher.example.test/lamb-without-pairing";
        var client = CreateClient(new CapturingHandler(Response(
            sourceUrl,
            sourceUrl,
            string.Empty)));

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Contains("Merlot", Assert.Single(response.Recipes).WinePairing, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suppresses_wine_pairing_for_a_sulphite_allergy()
    {
        const string sourceUrl = "https://publisher.example.test/sulphite-safe-lamb";
        var client = CreateClient(new CapturingHandler(Response(sourceUrl, sourceUrl, "Syrah")));
        var request = Request();
        request.Allergens.Add("Sulphites");

        var response = await client.FindRecipesAsync(request, CancellationToken.None);

        Assert.Null(Assert.Single(response.Recipes).WinePairing);
    }

    [Fact]
    public async Task Preserves_the_traditional_tag_for_deterministic_ranking()
    {
        const string sourceUrl = "https://publisher.example.test/traditional-stew";
        var client = CreateClient(new CapturingHandler(Response(
            sourceUrl,
            sourceUrl,
            string.Empty,
            tags: ["Dinner", "British", "Family", "Traditional"])));

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Equal("Traditional", Assert.Single(response.Recipes).Tags[0]);
    }

    [Fact]
    public async Task Reads_json_wrapped_in_explanatory_text_without_another_search()
    {
        const string sourceUrl = "https://publisher.example.test/wrapped-recipe";
        var handler = new CapturingHandler(Response(
            sourceUrl,
            sourceUrl,
            string.Empty,
            wrapInText: true));
        var client = CreateClient(handler);

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Single(response.Recipes);
        Assert.Single(handler.RequestBodies);
    }

    [Fact]
    public async Task Retries_in_plain_text_mode_when_azure_returns_only_prose()
    {
        const string sourceUrl = "https://publisher.example.test/retry-recipe";
        var handler = new CapturingHandler(
            TextResponse(sourceUrl, "I found several possible recipes, but returned prose."),
            Response(sourceUrl, sourceUrl, string.Empty));
        var client = CreateClient(handler);

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Single(response.Recipes);
        Assert.Equal(2, handler.RequestBodies.Count);
        foreach (var requestBody in handler.RequestBodies)
        {
            using var requestDocument = JsonDocument.Parse(requestBody);
            Assert.False(requestDocument.RootElement.TryGetProperty("text", out _));
            Assert.False(requestDocument.RootElement.TryGetProperty("response_format", out _));
        }
        Assert.Contains("previous response could not be parsed", handler.RequestBodies[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_a_clear_error_when_both_azure_attempts_are_prose()
    {
        const string sourceUrl = "https://publisher.example.test/unreadable";
        var handler = new CapturingHandler(
            TextResponse(sourceUrl, "I returned prose on the first attempt."),
            TextResponse(sourceUrl, "I returned prose on the second attempt."));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<RecipeSafetyException>(() =>
            client.FindRecipesAsync(Request(), CancellationToken.None));

        Assert.Contains("readable recipe data", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tries_a_fresh_batch_when_the_first_batch_is_unreadable()
    {
        const string sourceUrl = "https://publisher.example.test/recovered-recipe";
        var handler = new CapturingHandler(
            TextResponse(sourceUrl, "The first response was prose."),
            TextResponse(sourceUrl, "The JSON-only retry was also prose."),
            Response(sourceUrl, sourceUrl, string.Empty));
        var client = CreateClient(handler, minimumResultCount: 1, maxSearchAttempts: 2);

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Single(response.Recipes);
        Assert.Equal(3, handler.RequestBodies.Count);
        using var requestDocument = JsonDocument.Parse(handler.RequestBodies[2]);
        using var inputDocument = JsonDocument.Parse(requestDocument.RootElement.GetProperty("input").GetString()!);
        Assert.Equal(2, inputDocument.RootElement.GetProperty("batchNumber").GetInt32());
    }

    [Fact]
    public async Task Keeps_valid_first_batch_when_an_additional_batch_is_unreadable()
    {
        const string firstUrl = "https://publisher.example.test/first-valid-recipe";
        const string failedUrl = "https://publisher.example.test/unreadable-follow-up";
        var handler = new CapturingHandler(
            Response(firstUrl, firstUrl, string.Empty),
            TextResponse(failedUrl, "The additional response was prose."),
            TextResponse(failedUrl, "The additional JSON-only retry was also prose."));
        var client = CreateClient(handler, minimumResultCount: 2, maxSearchAttempts: 2);

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Single(response.Recipes);
        Assert.Equal(firstUrl, response.Recipes[0].SourceUrl);
        Assert.Contains("kept the validated recipes", response.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task Accepts_complete_json_with_trailing_commas()
    {
        const string sourceUrl = "https://publisher.example.test/trailing-comma-recipe";
        var validResponse = Response(sourceUrl, sourceUrl, string.Empty);
        using var responseDocument = JsonDocument.Parse(validResponse);
        var outputText = responseDocument.RootElement
            .GetProperty("output")[1]
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;
        var trailingCommaJson = outputText[..^2] + ",],}";
        var client = CreateClient(new CapturingHandler(TextResponse(sourceUrl, trailingCommaJson)));

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Single(response.Recipes);
    }

    [Fact]
    public async Task Searches_again_when_the_first_batch_has_too_few_distinct_recipes()
    {
        const string firstUrl = "https://publisher.example.test/first-recipe";
        const string secondUrl = "https://publisher.example.test/second-recipe";
        var handler = new CapturingHandler(
            Response(firstUrl, firstUrl, string.Empty),
            Response(secondUrl, secondUrl, string.Empty));
        var client = CreateClient(handler, minimumResultCount: 2, maxSearchAttempts: 2);

        var response = await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Equal(2, response.Recipes.Count);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("excludedSourceUrls", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Contains(firstUrl, handler.RequestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Includes_admin_recipe_guidance_without_replacing_locked_source_rules()
    {
        const string sourceUrl = "https://publisher.example.test/custom-guidance";
        const string customPrompt = "Prefer colourful one-pan family meals when the pantry supports them.";
        var handler = new CapturingHandler(Response(sourceUrl, sourceUrl, string.Empty));
        var client = CreateClient(
            handler,
            prompts: new TestPromptProvider(recipeRecommendationPrompt: customPrompt));

        await client.FindRecipesAsync(Request(), CancellationToken.None);

        Assert.Contains(customPrompt, handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("cannot override mandatory", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never invent a URL", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cook_with_what_I_have_requires_zero_missing_non_staple_ingredients()
    {
        const string sourceUrl = "https://publisher.example.test/pantry-only";
        var handler = new CapturingHandler(Response(sourceUrl, sourceUrl, "Pinot Noir"));
        var client = CreateClient(handler);

        await client.FindRecipesAsync(
            Request(onlyUseAvailableIngredients: true),
            CancellationToken.None);

        Assert.Contains("Cook with what I have mode", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no missing non-staple ingredients", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        using var requestDocument = JsonDocument.Parse(handler.RequestBody);
        using var inputDocument = JsonDocument.Parse(requestDocument.RootElement.GetProperty("input").GetString()!);
        Assert.True(inputDocument.RootElement.GetProperty("onlyUseAvailableIngredients").GetBoolean());
    }

    [Fact]
    public async Task Cook_with_what_I_have_retries_when_the_first_cited_recipe_needs_an_extra_ingredient()
    {
        const string nearMatchUrl = "https://publisher.example.test/eggs-with-garlic";
        const string exactMatchUrl = "https://publisher.example.test/simple-scrambled-eggs";
        var handler = new CapturingHandler(
            ResponseWithIngredients(nearMatchUrl, "Eggs with garlic", "eggs", "garlic"),
            ResponseWithIngredients(exactMatchUrl, "Simple scrambled eggs", "eggs", "unsalted butter", "whole milk"));
        var client = CreateClient(handler, minimumResultCount: 1, maxSearchAttempts: 2);
        var request = KitchenMemoryRequest();

        var response = await client.FindRecipesAsync(request, CancellationToken.None);

        var recipe = Assert.Single(response.Recipes);
        Assert.Equal(exactMatchUrl, recipe.SourceUrl);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains(nearMatchUrl, handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Contains("different subset", handler.RequestBodies[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_input_canonicalizes_scanned_container_names()
    {
        const string sourceUrl = "https://publisher.example.test/simple-scrambled-eggs";
        var handler = new CapturingHandler(
            ResponseWithIngredients(sourceUrl, "Simple scrambled eggs", "eggs", "unsalted butter", "whole milk"));
        var client = CreateClient(handler);

        await client.FindRecipesAsync(KitchenMemoryRequest(), CancellationToken.None);

        using var requestDocument = JsonDocument.Parse(handler.RequestBody);
        using var inputDocument = JsonDocument.Parse(requestDocument.RootElement.GetProperty("input").GetString()!);
        var availableNames = inputDocument.RootElement
            .GetProperty("availableIngredientNames")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        Assert.Contains("egg", availableNames);
        Assert.Contains("butter", availableNames);
        Assert.Contains("milk", availableNames);
        Assert.Contains("yogurt", availableNames);
        Assert.Contains("mayonnaise", availableNames);
        Assert.Contains("cream cheese", availableNames);
        Assert.Contains("soy sauce", availableNames);
        Assert.DoesNotContain("yogurt tub", availableNames);
        Assert.Equal(
            ["salt", "black pepper", "water", "olive oil", "cooking oil"],
            inputDocument.RootElement.GetProperty("allowedPantryStaples").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Show_all_searches_large_kitchen_memory_as_ingredient_subsets()
    {
        const string sourceUrl = "https://publisher.example.test/chicken-pepper-skillet";
        var handler = new CapturingHandler(
            ResponseWithIngredients(sourceUrl, "Chicken pepper skillet", "chicken breast", "bell pepper", "onion"));
        var client = CreateClient(handler);

        var response = await client.FindRecipesAsync(
            KitchenMemoryRequest(onlyUseAvailableIngredients: false),
            CancellationToken.None);

        Assert.Single(response.Recipes);
        Assert.Contains("pantry options", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requirement to find one dish", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        using var requestDocument = JsonDocument.Parse(handler.RequestBody);
        using var inputDocument = JsonDocument.Parse(requestDocument.RootElement.GetProperty("input").GetString()!);
        Assert.False(inputDocument.RootElement.GetProperty("onlyUseAvailableIngredients").GetBoolean());
        Assert.Contains(
            "do not require one recipe to contain the entire pantry",
            inputDocument.RootElement.GetProperty("ingredientSubsetStrategy").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlimited_time_does_not_reject_a_sourced_recipe_or_send_a_numeric_limit()
    {
        const string sourceUrl = "https://publisher.example.test/slow-stew";
        var handler = new CapturingHandler(Response(sourceUrl, sourceUrl, "Cabernet Sauvignon"));
        var client = CreateClient(handler);

        var response = await client.FindRecipesAsync(
            Request(maxCookingMinutes: 0),
            CancellationToken.None);

        Assert.Single(response.Recipes);
        using var requestDocument = JsonDocument.Parse(handler.RequestBody);
        using var inputDocument = JsonDocument.Parse(requestDocument.RootElement.GetProperty("input").GetString()!);
        Assert.Equal(JsonValueKind.Null, inputDocument.RootElement.GetProperty("maximumCookingMinutes").ValueKind);
        Assert.Contains("Unlimited", inputDocument.RootElement.GetProperty("cookingTimeLimit").GetString(), StringComparison.Ordinal);
    }

    private static AzureGroundedRecipeClient CreateClient(
        HttpMessageHandler handler,
        int minimumResultCount = 1,
        int maxSearchAttempts = 1,
        TestPromptProvider? prompts = null)
    {
        var foodOptions = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions
        {
            AzureOpenAI = new AzureOpenAiOptions
            {
                Endpoint = "https://test.openai.azure.com",
                ApiKey = "test-key",
                Deployment = "gpt-test"
            }
        });
        var catalogOptions = Microsoft.Extensions.Options.Options.Create(new RecipeCatalogOptions
        {
            AzureWebSearch = new AzureWebSearchOptions
            {
                MinimumResultCount = minimumResultCount,
                MaxSearchAttempts = maxSearchAttempts
            }
        });
        var normalizer = new IngredientNormalizer();
        return new AzureGroundedRecipeClient(
            new HttpClient(handler),
            foodOptions,
            catalogOptions,
            prompts ?? new TestPromptProvider(),
            normalizer,
            new RecipeRankingService(normalizer),
            NullLogger<AzureGroundedRecipeClient>.Instance);
    }

    private static GenerateRecipesRequest Request(
        string diet = "Anything",
        int maxCookingMinutes = 90,
        bool onlyUseAvailableIngredients = false) => new()
    {
        Ingredients = [new IngredientInput("lamb", "500 g")],
        DietaryPreference = diet,
        MaxCookingMinutes = maxCookingMinutes,
        Servings = 2,
        OnlyUseAvailableIngredients = onlyUseAvailableIngredients
    };

    private static GenerateRecipesRequest KitchenMemoryRequest(bool onlyUseAvailableIngredients = true) => new()
    {
        Ingredients =
        [
            new IngredientInput("red bell pepper", "2"),
            new IngredientInput("yellow bell pepper", "1"),
            new IngredientInput("onion", "1 onion"),
            new IngredientInput("tomato", "2"),
            new IngredientInput("potato", "3"),
            new IngredientInput("spinach", "1 small bunch"),
            new IngredientInput("raw chicken breast", "1 piece"),
            new IngredientInput("sliced bread", "5 slices"),
            new IngredientInput("brown eggs", "8 eggs"),
            new IngredientInput("pickled vegetables jar", "1 jar"),
            new IngredientInput("butter block", "1 block"),
            new IngredientInput("bottle of milk", "1 bottle"),
            new IngredientInput("yogurt tub", "1 tub"),
            new IngredientInput("mustard jar", "1 jar"),
            new IngredientInput("mayonnaise jar", "1 jar"),
            new IngredientInput("cream cheese box", "1 box"),
            new IngredientInput("black olives jar", "1 jar"),
            new IngredientInput("sliced cheese pack", "1 pack"),
            new IngredientInput("packaged cooked meat slices", "1 pack"),
            new IngredientInput("small round cheese wheel", "1 wheel"),
            new IngredientInput("bottle of soy sauce", "1 bottle"),
            new IngredientInput("bottle of salad dressing", "1 bottle"),
            new IngredientInput("bottle of juice", "4 bottles"),
            new IngredientInput("lemon", "1 lemon"),
            new IngredientInput("red apple", "1 apple"),
            new IngredientInput("cucumber", "1 cucumber"),
            new IngredientInput("packaged nuts or granola bowl", "1 bowl"),
            new IngredientInput("packaged sliced bread", "1 pack")
        ],
        DietaryPreference = "Anything",
        MaxCookingMinutes = 45,
        Servings = 2,
        OnlyUseAvailableIngredients = onlyUseAvailableIngredients
    };

    private static string Response(
        string citedUrl,
        string recipeUrl,
        string winePairing,
        bool wrapInText = false,
        string[]? tags = null)
    {
        var recipePayload = JsonSerializer.Serialize(new
        {
            recipes = new[]
            {
                new
                {
                    title = "Publisher lamb stew",
                    cookingMinutes = 75,
                    difficulty = "Medium",
                    cuisine = "British",
                    servings = 2,
                    tags = tags ?? ["Dinner"],
                    ingredients = new[]
                    {
                        new
                        {
                            amount = "500 g",
                            name = "lamb",
                            quantity = (double?)500,
                            unit = "g",
                            originalText = "500 g lamb"
                        }
                    },
                    cookingGuideSteps = new[]
                    {
                        "Brown the lamb in a heavy pan for 6 to 8 minutes.",
                        "Simmer until tender and confirm the meat is safely cooked before serving."
                    },
                    sourceUrl = recipeUrl,
                    winePairing
                }
            }
        });

        var outputText = wrapInText
            ? $"I found a cited recipe.\n```json\n{recipePayload}\n```"
            : recipePayload;
        return TextResponse(citedUrl, outputText);
    }

    private static string ResponseWithIngredients(
        string citedUrl,
        string title,
        params string[] ingredients)
    {
        var recipePayload = JsonSerializer.Serialize(new
        {
            recipes = new[]
            {
                new
                {
                    title,
                    cookingMinutes = 15,
                    difficulty = "Easy",
                    cuisine = "British",
                    servings = 2,
                    tags = new[] { "Quick" },
                    ingredients = ingredients.Select(name => new
                    {
                        amount = "As listed",
                        name,
                        quantity = (double?)null,
                        unit = (string?)null,
                        originalText = name
                    }),
                    cookingGuideSteps = new[]
                    {
                        "Prepare the listed ingredients as described by the source.",
                        "Cook the dish and verify eggs or meat are safely cooked before serving."
                    },
                    sourceUrl = citedUrl,
                    winePairing = "A light sparkling wine is a rough match."
                }
            }
        });
        return TextResponse(citedUrl, recipePayload);
    }

    private static string TextResponse(string citedUrl, string outputText) =>
        JsonSerializer.Serialize(new
        {
            output = new object[]
            {
                new
                {
                    type = "web_search_call",
                    action = new { sources = new[] { new { type = "url", url = citedUrl } } }
                },
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = outputText,
                            annotations = new[] { new { type = "url_citation", url = citedUrl } }
                        }
                    }
                }
            }
        });

    private sealed class CapturingHandler(params string[] payloads) : HttpMessageHandler
    {
        private readonly Queue<string> _payloads = new(payloads);

        public string RequestBody { get; private set; } = string.Empty;
        public List<string> RequestBodies { get; } = [];
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("api-key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(RequestBody);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payloads.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }
}
