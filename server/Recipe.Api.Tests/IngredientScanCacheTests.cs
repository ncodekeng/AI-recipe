using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class IngredientScanCacheTests
{
    [Fact]
    public void Same_client_and_photo_reuses_azure_result_without_retaining_photo_bytes()
    {
        var cache = CreateCache();
        var originalPhoto = Photo("first-name.jpg", [1, 2, 3, 4]);
        var samePhoto = Photo("first-name.jpg", [1, 2, 3, 4]);
        var response = Response();

        cache.Store("client-one", [originalPhoto], response);

        Assert.True(cache.TryGet("client-one", [samePhoto], out var cached));
        Assert.Same(response, cached);
    }

    [Fact]
    public void Cached_scan_results_are_isolated_per_client()
    {
        var cache = CreateCache();
        var photo = Photo("fridge.jpg", [5, 6, 7, 8]);
        cache.Store("client-one", [photo], Response());

        Assert.False(cache.TryGet("client-two", [photo], out _));
    }

    [Fact]
    public void Demo_results_are_not_cached()
    {
        var cache = CreateCache();
        var photo = Photo("fridge.jpg", [9, 10, 11]);
        cache.Store("client-one", [photo], Response() with { Provider = "Demo" });

        Assert.False(cache.TryGet("client-one", [photo], out _));
    }

    [Fact]
    public void Partial_results_are_not_cached()
    {
        var cache = CreateCache();
        var photo = Photo("fridge.jpg", [9, 10, 11]);
        cache.Store(
            "client-one",
            [photo],
            Response() with { FailedPhotos = ["freezer.jpg"] });

        Assert.False(cache.TryGet("client-one", [photo], out _));
    }

    [Fact]
    public void Cache_does_not_cross_prompt_revisions()
    {
        var prompts = new TestPromptProvider();
        var cache = CreateCache(prompts);
        var photo = Photo("fridge.jpg", [12, 13, 14]);
        cache.Store("client-one", [photo], Response());

        prompts.ChangeRevision("test-prompts-v2");

        Assert.False(cache.TryGet("client-one", [photo], out _));
    }

    private static IngredientScanCache CreateCache(TestPromptProvider? prompts = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new FoodAiOptions
        {
            Provider = "AzureOpenAI",
            AzureOpenAI = new AzureOpenAiOptions { Deployment = "vision-test" },
            ScanCache = new IngredientScanCacheOptions
            {
                Enabled = true,
                DurationHours = 168
            }
        });
        return new IngredientScanCache(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 }),
            options,
            prompts ?? new TestPromptProvider(),
            NullLogger<IngredientScanCache>.Instance);
    }

    private static UploadedPhoto Photo(string name, byte[] content) =>
        new(name, "image/jpeg", content);

    private static IngredientAnalysisResponse Response() => new(
        [new DetectedIngredient(Guid.NewGuid(), "Lamb", "500 g", 92, "fridge.jpg")],
        "Azure OpenAI");
}
