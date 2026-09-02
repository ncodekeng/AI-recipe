using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Tests;

public sealed class AiUsageGuardTests
{
    [Fact]
    public void Development_reset_restores_the_clients_allowance()
    {
        var guard = new AiUsageGuard(
            Microsoft.Extensions.Options.Options.Create(new UsageControlOptions
            {
                AllowTestReset = true,
                DailyScanLimit = 1,
                DailyRecipeLimit = 1
            }),
            TimeProvider.System);

        using (guard.TryAcquire("test-client", AiOperation.IngredientScan).Lease)
        {
        }
        using (guard.TryAcquire("test-client", AiOperation.RecipeGeneration).Lease)
        {
        }

        var reset = guard.ResetClient("test-client");

        Assert.Equal(1, reset.ScansRemaining);
        Assert.Equal(1, reset.RecipesRemaining);
        Assert.True(reset.CanReset);
    }
}
