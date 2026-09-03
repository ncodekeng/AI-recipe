using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Recipe.Api.Controllers;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class PromptConfigurationStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"plate-prompt-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Custom_prompts_are_persisted_and_reloaded()
    {
        var firstStore = CreateStore();
        var originalRevision = firstStore.Current.Revision;

        var saved = await firstStore.UpdateAsync(
            "Recognise visible ingredients and describe package sizes clearly.",
            "Prefer quick family dinners with the fewest missing ingredients.",
            CancellationToken.None);
        var reloaded = CreateStore().Current;

        Assert.False(saved.UsingDefaults);
        Assert.NotEqual(originalRevision, saved.Revision);
        Assert.Equal(saved.IngredientRecognitionPrompt, reloaded.IngredientRecognitionPrompt);
        Assert.Equal(saved.RecipeRecommendationPrompt, reloaded.RecipeRecommendationPrompt);
        Assert.Equal(saved.Revision, reloaded.Revision);
    }

    [Fact]
    public void Admin_endpoint_rejects_an_incorrect_key()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PromptAdminOptions
        {
            Enabled = true,
            ApiKey = "correct-secret",
            StoragePath = StoragePath
        });
        var controller = new PromptAdminController(
            CreateStore(options),
            options,
            NullLogger<PromptAdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["X-Plate-Admin-Key"] = "incorrect-secret";

        var result = controller.Get();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Admin_endpoint_saves_valid_prompts_with_the_correct_key()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PromptAdminOptions
        {
            Enabled = true,
            ApiKey = "correct-secret",
            StoragePath = StoragePath
        });
        var store = CreateStore(options);
        var controller = new PromptAdminController(
            store,
            options,
            NullLogger<PromptAdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["X-Plate-Admin-Key"] = "correct-secret";

        var result = await controller.Update(new()
        {
            IngredientRecognitionPrompt = "Recognise visible ingredients and estimate useful quantities.",
            RecipeRecommendationPrompt = "Prefer practical family recipes with very few missing ingredients."
        }, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<Recipe.Api.Models.AiPromptSettingsResponse>(response.Value);
        Assert.False(store.Current.UsingDefaults);
        Assert.True(File.Exists(StoragePath));
    }

    public void Dispose()
    {
        if (File.Exists(StoragePath))
        {
            File.Delete(StoragePath);
        }
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory);
        }
    }

    private string StoragePath => Path.Combine(_testDirectory, "prompts.json");

    private PromptConfigurationStore CreateStore() => CreateStore(
        Microsoft.Extensions.Options.Options.Create(new PromptAdminOptions
        {
            Enabled = true,
            ApiKey = "correct-secret",
            StoragePath = StoragePath
        }));

    private static PromptConfigurationStore CreateStore(
        Microsoft.Extensions.Options.IOptions<PromptAdminOptions> options) => new(
            options,
            new TestHostEnvironment(),
            TimeProvider.System,
            NullLogger<PromptConfigurationStore>.Instance);
}
