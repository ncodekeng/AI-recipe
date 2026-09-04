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

    [Fact]
    public void Authenticated_admin_is_not_blocked_by_the_per_browser_limit()
    {
        var guard = new AiUsageGuard(
            Microsoft.Extensions.Options.Options.Create(new UsageControlOptions
            {
                DailyRecipeLimit = 1,
                GlobalDailyBudgetUsd = 100
            }),
            TimeProvider.System);

        using (guard.TryAcquire("admin-client", AiOperation.RecipeGeneration, hasUnlimitedQuota: true).Lease)
        {
        }
        using var second = guard.TryAcquire(
            "admin-client",
            AiOperation.RecipeGeneration,
            hasUnlimitedQuota: true).Lease;

        Assert.NotNull(second);
        var status = guard.GetStatus("admin-client", hasUnlimitedQuota: true);
        Assert.True(status.IsUnlimited);
        Assert.Equal(2, status.RecipesUsed);
    }

    [Fact]
    public void Authenticated_admin_does_not_bypass_the_AI_kill_switch()
    {
        var guard = new AiUsageGuard(
            Microsoft.Extensions.Options.Options.Create(new UsageControlOptions
            {
                AiEnabled = false,
                DailyRecipeLimit = 1
            }),
            TimeProvider.System);

        var admission = guard.TryAcquire(
            "admin-client",
            AiOperation.RecipeGeneration,
            hasUnlimitedQuota: true);

        Assert.False(admission.Allowed);
        Assert.Equal(503, admission.Rejection?.StatusCode);
    }
}
