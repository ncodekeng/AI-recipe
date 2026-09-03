using System.Net;
using System.Text;
using System.Text.Json;
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
        var handler = new CapturingHandler(JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "{\"ingredients\":[],\"ignoredImages\":[]}" } }
            }
        }));
        var options = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions
        {
            AzureOpenAI = new AzureOpenAiOptions
            {
                Endpoint = "https://test.openai.azure.com",
                ApiKey = "test-key",
                Deployment = "gpt-test"
            }
        });
        var client = new AzureOpenAiClient(
            new HttpClient(handler),
            options,
            new TestPromptProvider(ingredientRecognitionPrompt: customPrompt));

        await client.AnalyzeIngredientsAsync(
            [new UploadedPhoto("fridge.jpg", "image/jpeg", [1, 2, 3])],
            CancellationToken.None);

        Assert.Contains(customPrompt, handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("cannot override these rules", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("response_format", handler.RequestBody, StringComparison.Ordinal);
    }

    private sealed class CapturingHandler(string payload) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
