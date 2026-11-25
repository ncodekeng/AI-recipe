using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class AzureOpenAiClientTests
{
    [Fact]
    public async Task Uses_admin_recognition_guidance_without_replacing_locked_rules()
    {
        const string customPrompt = "Prioritise exact produce varieties and practical package quantities.";
        var handler = new RoutingHandler(_ => Success("{\"ingredients\":[],\"ignoredImage\":true}"));
        var client = CreateClient(handler, ingredientRecognitionPrompt: customPrompt);

        await client.AnalyzeIngredientsAsync(
            [new UploadedPhoto("fridge.jpg", "image/jpeg", [1, 2, 3])],
            CancellationToken.None);

        var requestBody = Assert.Single(handler.RequestBodies);
        Assert.Contains(customPrompt, requestBody, StringComparison.Ordinal);
        Assert.Contains("cannot override these rules", requestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("response_format", requestBody, StringComparison.Ordinal);
        Assert.Contains("systematically and exhaustively", requestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without brand, package, container, size", requestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analyses_each_photo_separately_at_high_detail_and_merges_duplicates()
    {
        var handler = new RoutingHandler(body => body.Contains("first.jpg", StringComparison.Ordinal)
            ? Success("""
                {"ingredients":[
                  {"name":"Tomatoes","quantity":"2","confidence":88,"kind":"Ingredient"}
                ],"ignoredImage":false}
                """)
            : Success("""
                {"ingredients":[
                  {"name":"Tomato","quantity":3,"confidence":0.96,"kind":"Ingredient"},
                  {"name":"Carrots","quantity":"1 bag","confidence":"91","kind":"Ingredient"}
                ],"ignoredImage":false}
                """));
        var client = CreateClient(handler, maxOutputTokens: 3500, maxParallelImages: 2);

        var result = await client.AnalyzeIngredientsAsync(
            [
                new UploadedPhoto("first.jpg", "image/jpeg", [1, 2, 3]),
                new UploadedPhoto("second.jpg", "image/jpeg", [4, 5, 6])
            ],
            CancellationToken.None);

        Assert.Equal(2, handler.RequestBodies.Count);
        foreach (var body in handler.RequestBodies)
        {
            using var request = JsonDocument.Parse(body);
            var root = request.RootElement;
            Assert.Equal(3500, root.GetProperty("max_completion_tokens").GetInt32());
            var userContent = root.GetProperty("messages")[1].GetProperty("content");
            var image = Assert.Single(
                userContent.EnumerateArray(),
                item => item.GetProperty("type").GetString() == "image_url");
            Assert.Equal("high", image.GetProperty("image_url").GetProperty("detail").GetString());
        }

        Assert.Equal(2, result.Ingredients.Count);
        var tomato = Assert.Single(result.Ingredients, item => item.Name == "Tomato");
        Assert.Equal(96, tomato.Confidence);
        Assert.Equal("3", tomato.Quantity);
        Assert.Equal("second.jpg", tomato.SourceImage);
        Assert.Contains(result.Ingredients, item => item.Name == "Carrots");
        Assert.Empty(result.FailedPhotos!);
    }

    [Fact]
    public async Task Merges_semantic_duplicates_but_preserves_meaningful_variants()
    {
        var handler = new RoutingHandler(body => body.Contains("first.jpg", StringComparison.Ordinal)
            ? Success("""
                {"ingredients":[
                  {"name":"Brown eggs","quantity":"6","confidence":88,"kind":"Ingredient"},
                  {"name":"Fresh spinach","quantity":"1 bag","confidence":90,"kind":"Ingredient"},
                  {"name":"Red bell peppers","quantity":"2","confidence":91,"kind":"Ingredient"},
                  {"name":"Dijon Mustard (small jar)","quantity":"1 jar","confidence":89,"kind":"Ingredient"}
                ],"ignoredImage":false}
                """)
            : Success("""
                {"ingredients":[
                  {"name":"Eggs","quantity":"8","confidence":96,"kind":"Ingredient"},
                  {"name":"Spinach","quantity":"2 bags","confidence":95,"kind":"Ingredient"},
                  {"name":"Yellow bell pepper","quantity":"1","confidence":92,"kind":"Ingredient"},
                  {"name":"Dijon Mustard (larger jar)","quantity":"1 jar","confidence":97,"kind":"Ingredient"}
                ],"ignoredImage":false}
                """));
        var client = CreateClient(handler);

        var result = await client.AnalyzeIngredientsAsync(
            [
                new UploadedPhoto("first.jpg", "image/jpeg", [1, 2, 3]),
                new UploadedPhoto("second.jpg", "image/jpeg", [4, 5, 6])
            ],
            CancellationToken.None);

        Assert.Equal(5, result.Ingredients.Count);
        Assert.Single(result.Ingredients, item => item.Name == "Eggs" && item.Quantity == "8");
        Assert.Single(result.Ingredients, item => item.Name == "Spinach" && item.Quantity == "2 bags");
        Assert.Contains(result.Ingredients, item => item.Name == "Red bell peppers");
        Assert.Contains(result.Ingredients, item => item.Name == "Yellow bell pepper");
        Assert.Single(result.Ingredients, item => item.Name == "Dijon Mustard (larger jar)");
    }

    [Fact]
    public async Task Keeps_successful_results_when_one_photo_fails()
    {
        var handler = new RoutingHandler(body => body.Contains("failed.jpg", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited", Encoding.UTF8, "text/plain")
            }
            : Success("""
                {"ingredients":[
                  {"name":"Spinach","quantity":"1 bag","confidence":90,"kind":"Ingredient"}
                ],"ignoredImage":false}
                """));
        var client = CreateClient(handler);

        var result = await client.AnalyzeIngredientsAsync(
            [
                new UploadedPhoto("good.jpg", "image/jpeg", [1, 2, 3]),
                new UploadedPhoto("failed.jpg", "image/jpeg", [4, 5, 6])
            ],
            CancellationToken.None);

        Assert.Single(result.Ingredients);
        Assert.Equal(["failed.jpg"], result.FailedPhotos);
        Assert.Contains("1 photo could not be analysed", result.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Azure OpenAI", result.Provider);
    }

    [Fact]
    public async Task A_photo_the_safety_filter_refuses_is_reported_as_refused()
    {
        // Measured on 25 November 2026: two photos were refused three times over, and the app said only
        // "An error occurred while processing your request", so the same photos were sent again twice for
        // nothing. A refusal is the one photo failure the customer can act on, and a retry never helps.
        var handler = new RoutingHandler(_ => Refused());
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<PhotoRefusedException>(() =>
            client.AnalyzeIngredientsAsync(
                [new UploadedPhoto("brisket.jpg", "image/jpeg", [1, 2, 3])],
                CancellationToken.None));

        // The message carries no part of Azure's response: that body quotes the request back.
        Assert.DoesNotContain("content_policy", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task One_refused_photo_does_not_lose_the_others()
    {
        // Somebody photographing a raw joint and a vegetable drawer should still get the vegetables.
        var handler = new RoutingHandler(body => body.Contains("brisket.jpg", StringComparison.Ordinal)
            ? Refused()
            : Success("""
                {"ingredients":[
                  {"name":"Carrots","quantity":"4","confidence":90,"kind":"Ingredient"}
                ],"ignoredImage":false}
                """));
        var client = CreateClient(handler);

        var result = await client.AnalyzeIngredientsAsync(
            [
                new UploadedPhoto("brisket.jpg", "image/jpeg", [1, 2, 3]),
                new UploadedPhoto("drawer.jpg", "image/jpeg", [4, 5, 6]),
            ],
            CancellationToken.None);

        Assert.Contains(result.Ingredients, ingredient =>
            ingredient.Name.Contains("Carrot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_failure_that_is_not_a_refusal_is_not_reported_as_one()
    {
        // A broken deployment, a bad key or a rate limit is our problem, not a photo the customer can
        // retake. Telling them to try a different picture would send them chasing the wrong thing.
        var handler = new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("upstream exploded", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.AnalyzeIngredientsAsync(
                [new UploadedPhoto("fridge.jpg", "image/jpeg", [1, 2, 3])],
                CancellationToken.None));

        Assert.IsNotType<PhotoRefusedException>(exception);
    }

    /// <summary>Azure's own response when its content safety filter refuses an image.</summary>
    private static HttpResponseMessage Refused() => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent(
            """
            {"error":{"message":"Your input image may contain content that is not allowed by our content safety system.","type":"invalid_request_error","param":null,"code":"content_policy_violation"}}
            """,
            Encoding.UTF8,
            "application/json"),
    };

    private static AzureOpenAiClient CreateClient(
        HttpMessageHandler handler,
        string? ingredientRecognitionPrompt = null,
        int maxOutputTokens = 4000,
        int maxParallelImages = 2)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions
        {
            AzureOpenAI = new AzureOpenAiOptions
            {
                Endpoint = "https://test.openai.azure.com",
                ApiKey = "test-key",
                Deployment = "gpt-test",
                ImageDetail = "high",
                MaxOutputTokensPerImage = maxOutputTokens,
                MaxParallelImages = maxParallelImages
            }
        });
        return new AzureOpenAiClient(
            new HttpClient(handler),
            options,
            new TestPromptProvider(ingredientRecognitionPrompt: ingredientRecognitionPrompt),
            NullLogger<AzureOpenAiClient>.Instance);
    }

    private static HttpResponseMessage Success(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content } } }
            }),
            Encoding.UTF8,
            "application/json")
    };

    private sealed class RoutingHandler(Func<string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public ConcurrentBag<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(requestBody);
            return responder(requestBody);
        }
    }
}
